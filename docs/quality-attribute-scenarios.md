# Quality Attribute Scenarios

**Author:** Guilherme Silva | Architect  
**Date:** 2026-05-03  
**Scenario:** Scenario C - Omnichannel Commerce Core

---

## Overview

Five quality attribute scenarios were defined to formalise the pressures identified in the business driver analysis. They were elicited by mapping the five architectural pressure points (PP-1 to PP-5) against the three mandatory demonstration conditions of Scenario C: a buy-online / fulfill-through-another-channel flow, a cross-channel stock or order-state visibility flow, and a surrounding operational system becoming degraded while the commerce core remains useful.

Each scenario follows the standard ADD stimulus-response format. The scenarios are ordered by the decisions they drove: QAS-1 to QAS-3 directly motivated ADR-001 through ADR-003; QAS-4 and QAS-5 are addressed in Part 2 once the messaging infrastructure is in place.

**Implementation note:** RabbitMQ is the message broker (durable queues, persistent messages, at-least-once delivery). WireMock simulates both the ERP and WMS stubs with configurable fault injection. The Outbox Pattern (ADR-002) handles reliable event publishing on the nopCommerce side; Polly retry and circuit-breaker logic sits in OrderSyncAdapter on the consumer side (ADR-003).

| ID | Quality Attribute | Priority | Drives |
|---|---|---|---|
| QAS-1 | Availability | Critical | ADR-001, ADR-002, ADR-003 |
| QAS-2 | Reliability | Critical | ADR-002 |
| QAS-3 | Recoverability | High | ADR-003 |
| QAS-4 | Consistency | High | Part 2 |
| QAS-5 | Availability | High | ADR-001 |

---

## QAS-1: Order Acceptance under External System Failure

**Quality Attribute:** Availability  
**Drives:** ADR-001, ADR-002, ADR-003  
**Mandatory use case:** Buy-online / fulfill-through-another-channel

| Field | Value |
|---|---|
| **Source** | Online customer |
| **Stimulus** | Places an order on the nopCommerce storefront |
| **Environment** | External WMS or ERP is unavailable or returning errors |
| **Artifact** | nopCommerce order processing pipeline |
| **Response** | Order is accepted and persisted locally; an `order.placed` event is written atomically to the Outbox table and delivered to RabbitMQ asynchronously; OrderSyncAdapter forwards it to the ERP/WMS once they recover |
| **Response Measure** | 100% of orders accepted regardless of WMS or ERP state; zero orders lost; synchronisation completes within 2 minutes of external system recovery; no manual intervention required |

This is the primary driver of the entire architecture. The current nopCommerce monolith has no mechanism to decouple order acceptance from downstream system availability. If any synchronous dependency on ERP or WMS existed at checkout time, this scenario would be impossible to satisfy. That is exactly why ADR-001 (async messaging) is the foundational decision, and why ADR-002 and ADR-003 exist to make that decision reliable and operationally clean.

---

## QAS-2: Message Broker Resilience

**Quality Attribute:** Reliability  
**Drives:** ADR-002  
**Mandatory use case:** Reliability of the event pipeline (buy-online / fulfill-through-another-channel)

| Field | Value |
|---|---|
| **Source** | RabbitMQ broker |
| **Stimulus** | Broker restarts or becomes temporarily unavailable during active order processing |
| **Environment** | One or more `order.placed` events are in-flight or awaiting delivery |
| **Artifact** | Asynchronous messaging layer (RabbitMQ and the Outbox background publisher) |
| **Response** | No events are lost; the Outbox background service reprocesses any unacknowledged records on its next poll cycle; OrderSyncAdapter reconnects automatically and resumes consuming |
| **Response Measure** | Zero message loss across broker restart; at-least-once delivery guaranteed; duplicate events handled idempotently via `OrderId` correlation key; auto-reconnection within 10 seconds of broker availability |

At-least-once delivery does not emerge automatically from "using a message broker." Without the Outbox Pattern, a broker restart between the database commit and the `BasicPublish` call produces a committed order with no downstream notification. The order exists in the database but no external system ever learns about it. QAS-2 forces this failure mode to be addressed structurally, not treated as an unlikely edge case.

---

## QAS-3: Reconciliation and Boundary Isolation after Outage

**Quality Attribute:** Recoverability  
**Drives:** ADR-003  
**Mandatory pressure point:** Surrounding system degradation and recovery

| Field | Value |
|---|---|
| **Source** | Returning external system (WMS or ERP) after a prolonged outage |
| **Stimulus** | System comes back online; WireMock fault injection removed |
| **Environment** | Post-degradation recovery with queued `order.placed` events in RabbitMQ |
| **Artifact** | OrderSyncAdapter integration layer |
| **Response** | OrderSyncAdapter automatically drains the queue, forwarding each event to the recovered system in order; duplicate events are detected and skipped via `OrderId` idempotency; nopCommerce storefront is unaffected throughout |
| **Response Measure** | All queued events processed within 5 minutes of recovery; no duplicate orders created; nopCommerce availability unaffected during both degradation and recovery; no manual restart of any service required |

Recovery is the half of the degradation story that is easy to overlook. A system that degrades gracefully but needs an operator to restart a service to resume is not a reliable system; it has just moved the failure mode from automatic to manual. This scenario also justifies the service extraction in ADR-003: if OrderSyncAdapter ran inside the nopCommerce process as a plugin, restarting the integration layer would take the storefront with it.

---

## QAS-4: Cross-channel Stock Visibility

**Quality Attribute:** Consistency  
**Drives:** Part 2 implementation  
**Mandatory use case:** Cross-channel stock or order-state visibility

| Field | Value |
|---|---|
| **Source** | Warehouse operator or WMS |
| **Stimulus** | Stock level updated in WMS (e.g. item sold at a physical store, or manual stock adjustment) |
| **Environment** | Normal operation; both WMS and nopCommerce are available |
| **Artifact** | nopCommerce product catalog and stock display |
| **Response** | The stock change propagates to nopCommerce through the integration layer and becomes visible to online customers without manual intervention |
| **Response Measure** | Change reflected within 30 seconds under normal load; no stale stock displayed beyond 60 seconds; no overselling events caused by propagation delay under normal conditions |

When the WMS is temporarily unavailable, nopCommerce should serve the last known stock value with a staleness indicator visible to operators, rather than blocking browsing or displaying errors. The degradation behaviour is part of this scenario's scope, even though the normal-operation flow is its primary focus.

---

## QAS-5: Storefront Availability under ERP Lag

**Quality Attribute:** Availability  
**Drives:** ADR-001  
**Mandatory pressure point:** Surrounding system degradation

| Field | Value |
|---|---|
| **Source** | External ERP system (WireMock stub) |
| **Stimulus** | ERP response time exceeds 5 seconds, returns 5xx errors, or becomes entirely unresponsive |
| **Environment** | Customers actively browsing or in the process of checking out |
| **Artifact** | nopCommerce storefront and checkout flow |
| **Response** | The storefront remains fully responsive; checkout completes successfully; ERP-bound data is queued for later delivery; degradation is visible to operators, not customers |
| **Response Measure** | Customer-facing response time ≤500ms regardless of ERP state; checkout success rate unaffected by ERP unavailability; operator dashboard shows ERP degradation status |

QAS-5 and QAS-1 share the same root cause and the same structural fix. By removing all synchronous ERP coupling from the request path through ADR-001, neither WMS nor ERP unavailability can reach the checkout flow. The two scenarios were kept separate because they describe different stimulus sources and may require different monitoring and observability approaches in Part 2.