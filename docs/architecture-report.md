# Architecture Report — VerdeMart Omnichannel Commerce Core

**Author:** Guilherme Silva | Architect  
**Scenario:** Scenario C — Omnichannel Commerce Core  
**Last updated:** 2026-05-18  
**Status:** In progress (Part 2)

---

## 1. Context and scenario

VerdeMart Retail runs nopCommerce as a web storefront and wants to expand its role into a wider operational ecosystem: WMS, ERP, and store operations should all sync with it in both directions. The design question is what the commerce core does when those surrounding systems are slow, inconsistent, or temporarily unavailable, and how to keep that degradation away from the customer.

Full scenario motivation and business context: [scenario-and-business-drivers.md](scenario-and-business-drivers.md)

---

## 2. Current-state analysis

nopCommerce is a modular monolith built in C# / ASP.NET Core with a layered / onion-style structure. It works well as a standalone store. The gaps that matter for this scenario are structural, not functional:

| Pressure Point | Current Behaviour | Why It Fails Here |
|---|---|---|
| PP-1: No async boundary | Any external integration would run synchronously inside the HTTP request | A slow or unavailable WMS/ERP propagates directly into checkout failure |
| PP-2: No outbound event mechanism | No built-in way to publish order or stock events | External systems cannot react without polling |
| PP-3: No inbound state contract | No defined integration point for external systems to push state back | Cross-channel stock visibility requires a defined inbound path |
| PP-4: No degraded-mode behaviour | No circuit-breaker or fallback logic | Failures propagate rather than being contained |
| PP-5: No reconciliation mechanism | No process to resolve inconsistencies after an outage | Data remains diverged after recovery unless an operator intervenes |

The Outbox table (`Integration_OutboxMessage`) and the `OrderSyncAdapter` service address all five pressure points.

---

## 3. Business drivers and architectural drivers

Three goals drive the evolution, ordered by priority:

- SG-1: Decouple order acceptance from operational system availability
- SG-2: Achieve cross-channel visibility of stock, order state, and fulfillment progress
- SG-3: Remain operationally useful during and after external system degradation

These map to five architectural drivers:

| Driver | Quality Attribute | Priority |
|---|---|---|
| Commerce core available when external systems fail | Availability | Critical |
| Order events not silently lost | Reliability | Critical |
| Integration concerns isolated from storefront | Deployability | High |
| Stock state converges across channels | Consistency | High |
| Automatic recovery after dependency outage | Recoverability | High |

---

## 4. Bounded contexts and responsibilities

| Context | Responsibility | Lives In |
|---|---|---|
| Commerce Core | Order placement, cart, catalog, customer-facing checkout | nopCommerce monolith |
| Outbox / Event Publishing | Reliable at-least-once delivery of `order.placed` events to RabbitMQ | nopCommerce plugin (`Nop.Plugin.Integration.OrderPublisher`) |
| WMS Stock Sync | Consumes `wms.stock.update` messages from RabbitMQ and applies stock deltas to nopCommerce products via `AdjustInventoryAsync` | nopCommerce plugin (`WmsStockSyncService`) |
| Order Synchronisation | Consuming events, calling ERP/WMS, retry, dead-letter, reconciliation | `OrderSyncAdapter` (independent Worker Service) |
| ERP stub | Receiving and acknowledging orders from the integration layer | WireMock (`POST /api/orders`) |
| WMS stub | Receiving order sync calls and stock queries from the integration layer | WireMock (`POST /api/wms/orders`, `GET /api/stock/{id}`) |

See also: [bounded-context-view.md](bounded-context-view.md) (Mermaid, Part 2), [Domain Model & Bounded Contexts.jpg](Domain%20Model%20%26%20Bounded%20Contexts.jpg) (Part 1), [Target Architecture.jpg](Target%20Architecture.jpg)

---

## 5. What remains inside the monolith and why

Order placement, catalog, cart, checkout, and customer identity stay in the nopCommerce monolith. This is a deliberate design decision.

Order placement is the commerce core's primary responsibility and touches nearly everything in the nopCommerce data model: cart, inventory, customer, payment. Extracting it would require exposing or replicating that model externally, creating exactly the kind of hidden cross-boundary coupling the design is trying to avoid. The Outbox Pattern lets the monolith publish events without becoming distributed: it writes to a local table atomically with the order, and a background service handles delivery independently.

Stock management stays in the monolith for a different reason. From the commerce core's perspective, stock visibility (QAS-4) is read-only during degradation — it serves a stale value with an indicator. There is no strong consistency requirement that would justify extracting this into a separate deployable.

The boundary with OrderSyncAdapter is enforced structurally, not just by convention. The project has no references to any `Nop.*` library and no connection to the nopCommerce database. The only coupling point is the `order.placed` queue in RabbitMQ, a versioned interface.

---

## 6. Architectural evolution path

```
Current state                         Target state (Part 2)
─────────────────────                 ─────────────────────────────────────
nopCommerce monolith                  nopCommerce monolith
  └─ Order placed                       └─ Order placed
  └─ (no outbound events)               └─ OutboxMessage written (atomic)
  └─ (no ERP/WMS integration)           └─ OutboxPublisherService → RabbitMQ

                                      OrderSyncAdapter (independent container)
                                        └─ Consumes order.placed queue
                                        └─ POST /api/orders → WireMock ERP
                                        └─ Polly retry + exponential backoff
                                        └─ Dead-letter queue on exhaustion
                                        └─ Circuit breaker for WMS calls
                                        └─ ReconciliationService on recovery

                                      WireMock (independent container)
                                        └─ ERP stub (POST /api/orders)
                                        └─ WMS stub (GET /api/stock/{id})
                                        └─ Fault injection profiles (Part 2)
```

Only the integration boundary is extracted. The commerce core keeps its monolithic structure, which preserves its existing reliability and avoids introducing distributed consistency problems inside nopCommerce itself.

---

## 7. How ADD was applied

ADD (Attribute-Driven Design) was chosen because the problem is about quality attributes, not missing features. nopCommerce already places orders and tracks inventory. Every structural decision derives from a quality attribute scenario.

The process had three steps:

1. Identify drivers from business analysis: five pressure points (PP-1 to PP-5), three strategic goals (SG-1 to SG-3)
2. Formalise as QA scenarios: five scenarios (QAS-1 to QAS-5) with measurable response conditions
3. Derive decisions from scenarios: each scenario drove one or more ADRs

Full methodology: [add-framework.md](add-framework.md)

---

## 8. Runtime interaction diagrams

Three sequence diagrams document the observable runtime behaviour: normal flow, degraded flow (WMS unavailable), and recovery flow (circuit breaker closes, DLQ drains).

Full diagrams: [runtime-interaction-diagrams.md](runtime-interaction-diagrams.md)

---

## 9. Cross-cutting concerns

| Concern | Decision | Where Applied |
|---|---|---|
| Correlation / traceability | Every `order.placed` message carries a `CorrelationId` derived from `OrderId`; visible in RabbitMQ Management UI and WireMock request journal | OutboxMessage table, OrderSyncAdapter |
| Idempotency | `OrderId` is the idempotency key; duplicate messages produce no duplicate ERP/WMS calls | OrderSyncAdapter (checked before each outbound call) |
| At-least-once delivery | Outbox polls unprocessed records; acknowledged only after successful publish | OutboxPublisherService |
| Dead-letter handling | Messages exhausting all Polly retries are published to `order.placed.dlq` for inspection and reprocessing | OrderSyncAdapter |
| Stale data handling | When WMS circuit breaker is open, OrderSyncAdapter returns the last cached stock value with a `stale: true` indicator | OrderSyncAdapter WMS client |
| Identity / auth | Out of scope — WireMock stubs accept all requests without authentication | Documented in [evidence/known-limitations.md](../evidence/known-limitations.md) |
| Logging | Structured logs with correlation ID on all integration events; visible in container stdout | OrderSyncAdapter |

---

## 10. Traceability matrix

| Business Driver | QA Scenario | ADR | Implementation |
|---|---|---|---|
| SG-1: Decouple checkout from WMS/ERP | QAS-1 (Availability) | ADR-001 (RabbitMQ async) | `order.placed` queue; OutboxPublisherService |
| SG-1: No silent event loss | QAS-2 (Reliability) | ADR-002 (Outbox Pattern) | `Integration_OutboxMessage` table; polling publisher |
| SG-3: Integration isolated from storefront | QAS-3 (Recoverability) | ADR-003 (OrderSyncAdapter extraction) | `src/Services/OrderSyncAdapter/` |
| SG-2: Cross-channel stock visibility | QAS-4 (Consistency) | ADR-001 (RabbitMQ) | `WmsStockSyncService` consumes `wms.stock.update` queue; `simulate-wms-restock.sh` simulates WMS push; `POST /api/wms/orders` syncs orders to WMS |
| SG-3: Storefront available under ERP lag | QAS-5 (Availability) | ADR-001 (RabbitMQ async) | Async order path; checkout never calls ERP directly |

---

## 11. Known limitations

Full list: [evidence/known-limitations.md](../evidence/known-limitations.md)

The Outbox guarantees at-least-once delivery, not exactly-once. Idempotency in OrderSyncAdapter prevents duplicate ERP/WMS calls, but if the publisher crashes mid-poll, duplicate Outbox entries are possible.

WireMock represents the integration boundary and failure modes, not the internal business logic or validation of a real ERP. A production ERP behaves differently under load and partial failures.

There is no authentication between OrderSyncAdapter and the WireMock stubs. In production this boundary would need mutual TLS or API key management at minimum.

The Outbox polls every 2 seconds, so worst-case delivery latency under normal conditions is 2 seconds. That satisfies QAS-1's recovery SLA but not sub-second real-time visibility.
