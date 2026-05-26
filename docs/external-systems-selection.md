# External Systems Selection

**Scenario:** Scenario C - Omnichannel Commerce Core  
**Deciders:** Carolina Reis | Technical Lead  
**Date:** 2026-05-02

---

## Selected Systems

### System 1: RabbitMQ

**Role:** Asynchronous message broker between nopCommerce and external operational systems.

**What it does in this architecture:**  
nopCommerce publishes `order.placed` events to a durable RabbitMQ queue after each order is committed. The `OrderSyncAdapter` consumes from that queue and forwards events to downstream systems. Neither side knows about the other's availability at the time of the exchange.

**Architectural justification:**  
RabbitMQ is the mechanism that makes the mandatory pressure point of Scenario C observable. When a downstream system (ERP or WMS) is degraded or unavailable, messages accumulate in the queue: the commerce core continues to accept orders without interruption. When the downstream system recovers, the queue drains and consistency is restored. Without a broker, this degradation and recovery story cannot be demonstrated.

**Why RabbitMQ over Kafka:**  
Kafka is designed for high-throughput, append-only event log streaming with multiple independent consumers replaying history. Our use case is point-to-point reliable delivery of order events to a small number of consumers, with acknowledgement-based processing and dead-letter routing. RabbitMQ's consumer acknowledgement model, native dead-letter exchange support, and significantly simpler local setup make it the appropriate choice for this scope. Kafka would introduce Zookeeper or KRaft coordination overhead with no architectural benefit for the volume and pattern of this scenario.

---

### System 2: WireMock

**Role:** HTTP API simulator replacing the real ERP (Odoo/ERPNext) and real WMS (OpenBoxes).

**What it does in this architecture:**  
WireMock serves as both the ERP stub (`POST /api/erp/orders`) and the WMS stub (`GET /api/wms/stock/{productId}`, `POST /api/wms/reservations`). It responds with configurable payloads and, critically, can be instructed at runtime to introduce delays, return error codes, or stop responding entirely, without touching any application code.

**Architectural justification:**  
The assignment requires demonstrating a surrounding system becoming delayed, stale, or unavailable while the commerce core remains useful. WireMock makes this controllable and repeatable: a single admin API call switches the ERP stub from healthy to degraded and back, on demand, during a live demo. This is what makes the pressure point demonstrable without relying on real infrastructure failures.

**Why not use a real ERP (ERPNext / Odoo)?**  
A real ERP instance would require significant setup, domain configuration, and operational overhead that does not contribute to the architectural argument. What matters for Scenario C is not the fidelity of the ERP's business logic - it is the *behavior of the commerce core when the ERP is unreachable*. WireMock creates that pressure faithfully while keeping the demo environment reproducible in a single `docker compose up`.

---

## System Map

```
┌─────────────────────────────────────────────────────────────┐
│                        Docker Network                       │
│                                                             │
│  ┌──────────────┐    order.placed     ┌──────────────────┐  │
│  │ nopCommerce  │ ──── (RabbitMQ) ──► │ OrderSyncAdapter │  │
│  │  (monolith)  │                     │  (Worker Service)│  │
│  └──────────────┘                     └────────┬─────────┘  │
│                                                │ HTTP       │
│                                                ▼            │
│                                       ┌──────────────────┐  │
│                                       │     WireMock     │  │
│                                       │  ┌────────────┐  │  │
│                                       │  │  ERP stub  │  │  │
│                                       │  ├────────────┤  │  │
│                                       │  │  WMS stub  │  │  │
│                                       │  └────────────┘  │  │
│                                       └──────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**Communication breakdown:**
- nopCommerce → RabbitMQ: asynchronous, fire-and-forget from the commerce core's perspective (Outbox Pattern ensures reliability - see [ADR-002](adr/ADR-002-outbox-pattern.md))
- RabbitMQ → OrderSyncAdapter: push-based consumer with manual acknowledgement
- OrderSyncAdapter → WireMock: synchronous HTTP with Polly retry and circuit breaker

---

## What Is Deliberately Out of Scope

| System | Reason excluded |
|--------|----------------|
| ERPNext / Odoo | Business logic fidelity is not the architectural concern; WireMock provides the same pressure |
| OpenBoxes (real WMS) | Same reasoning; WMS stub in WireMock is sufficient to demonstrate stock visibility and degradation |
| Keycloak / identity | Identity across channels is a valid concern for Scenario C but out of scope for this MVP (documented as a known limitation) |
| OpenSearch | Search freshness pressure is secondary to the order flow and degradation demo; excluded to maintain focus |
