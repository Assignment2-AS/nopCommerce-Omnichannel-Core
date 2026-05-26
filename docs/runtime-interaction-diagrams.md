# Runtime interaction diagrams

**Author:** Guilherme Silva | Architect  
**Scenario:** Scenario C — Omnichannel Commerce Core  
**Last updated:** 2026-05-18

The three diagrams below document the observable runtime behaviour during normal operation, WMS degradation, and automatic recovery after the fault is removed.

---

## Diagram 1 — Normal flow

```mermaid
sequenceDiagram
    actor Customer
    participant nopCommerce
    participant OutboxDB as Outbox Table (MSSQL)
    participant OutboxSvc as OutboxPublisherService
    participant RabbitMQ
    participant OrderSync as OrderSyncAdapter
    participant WireMock as WireMock (ERP + WMS)

    Customer->>nopCommerce: POST /checkout/confirm
    nopCommerce->>OutboxDB: BEGIN TRANSACTION
    note right of OutboxDB: INSERT Order (nopCommerce tables)<br/>INSERT OutboxMessage (order.placed, ProcessedAt=NULL)<br/>COMMIT — atomic
    nopCommerce-->>Customer: 200 OK — Order confirmed

    loop Every 2 seconds
        OutboxSvc->>OutboxDB: SELECT WHERE ProcessedAt IS NULL
        OutboxDB-->>OutboxSvc: OutboxMessage #N (CorrelationId = OrderId)
        OutboxSvc->>RabbitMQ: Publish order.placed (persistent, durable queue)
        RabbitMQ-->>OutboxSvc: Publisher confirm
        OutboxSvc->>OutboxDB: UPDATE ProcessedAt = NOW()
    end

    RabbitMQ->>OrderSync: Deliver order.placed message
    OrderSync->>WireMock: POST /api/orders (ERP) — body: {orderId, items, total}
    WireMock-->>OrderSync: 200 OK — {status: "received"}
    OrderSync->>WireMock: GET /api/stock/{productId} (WMS)
    WireMock-->>OrderSync: 200 OK — {quantity: 95, status: "IN_STOCK"}
    OrderSync->>RabbitMQ: ACK message (removed from queue)
```

The checkout never calls ERP or WMS directly. The Outbox write is atomic with the order write, so there is no window where an order exists without a corresponding event. OrderSyncAdapter is the only component that crosses the integration boundary.

---

## Diagram 2 — Degraded flow (WMS unavailable)

```mermaid
sequenceDiagram
    actor Operator
    actor Customer
    participant nopCommerce
    participant OutboxSvc as OutboxPublisherService
    participant RabbitMQ
    participant DLQ as order.placed.dlq
    participant OrderSync as OrderSyncAdapter
    participant WireMock as WireMock (WMS — fault active)

    Operator->>WireMock: activate-wms-failure.sh<br/>(injects 503 on /api/stock/*)
    note right of WireMock: Priority-1 mapping overrides normal stub

    Customer->>nopCommerce: POST /checkout/confirm
    note right of nopCommerce: Order + OutboxMessage written atomically<br/>No WMS call made at checkout time
    nopCommerce-->>Customer: 200 OK — Order confirmed

    OutboxSvc->>RabbitMQ: Publish order.placed

    RabbitMQ->>OrderSync: Deliver order.placed

    OrderSync->>WireMock: POST /api/orders (ERP) → 200 OK
    note right of OrderSync: ERP call succeeds — ERP is not affected

    OrderSync->>WireMock: GET /api/stock/{id} (WMS)
    WireMock-->>OrderSync: 503 Service Unavailable

    note right of OrderSync: Polly retry #1 — wait 1s
    OrderSync->>WireMock: GET /api/stock/{id}
    WireMock-->>OrderSync: 503

    note right of OrderSync: Polly retry #2 — wait 2s
    OrderSync->>WireMock: GET /api/stock/{id}
    WireMock-->>OrderSync: 503

    note right of OrderSync: Polly retry #3 — wait 4s — exhausted
    OrderSync->>DLQ: Publish to order.placed.dlq (with original payload + CorrelationId)
    note right of OrderSync: After 5 consecutive failures across messages:<br/>Circuit breaker OPENS<br/>Subsequent WMS calls bypass network<br/>Return stale stock value with stale:true indicator

    note over nopCommerce: Checkout remains fully operational<br/>No customer-visible degradation
    note over RabbitMQ: order.placed queue: new messages accumulate<br/>order.placed.dlq: failed messages visible in Management UI
```

Checkout success is 100% regardless of WMS state (QAS-1). Polly retries absorb transient failures before a message moves to the DLQ. After 5 consecutive failures across messages, the circuit breaker opens and WMS calls skip the network, returning a stale cached value. Messages are not lost — the DLQ is durable and visible in the RabbitMQ Management UI.

---

## Diagram 3 — Recovery flow

```mermaid
sequenceDiagram
    actor Operator
    participant WireMock as WireMock (WMS — fault removed)
    participant OrderSync as OrderSyncAdapter
    participant RecSvc as ReconciliationService
    participant DLQ as order.placed.dlq
    participant RabbitMQ

    Operator->>WireMock: restore-wms.sh<br/>(DELETE fault mapping f1000001-*)
    note right of WireMock: Normal stub reactivated<br/>GET /api/stock/* → 200 OK

    note over OrderSync: Circuit breaker: HALF-OPEN state (after 30s)
    OrderSync->>WireMock: GET /api/stock/{id} (probe request)
    WireMock-->>OrderSync: 200 OK
    note over OrderSync: Circuit breaker: CLOSED

    note over RecSvc: WMS recovery detected — circuit breaker closed event
    RecSvc->>DLQ: Consume messages ordered by CreatedAt ASC

    loop For each message in DLQ
        RecSvc->>RecSvc: Check: has OrderId already been forwarded?
        note right of RecSvc: Idempotency check via CorrelationId store
        RecSvc->>WireMock: POST /api/orders (ERP)
        WireMock-->>RecSvc: 200 OK
        RecSvc->>WireMock: GET /api/stock/{id} (WMS)
        WireMock-->>RecSvc: 200 OK — {quantity: 95}
        RecSvc->>DLQ: ACK message
    end

    note over WireMock: Request journal shows:<br/>1 POST /api/orders per OrderId (no duplicates)<br/>Verify at /__admin/requests
    note over RabbitMQ: order.placed.dlq: empty<br/>order.placed: normal processing resumed
```

Recovery is fully automatic once the fault is removed (QAS-3). ReconciliationService processes messages in creation order and checks each OrderId before forwarding, so duplicate ERP calls are not possible even if a message was partially processed during the outage. The WireMock request journal at `/__admin/requests` shows exactly one `POST /api/orders` per OrderId — use this to verify idempotency during the demo.
