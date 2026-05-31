# ADR-001: Asynchronous Messaging via RabbitMQ for External System Integration

**Date:** 2026-05-02  
**Status:** Accepted  
**Deciders:** Carolina Reis | Technical Lead  
**Scenario:** Scenario C - Omnichannel Commerce Core

---

## Context

When a customer places an order, nopCommerce must notify surrounding operational systems, specifically the ERP (back-office) and WMS (warehouse), so they can act on it.

The naive approach is a direct synchronous REST call at checkout time: place the order, then immediately call the ERP API to register it. This creates a hard runtime dependency: if the ERP is slow, the checkout is slow; if the ERP is down, the checkout fails. In the VerdeMart omnichannel scenario, surrounding systems are explicitly expected to become delayed, stale, or temporarily unavailable. A synchronous coupling would make nopCommerce's core checkout only as reliable as its least reliable dependency.

This decision addresses how nopCommerce communicates order events to external systems without inheriting their availability characteristics.

---

## Decision

nopCommerce publishes an `order.placed` event to a durable RabbitMQ queue immediately after an order is persisted. External systems (ERP adapter, WMS adapter) consume from that queue independently, at their own pace.

nopCommerce's responsibility ends at publishing the event. It does not wait for, or care about, what consuming systems do with it. The broker absorbs availability mismatches between producer and consumers.

---

## Consequences

**Positive:**
- nopCommerce checkout is decoupled from ERP/WMS availability - orders are accepted even when surrounding systems are down
- Surrounding systems can be replaced or scaled independently without touching nopCommerce
- The broker acts as a buffer during traffic spikes or downstream slowness
- Natural fit for demonstrating the mandatory pressure point of Scenario C (degraded surrounding system)

**Negative / Trade-offs:**
- Order state in ERP/WMS is eventually consistent - there is a window where an order exists in nopCommerce but has not yet reached the ERP
- Requires RabbitMQ as additional infrastructure (mitigated: single Docker container)
- Consumers must handle duplicate delivery (at-least-once semantics) - see ADR-002 for the reliability pattern chosen

**Neutral / Notes:**
- RabbitMQ queues are declared as `durable=true` and messages as `persistent` to survive broker restarts
- The publishing step itself is made reliable via the Outbox Pattern (see ADR-002)

---

## Implementation

This decision was implemented as part of the `Nop.Plugin.Integration.OrderPublisher` plugin
(`src/Plugins/Nop.Plugin.Integration.OrderPublisher/`).

**Producing side (nopCommerce):**

- `OrderPlacedConsumer` (`Consumers/OrderPlacedConsumer.cs`) handles `IConsumer<OrderPlacedEvent>`
  and writes a pending `OutboxMessage` to the database immediately after the order is persisted.
  It does **not** publish to RabbitMQ directly; that is the responsibility of the background service.
- `OutboxPublisherService` (`Services/OutboxPublisherService.cs`) is a `BackgroundService` that
  polls the `Integration_OutboxMessage` table every 2 s, publishes each pending row to the
  `order.placed` exchange with publisher confirms (`WaitForConfirmsOrDie`), and marks the row
  as processed only after the broker acknowledges it.

**Exchange and queue configuration (confirmed at runtime):**

- Exchange: `order.placed`, type `direct`, `durable=true`
- Queue: `order.placed`, `durable=true`, bound to the exchange with routing key `order.placed`
- Message delivery mode: `persistent` (mode 2) - survives broker restart
- Publisher confirms enabled: broker acks each message after writing to disk

**RabbitMQ connection configuration** comes from `appsettings.json` keys
`RabbitMQ:Host`, `RabbitMQ:Port`, `RabbitMQ:Username`, `RabbitMQ:Password`.
`AutomaticRecoveryEnabled = true` with `NetworkRecoveryInterval = 5 s` handles broker restarts
(risk R4 from the original risk plan, validated in `spike/rabbitmq-spike/`).

**No deviations from the original decision.** The synchronous publish path (Alternative A)
was never introduced. The interface between nopCommerce and downstream systems remains
exclusively the RabbitMQ queue.

---

## Rejected Alternatives

### Alternative A: Direct synchronous REST call to ERP at checkout

**Description:** After saving the order to the database, nopCommerce makes an HTTP POST to the ERP API before returning a response to the customer.

**Reason for rejection:** Creates a synchronous dependency between the checkout critical path and the ERP. Any ERP slowness or outage directly degrades or breaks the customer-facing checkout flow. This directly violates the core quality attribute of the scenario: *the commerce core must remain useful when surrounding systems are delayed or unavailable* ([QAS-1](../quality-attribute-scenarios.md#qas-1-order-acceptance-under-external-system-failure)).

### Alternative B: Outbound webhooks pushed by nopCommerce

**Description:** nopCommerce maintains a list of subscriber URLs and POSTs event payloads to each one when an order is placed.

**Reason for rejection:** Shifts retry and timeout management responsibility back into nopCommerce. If a webhook target is slow or unreachable, nopCommerce must decide how long to wait, how many times to retry, and what to do on failure, all within the request cycle or via ad-hoc background logic. A message broker handles exactly this problem as a first-class concern, with durability, routing, and consumer acknowledgement built in.

---

## Links

- Related ADR: [ADR-002](ADR-002-outbox-pattern.md) - reliable publishing into this queue
- Related ADR: [ADR-003](ADR-003-ordersync-adapter-extraction.md) - the service that consumes from this queue
- Quality attribute scenario: [QAS-1: Order Acceptance under External System Failure](../quality-attribute-scenarios.md#qas-1-order-acceptance-under-external-system-failure)
