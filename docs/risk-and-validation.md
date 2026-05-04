# Risk and Validation Plan

**Author:** Guilherme Silva | Architect  
**Date:** 2026-05-03  
**Scenario:** Scenario C - Omnichannel Commerce Core

---

## 1. Approach

Architectural risks are failure modes that could prevent the system from satisfying one or more quality attribute scenarios. The risks here were identified by working through the trade-offs in each architectural decision and asking what breaks if an assumption turns out to be wrong.

Two concerns raised during early analysis were closed before this document was written. The risk of nopCommerce blocking on a synchronous ERP call disappeared when ADR-001 made all external communication asynchronous through RabbitMQ. The risk of OrderSyncAdapter sharing a database with the monolith was closed by ADR-003, which enforces the boundary explicitly: the adapter holds no reference to any Nop.* library and has no database access. Neither risk requires monitoring going forward.

The remaining risks fall into three areas: message infrastructure, data consistency, and integration contract stability. They are documented below with their mitigations, contingency plans if the mitigation proves insufficient, and the specific criteria that will be used to validate them.

---

## 2. Risk Interdependencies

Before the individual risks, it is worth noting that R1 and R3 are not independent. The Outbox Pattern (the mitigation for R1) is precisely what introduces the conditions for R3: by guaranteeing at-least-once delivery, it makes duplicate message delivery expected rather than exceptional. Solving R1 creates R3 as a side effect, which is why idempotency in OrderSyncAdapter is not optional; it is the other half of the same design decision.

Similarly, R4 (reconnection failure) and R3 (duplicate delivery) can compound each other. If OrderSyncAdapter loses its RabbitMQ connection and then reconnects, it may redeliver messages that were in-flight at the time of the disconnect. R4's mitigation (automatic recovery) must be validated together with R3's mitigation (idempotency) to confirm the combined behaviour is correct.

---

## 3. Active Risks

### R1: RabbitMQ message loss on broker restart

By default, RabbitMQ does not persist queues or messages across restarts. If the broker goes down while order events are queued, everything in memory is gone. The Outbox background service will try to re-publish on its next poll cycle, but only if the target queue still exists. A non-durable queue disappears on restart, and at that point the message cannot be recovered through normal means.

The fix is straightforward: queues declared with `durable=true` and messages published with `DeliveryMode = Persistent`. Both are configuration decisions enforced at the point of queue declaration, not runtime logic. The feasibility spike validates this before the Part 1 presentation by publishing a test message, restarting the broker container, and confirming the message survives.

**If the mitigation fails:** the Outbox table still holds unprocessed records. A manual re-publish from the outbox is possible as a last resort, though it should not be needed if persistence is configured correctly. The outbox table is essentially a durable fallback by design.

**Validation pass criteria:** after a broker restart with a pending message, the message must be consumable by OrderSyncAdapter within 10 seconds of the broker coming back online. Zero messages dropped across 5 consecutive restart cycles.

**Probability:** Medium. **Impact:** Critical.

---

### R2: Stock data diverges during extended WMS outage

When the WMS is down, physical stock continues to change (items sold in-store, warehouse adjustments recorded) but nopCommerce cannot receive any of it. The longer the outage, the wider the gap. For short outages this is manageable: the queue drains after recovery and consistency is restored within minutes. For multi-hour outages on high-turnover products, the backlog may be large enough that some orders placed during the window are for items that no longer exist in physical stock.

The architecture mitigates this on two fronts. During the outage, nopCommerce serves last-known stock values with a staleness indicator visible to operators. After recovery, OrderSyncAdapter reconciles the queued stock events in order. What it cannot do is retroactively cancel orders placed against stale stock data during the outage window. That is a business process decision, not an architectural one.

**If the mitigation fails:** stock divergence during an outage is ultimately bounded by business policy, not technology. The architectural responsibility ends at ensuring reconciliation happens automatically and completely after recovery. The residual risk for very long outages is accepted and documented.

**Validation pass criteria:** after a simulated 10-minute WMS outage via WireMock, all queued stock events must be reconciled within 5 minutes of recovery. No events dropped. Stock levels in nopCommerce must match the WireMock state within the reconciliation window.

**Probability:** Medium. **Impact:** High.

---

### R3: Duplicate orders forwarded to ERP on message retry

At-least-once delivery is a guarantee, not a warning. When the Outbox background service publishes an event and then crashes before marking it as processed, the same event gets published again on the next poll cycle. OrderSyncAdapter will receive two copies of the same `order.placed` event. Without idempotency enforcement, both get forwarded to the ERP, creating a duplicate order in the back-office.

The mitigation is an idempotency check at the consumer level: before forwarding any event, OrderSyncAdapter checks whether an order with that `OrderId` has already been processed. If it has, the duplicate is acknowledged and discarded. The `OrderId` is a natural idempotency key since it is stable across redeliveries.

Note the relationship to R1 described above: the Outbox Pattern that prevents message loss in R1 is exactly what makes duplicate delivery possible here. These two risks are two sides of the same design choice.

**If the mitigation fails:** a duplicate order in the ERP would require manual correction. The WireMock request journal provides a full record of what was forwarded and when, which would support the investigation. In production, the ERP would need its own idempotency layer as a secondary defence.

**Validation pass criteria:** replaying the same `order.placed` message to OrderSyncAdapter twice must result in exactly one call to the WireMock ERP stub. Verified via the WireMock request journal. Tested across both normal operation and post-reconnection scenarios (see R4 interdependency).

**Probability:** Medium. **Impact:** High.

---

### R4: OrderSyncAdapter loses RabbitMQ connection and does not recover

The RabbitMQ .NET client does not automatically reconnect after a dropped connection unless explicitly configured to do so. If the broker restarts during the degradation demo, OrderSyncAdapter's channel goes dead. Events accumulate in the queue. The service continues running with no visible error, which is the worst kind of failure for a live demonstration: everything looks fine until someone checks whether anything is actually being processed.

Setting `ConnectionFactory.AutomaticRecoveryEnabled = true` and a `NetworkRecoveryInterval` resolves this at the client level. The feasibility spike validates it by restarting the broker container mid-consumption and confirming OrderSyncAdapter resumes without a manual restart. This is also where the R3 interdependency matters: messages that were in-flight during the disconnect may be redelivered after reconnection, so the idempotency check from R3 must hold in this scenario too.

**If the mitigation fails:** OrderSyncAdapter would need to be restarted manually. For a demonstration this is recoverable, but it would undermine the architectural argument about autonomous recovery. A health check endpoint on OrderSyncAdapter that reports consumer status would provide early detection in any scenario where automatic recovery does not engage.

**Validation pass criteria:** broker container restarted while OrderSyncAdapter is actively consuming; OrderSyncAdapter must resume consuming within 15 seconds of broker availability, with no messages lost and no duplicate forwarding to WireMock. Tested as part of the combined R3/R4 scenario.

**Probability:** Medium. **Impact:** High.

---

### R5: Dead-lettered messages are silently dropped

OrderSyncAdapter uses Polly to retry failed ERP or WMS calls with exponential backoff. If a message exhausts all retry attempts because the external system is returning errors consistently rather than just being slow, it ends up on the dead-letter exchange. Without a dead-letter queue configured and monitored, those messages disappear. An order that could not be forwarded to the ERP after all retries is lost from the integration layer with no alert and no recovery path.

Configuring a dead-letter queue on the main `order.placed` exchange ensures that exhausted messages land somewhere recoverable rather than being discarded. Monitoring the dead-letter queue depth provides an operational signal when the integration layer is struggling. Manual reprocessing from the dead-letter queue is the recovery path for genuinely persistent failures.

**If the mitigation fails:** orders exist in nopCommerce but were never forwarded to the ERP. The Outbox table retains the original events, so a replay is possible. The risk is that no one notices the failure until a customer enquires about an order that never reached fulfillment.

**Validation pass criteria:** configure WireMock to return 500 on all ERP calls; after Polly exhausts retries, the message must appear on the dead-letter queue and not be silently dropped. Dead-letter queue depth must be observable in the demo environment.

**Probability:** Low (requires sustained external system failure beyond retry window). **Impact:** High.

---

### R6: Event schema change breaks OrderSyncAdapter

The `order.placed` event has a specific schema that both the Outbox publisher and OrderSyncAdapter depend on. If the schema changes (a field renamed, a required field added, a type changed), OrderSyncAdapter will fail to deserialise incoming messages. Since the Outbox table stores serialised events, a schema change could also make previously queued messages unprocessable, blocking the entire integration pipeline until the adapter is updated and redeployed.

For Part 1 this is low risk since the schema is not yet defined in production code. For Part 2, any change to the `order.placed` event contract must be treated as a breaking change and coordinated between the Outbox publisher and OrderSyncAdapter. Versioning the event type (e.g. `order.placed.v1`) is the standard way to manage this without requiring simultaneous deployment of both components.

**If the mitigation fails:** messages on the queue become unprocessable. The dead-letter queue (R5 mitigation) provides a landing zone, but the root cause is a deployment coordination failure rather than a runtime error.

**Validation pass criteria:** not applicable for Part 1. For Part 2, any schema change must be deployed with a version bump and backward-compatible deserialisation in OrderSyncAdapter before the old schema is retired.

**Probability:** Low for Part 1, Medium for Part 2. **Impact:** High.

---

## 4. Risk Summary

| Risk | Area | Probability | Impact | Status |
|---|---|---|---|---|
| R1: Message loss on broker restart | Infrastructure | Medium | Critical | Mitigated (feasibility spike) |
| R2: Stock divergence during WMS outage | Consistency | Medium | High | Open (Part 2) |
| R3: Duplicate ERP forwarding on retry | Integration | Medium | High | Open (Part 2) |
| R4: Connection loss without recovery | Infrastructure | Medium | High | Mitigated (feasibility spike) |
| R5: Dead-lettered messages silently dropped | Integration | Low | High | Open (Part 2) |
| R6: Event schema change breaks consumer | Contract | Low | High | Open (Part 2) |

---

## 5. Validation Plan

Validation is split across two phases: what needs to be confirmed before the Part 1 presentation, and what is deferred to Part 2 implementation.

### Before Part 1 (May 5/6)

R1 and R4 are the two risks that could visibly break the live demonstration if left unvalidated. The feasibility spike by the Technical Lead covers both. For R1, the test publishes a persistent message to a durable queue, restarts the RabbitMQ container, and confirms the message is still there and consumable after recovery. The bar is zero message loss across five consecutive restart cycles, with the message available within 10 seconds of the broker coming back up. For R4, the same restart scenario is run while OrderSyncAdapter is actively consuming, and the test confirms the consumer resumes on its own within 15 seconds without any manual intervention. Because R3 and R4 interact, this test also checks that messages redelivered after reconnection do not result in duplicate ERP calls; the idempotency check must hold under reconnection conditions, not just under clean replay.

### During Part 2 implementation (May–June)

R3 gets its own dedicated integration test once OrderSyncAdapter is implemented: the same `order.placed` message is sent twice, and the WireMock request journal is inspected to confirm exactly one ERP call was made per `OrderId`. This test should also be run as part of the R4 combined scenario to cover the post-reconnection case described above.

R2 requires the stock synchronisation path to be in place before it can be validated end-to-end. The test uses WireMock to simulate a 10-minute WMS outage, measures stock divergence during the window, and then measures reconciliation time after the WMS stub is restored. Pass criteria: all queued events processed within 5 minutes of recovery, no events dropped, and nopCommerce stock levels matching WireMock state at the end of the window.

R5 is validated by configuring WireMock to return 500 on all ERP calls and letting Polly exhaust its retry budget. The message must land on the dead-letter queue rather than being silently discarded, and the DLQ depth must be visible through whatever monitoring is in place for the demo environment.

R6 has no runtime test for Part 1 since the event schema does not yet exist in production code. For Part 2, the validation is procedural: any change to the `order.placed` contract requires a version bump and backward-compatible deserialisation in OrderSyncAdapter before the old schema version is retired. This is a coordination check, not an automated test.

---

## 6. Known Limitations

The 2-second Outbox polling interval adds latency between order placement and event delivery. It is acceptable for a demonstration but would need tuning or replacement with an event-driven trigger before production use.

The architecture guarantees at-least-once delivery. Exactly-once would require distributed transaction support across SQL Server and RabbitMQ, which is not feasible without a coordinator that introduces more complexity than the problem warrants. Idempotent consumers are the right answer at this scope.

WireMock approximates the HTTP interface of external systems but does not replicate their business logic or failure modes. Some failure scenarios that would occur with a real ERP or WMS, like connection pooling exhaustion, partial responses, or malformed payloads, are not covered by WireMock stubs and would need to be tested against real systems before any production deployment.

Identity management across channels is out of scope for this phase and is documented here so it is not mistaken for an oversight.