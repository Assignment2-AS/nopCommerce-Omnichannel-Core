# ADR-003: Extract OrderSync as Independently Deployable Integration Adapter

**Date:** 2026-05-02  
**Status:** Accepted  
**Deciders:** Carolina Reis | Technical Lead  
**Scenario:** Scenario C - Omnichannel Commerce Core

---

## Context

ADR-001 and ADR-002 establish that nopCommerce publishes `order.placed` events to a durable RabbitMQ queue. Something must consume those events and forward them to the ERP and WMS.

The question is where that consumer lives. The simplest option is to implement it as a nopCommerce plugin: a background service running inside the monolith that reads from RabbitMQ and calls external APIs. However, this creates several problems.

A plugin running inside nopCommerce shares the same process, the same database connection pool, and the same deployment lifecycle as the commerce core. Any crash, memory pressure, or deployment of the integration logic affects the storefront. The plugin also has unrestricted access to nopCommerce's internal services and database: there is no enforced boundary between commerce concerns and integration concerns. Scaling the integration layer independently (e.g. to handle a spike in order events) would require scaling the entire nopCommerce instance.

More fundamentally, the integration logic (consuming events, calling external APIs, managing retries, handling dead letters) is not a commerce concern. It belongs to a separate operational domain with its own failure modes and deployment cadence.

---

## Decision

The integration consumer is extracted as `OrderSyncAdapter`: an independently deployable .NET Worker Service, packaged as a separate Docker container.

`OrderSyncAdapter` owns:
- consuming the `order.placed` queue from RabbitMQ
- calling external system APIs (ERP stub via WireMock, WMS stub via WireMock)
- retry logic with exponential backoff via Polly
- dead-letter handling for orders that exhaust all retries
- reconciliation after a degraded external system recovers

`OrderSyncAdapter` does not have access to the nopCommerce database. Its only coupling to nopCommerce is through the RabbitMQ queue, a deliberate, versioned interface.

Both services are orchestrated locally via `docker compose`.

---

## Consequences

**Positive:**
- Clear boundary between the commerce core (nopCommerce) and the integration layer (OrderSyncAdapter)
- nopCommerce database is not accessible from the integration layer (no hidden coupling across the boundary)
- OrderSyncAdapter can be restarted, redeployed, or scaled independently without touching the storefront
- The degradation and recovery demo is cleaner: stopping OrderSyncAdapter or its targets does not affect the nopCommerce checkout
- The extracted service satisfies the assignment requirement for at least one independently deployable subsystem

**Negative / Trade-offs:**
- Requires docker compose to orchestrate two services instead of one
- Adds a network hop between the two containers (mitigated: local Docker network, negligible latency)
- Operational surface is slightly larger than a single monolith deployment

**Neutral / Notes:**
- The Worker Service template (.NET) is the natural fit: long-running background process, no HTTP surface needed
- The boundary is enforced by the absence of project references (OrderSyncAdapter has no reference to any Nop.* library)

---

## Implementation

`OrderSyncAdapter` is implemented as a .NET Worker Service under
`src/Services/OrderSyncAdapter/`. It is orchestrated alongside nopCommerce, RabbitMQ,
and WireMock via `infrastructure/docker-compose.yml`.

**Boundary enforcement (confirmed):**

- `OrderSyncAdapter` has zero project references to any `Nop.*` library. Its only
  dependency on nopCommerce is the `order.placed` queue name, declared as a string constant.
- It does not connect to the nopCommerce SQL Server database. Connection strings for
  `OrderSyncAdapter` reference only RabbitMQ and the WireMock HTTP endpoints.

**Resilience inside `OrderSyncAdapter` (implemented by Francisco):**

- Polly `ResiliencePipeline` on ERP calls: 3 retries, exponential backoff (1 s → 2 s → 4 s).
  After 3 consecutive failures, the message is routed to `order.placed.dlq`.
- Circuit breaker on WMS calls: opens after 5 consecutive failures, half-open after 30 s.
  When open, the adapter returns a cached stock value with a staleness indicator.
- `ReconciliationService`: when the circuit breaker closes (WMS recovers), re-processes
  `order.placed.dlq` messages in creation order. Idempotency enforced via `CorrelationId = OrderId`.

**docker-compose orchestration:**

- `infrastructure/docker-compose.yml` starts: RabbitMQ (with management UI on `:15672`),
  WireMock (on `:8080`), and `OrderSyncAdapter`.
- nopCommerce is started separately (locally or via the root `docker-compose.yml`).
- Demo scripts under `infrastructure/demo-scripts/` activate and restore WireMock fault
  profiles without restarting containers.

**No deviations from the original decision.** The adapter has no access to the nopCommerce
database. Fault injection is done via WireMock Admin API, not by stopping the adapter itself,
which cleanly separates the commerce core availability from the integration layer availability.

---

## Rejected Alternatives

### Alternative A: nopCommerce plugin (inline consumer)

**Description:** Implement the RabbitMQ consumer as a `BackgroundService` inside a nopCommerce plugin, running in the same process as the storefront.

**Reason for rejection:** The plugin shares the nopCommerce process and has unrestricted access to internal services and the database. This makes the boundary implicit and unenforced - any developer can add a dependency from integration logic to core commerce logic. It also makes independent scaling and deployment impossible: updating the integration logic requires redeploying the entire storefront. This violates the principle that integration concerns should not affect the commerce core's availability or deployment cadence.

### Alternative B: Azure Function or cloud-hosted serverless consumer

**Description:** Deploy the RabbitMQ consumer as an Azure Function or similar FaaS offering, triggered by queue messages.

**Reason for rejection:** Introduces a hard dependency on a cloud provider, making local reproducibility of the demo significantly more complex. The assignment requires a runnable local demonstration environment. A serverless deployment also obscures the architectural boundary (the service exists as a configuration artifact rather than an explicit deployable), which reduces the clarity of the architectural story during the defense.

---

## Links

- Related ADR: [ADR-001](ADR-001-async-messaging-rabbitmq.md) - the queue OrderSyncAdapter consumes from
- Related ADR: [ADR-002](ADR-002-outbox-pattern.md) - at-least-once delivery means OrderSyncAdapter must be idempotent
- Quality attribute scenario: [QAS-1: Order Acceptance under External System Failure](../quality-attribute-scenarios.md#qas-1-order-acceptance-under-external-system-failure), [QAS-3: Reconciliation and Boundary Isolation after Outage](../quality-attribute-scenarios.md#qas-3-reconciliation-and-boundary-isolation-after-outage)
