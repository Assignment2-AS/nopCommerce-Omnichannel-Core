# Evidence Pack: Known Limitations

**Author:** Carolina Reis | Technical Lead  
**Date:** 2026-05-30  
**Scenario:** Scenario C - Omnichannel Commerce Core  
**Version:** 1.0.0  

These limitations are documented honestly, as required by the assignment brief. None of them are hidden defects, they are deliberate scope cuts or inherent trade-offs of the chosen patterns, acceptable for a demonstration environment.

---

## L-1: At-Least-Once Delivery (Duplicate Messages Are Possible)

**What it means:**  
If `OutboxPublisherService` publishes a message to RabbitMQ and then crashes before it
can mark `ProcessedOnUtc`, the row remains `NULL`. On restart, the service will publish
it again. The `order.placed` queue will therefore contain the same message twice.

**Where this matters:**  
`OrderSyncAdapter` will call the ERP and WMS twice for the same `OrderId`. Without
idempotency on the adapter side, this causes duplicate records in the ERP.

**Mitigation in scope (validated):**  
`OrderSyncAdapter` uses `CorrelationId = OrderId` as an idempotency key. The
`ReconciliationService` stores processed `OrderId` values in `_processedOrders`; on a
duplicate message it acknowledges and discards without forwarding to ERP/WMS.
This was validated experimentally: the same payload was published twice to
`order.placed.dlq` and WireMock confirmed only one `POST /api/orders` per `OrderId`.
See [scenarios/idempotency-validation.md](scenarios/idempotency-validation.md).

**Residual gap (production only):**  
`_processedOrders` is an in-memory dictionary. It does not survive an adapter process
restart, and it is not shared across multiple adapter instances. In a multi-instance
deployment, two instances could each process the same message after a restart, producing
a duplicate. This case does not occur in the single-instance demo environment.

**What would be needed for production:**  
Persist the processed-orders log to a durable store (e.g. a database table or Redis set)
so the idempotency check survives restarts and is visible to all instances. Alternatively,
use a broker with exactly-once semantics (e.g. Kafka with idempotent producers).

---

## L-2: Residual Window Between Order INSERT and Outbox INSERT

**What it means:**  
nopCommerce uses LinqToDB with per-operation connections. The order row INSERT and the
`OutboxMessage` INSERT are **not** in the same database transaction (there is no shared
ambient transaction). If the process crashes in the microsecond window between the two
INSERTs, the order exists in the database but no Outbox row is written. No downstream
system will ever be notified about that order.

**Why this trade-off was accepted:**  
The ordering guarantee is provided by the nopCommerce event pipeline (`OrderProcessingService`
fires `OrderPlacedEvent` only after `InsertOrderAsync` completes). The crash window is
extremely small (microseconds, in-process). For a demonstration environment with a single
process, the probability is negligible.

**What would be needed for production:**  
A compensating background scan that periodically queries for orders placed in the last N
minutes with no corresponding Outbox row and creates the missing row. This is a standard
complement to the Outbox Pattern in systems without a shared transaction coordinator.

---

## L-3: Fixed Polling Interval Adds Latency

**What it means:**  
The `OutboxPublisherService` polls every 2 seconds. An order placed immediately after a
poll will wait up to 2 s before it is published to RabbitMQ. Measured P99 latency between
order placement and RabbitMQ publication was 2000 ms (see [measurements.md](measurements.md)).

**Acceptable for:**  
Demonstration environment and asynchronous B2C fulfillment flows where sub-second ERP
notification is not a customer-facing requirement.

**Not acceptable for:**  
Real-time inventory reservation (e.g. "reserve stock at checkout"), where a 2 s window
could allow overselling under concurrent load. In that case, the polling interval should
be reduced (to 200–500 ms) or replaced with a push-based trigger (e.g. a `LISTEN/NOTIFY`
pattern on PostgreSQL, or an Azure Service Bus trigger).

---

## L-4: WireMock Does Not Replicate Real ERP/WMS Business Logic

**What it means:**  
The ERP and WMS are simulated by WireMock stubs (`infrastructure/wiremock/mappings/`).
WireMock returns hardcoded HTTP responses; it does not execute any business logic.

**Scenarios not covered by this demo:**
- Connection pool exhaustion (WireMock handles unlimited concurrent connections)
- Partial responses or chunked HTTP payloads
- Malformed JSON payloads from the ERP
- Authentication token expiry and re-login flows
- Rate limiting or throttling from the ERP/WMS
- ERP-side duplicate detection (idempotency is only tested from the adapter side)

**Why WireMock is still the right tool here:**  
The assignment requires demonstrating the architectural pressure point (surrounding system
becomes unavailable) and the recovery path. WireMock's fault injection profiles
(`fault-profiles/wms-unavailable.json`, `fault-profiles/erp-unavailable.json`) create
exactly that pressure with full control and without a full ERP installation.

---

## L-5: Cross-Channel Identity Management Is Out of Scope

**What it means:**  
This implementation does not address how a customer identity in nopCommerce maps to a
customer identity in the ERP, WMS, or POS system. The `order.placed` event payload
includes a `customerId` field (nopCommerce's internal ID), which is forwarded as-is to
the ERP stub.

**Implication:**  
In a real omnichannel deployment, the ERP would need a unified customer record. Without
identity mapping, the ERP would create a new customer record per order, leading to
duplicate customer data and broken loyalty/history flows.

**Why out of scope:**  
Implementing a shared identity layer (e.g. Keycloak, a customer data platform, or an
identity resolution service) would require a separate subsystem with its own architectural
decisions. The assignment brief allows honest scope cuts.

---

## L-6: Single `OutboxPublisherService` Instance (No Horizontal Scaling)

**What it means:**  
`OutboxPublisherService` is a singleton `BackgroundService` running inside the nopCommerce
process. If nopCommerce is deployed in multiple instances (e.g. behind a load balancer),
each instance would run its own `OutboxPublisherService`, and both would attempt to
publish the same pending Outbox rows.

**Current behaviour under multiple instances:**  
Both instances would publish the same message, resulting in duplicates in the queue.
There is no row-level locking or a "claimed-by" column on `Integration_OutboxMessage`.

**What would be needed for production:**  
Either: (a) run only one instance of nopCommerce (acceptable for a monolith deployment),
or (b) introduce a distributed lock (e.g. via Redis or a `SELECT FOR UPDATE SKIP LOCKED`
pattern) on the Outbox drain loop.

For the demo, a single nopCommerce instance is assumed.
