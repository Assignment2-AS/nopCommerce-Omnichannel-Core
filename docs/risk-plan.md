# Risk Plan and Validation Strategy

**Scenario:** Scenario C - Omnichannel Commerce Core  
**Deciders:** Carolina Reis | Technical Lead  
**Date:** 2026-05-02  
**Presentation:** Part 1 - Architecture Checkpoint (5/6 May 2026)

---

## Architectural Risks

| # | Risk | Probability | Impact | Mitigation |
|---|------|-------------|--------|------------|
| R1 | RabbitMQ loses messages if broker restarts without persistence configured | Medium | High | Declare queues as `durable=true` and messages as `persistent` at publish time (validated in the feasibility spike) |
| R2 | Outbox polling interval introduces visible latency in the demo | Low | Medium | Configure polling at 2s; acceptable for demo purposes; document as a known limitation in the evidence pack |
| R3 | WireMock cannot simulate timeouts in a convincing way | Low | Medium | Use `fixedDelayMilliseconds` in WireMock stub mappings; test timeout behavior before the final demo |
| R4 | Outbox background service produces duplicate messages if it crashes mid-publish | Medium | Medium | Consumers use `OrderId` as idempotency key (correlation ID); duplicate messages are processed but produce no side effects |
| R5 | OrderSyncAdapter fails to reconnect to RabbitMQ after a broker restart | Medium | High | Use `RabbitMQ.Client` with `ConnectionFactory.AutomaticRecoveryEnabled = true` and `NetworkRecoveryInterval` configured |

---

## Risk Detail

### R1: RabbitMQ message durability

Without `durable=true` on the queue and `persistent` delivery mode on messages, a RabbitMQ restart drops all queued messages. In the demo, a broker restart during the degradation scenario would silently lose pending orders, making the recovery story impossible to show.

**Validation:** The feasibility spike (Commit 7) explicitly tests durable queue declaration and persistent message delivery. The spike verifies that a message published before a broker restart is still retrievable after recovery.

---

### R2: Outbox polling latency

A 2-second polling interval means up to 2 seconds between order placement and event publication to RabbitMQ. This is acceptable for an architectural demo but must not be presented as a production-ready configuration.

**Validation:** Measure end-to-end latency from order placement to ERP stub receipt during the demo dry run. Document in the evidence pack.

---

### R3: WireMock timeout simulation

The degradation demo requires WireMock to simulate a slow or unresponsive ERP. If WireMock's delay behavior is unreliable or inconsistent, the circuit breaker and retry logic in OrderSyncAdapter cannot be demonstrated credibly.

**Validation:** Test `fixedDelayMilliseconds: 10000` on the ERP stub mapping before the final demo. Verify that Polly's retry timeout fires correctly and that the circuit breaker opens after the configured threshold.

---

### R4: Duplicate message delivery

If the Outbox background service publishes a message to RabbitMQ and then crashes before marking the `OutboxMessage` record as processed, the next poll cycle will publish the same message again. This is expected behavior under at-least-once delivery.

**Validation:** OrderSyncAdapter checks whether an order with the given `OrderId` has already been forwarded to the ERP before making the API call. Idempotent consumer behavior is verified in the recovery scenario test (later).

---

### R5: RabbitMQ connection recovery in OrderSyncAdapter

If RabbitMQ restarts during the demo (e.g. as part of the degradation scenario), OrderSyncAdapter must automatically reconnect and resume consuming without manual intervention.

**Validation:** The feasibility spike confirms `AutomaticRecoveryEnabled = true` behavior. The recovery demo scenario explicitly restarts the RabbitMQ container and verifies that OrderSyncAdapter resumes without a manual restart.

---

## Validation Plan

| Risk | Validated By | When |
|------|-------------|------|
| R1: Message durability | Feasibility spike | Before Part 1 presentation |
| R2: Outbox latency | Latency measurement in evidence pack | Before Part 2 demo |
| R3: WireMock timeouts | Demo dry run with fault injection | Before Part 2 demo |
| R4: Duplicate handling | Recovery scenario test | Before Part 2 demo |
| R5: Auto reconnection | Feasibility spike + recovery scenario | Before Part 1 (basic), Part 2 (full) |

---

## Known Limitations (accepted at this stage)

- The Outbox polling interval (2s) is a demo-appropriate configuration, not a production tuning.
- WireMock stubs approximate ERP/WMS behavior, they do not replicate real ERP business logic.
- Identity across channels (Keycloak) is out of scope for this MVP.
- The architecture handles at-least-once delivery; exactly-once would require a more complex consumer coordination protocol not justified at this scope.
