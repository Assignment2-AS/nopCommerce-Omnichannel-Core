# ADR-002: Outbox Pattern for Reliable Order Event Delivery

**Date:** 2026-05-02  
**Status:** Draft  
**Deciders:** Carolina Reis | Technical Lead  
**Scenario:** Scenario C - Omnichannel Commerce Core

---

## Context

ADR-001 established that nopCommerce will publish an `order.placed` event to RabbitMQ when an order is created. The question is: how is that publication made reliable?

The simplest implementation publishes directly to RabbitMQ inside the same `SaveOrder()` call, immediately after the database write. This creates a dual-write problem: two separate systems (the database and the message broker) must both succeed for the operation to be consistent. If the database commits but the RabbitMQ publish fails, the order exists but no downstream system is ever notified: a silent data loss. If the publish succeeds but the database rolls back (e.g. due to a constraint violation after the publish), the event is delivered for an order that does not exist.

There is no distributed transaction support between SQL Server and RabbitMQ that would allow both operations to be atomic without a two-phase commit coordinator.

This decision addresses how to guarantee that every committed order produces exactly one durable event, with no silent losses and no phantom events.

---

## Decision

The Outbox Pattern is applied. When an order is saved, an `OutboxMessage` record is written to the same database, in the same transaction, as the order itself. The message is not published to RabbitMQ at this point.

A separate background service polls the `OutboxMessage` table at a fixed interval, reads unprocessed records, publishes each one to RabbitMQ with publisher confirms enabled, and marks the record as processed only after the broker acknowledges it.

Because the outbox write and the order write share a single database transaction, they are atomic: either both succeed or neither does. The background service provides at-least-once delivery - if it crashes after publishing but before marking as processed, the message will be republished on the next poll cycle. Consumers are therefore responsible for idempotent handling (using `OrderId` as a correlation key).

---

## Consequences

**Positive:**
- The order write and the event write are atomic - no silent event loss, no phantom events
- At-least-once delivery guarantee to RabbitMQ
- nopCommerce's checkout path is not blocked by broker latency - the publish happens asynchronously in the background
- The outbox table provides a durable audit log of all published events

**Negative / Trade-offs:**
- Adds latency between order placement and event delivery (polling interval, default 2s)
- Consumers must implement idempotent handling to tolerate duplicate delivery
- Requires an additional database table (`Integration_OutboxMessage`) and a background polling service

**Neutral / Notes:**
- Polling interval is configurable; 2s is acceptable for the demo and documented as a known limitation
- The outbox table is owned by the `Nop.Plugin.Integration.OrderPublisher` plugin (it does not pollute the nopCommerce core schema)

---

## Rejected Alternatives

### Alternative A: Publish directly to RabbitMQ inside the order handler

**Description:** Call `channel.BasicPublish()` immediately after `_orderService.InsertOrderAsync()` returns, within the same request.

**Reason for rejection:** Dual-write problem with no atomicity guarantee. A failure between the database commit and the broker publish produces a committed order with no downstream notification. A failure between the broker publish and the database commit (if the publish somehow precedes the commit) produces a dangling event. Neither failure mode is detectable or recoverable without external tooling.

### Alternative B: Distributed Transaction (Two-Phase Commit)

**Description:** Use a distributed transaction coordinator (e.g. MSDTC) to wrap the database write and the RabbitMQ publish in a single atomic transaction.

**Reason for rejection:** RabbitMQ does not support XA transactions natively. Achieving 2PC between SQL Server and RabbitMQ would require a third-party coordinator, adding significant operational complexity, latency, and a new single point of failure. The Outbox Pattern solves the same consistency problem using only the database, which is already a reliable transactional system.

### Alternative C: Change Data Capture via Debezium

**Description:** Use Debezium to stream database change events (on the `Order` table) directly into Kafka or RabbitMQ, without any application-level outbox.

**Reason for rejection:** Introduces Debezium and its dependency on database binary log access as additional infrastructure. This is outside the scope of the MVP and adds operational complexity disproportionate to the problem. It also couples the event schema to the internal database schema, rather than to an intentionally designed event contract. The Outbox Pattern achieves the same reliability guarantee with only application-level code.

---

## Links

- Related ADR: [ADR-001](ADR-001-async-messaging-rabbitmq.md) - the queue this pattern publishes into
- Related ADR: [ADR-003](ADR-003-ordersync-adapter-extraction.md) - the consumer that must handle at-least-once delivery
- Quality attribute scenario: QAS-1 (commerce core availability), QAS-2 (order delivery reliability)
