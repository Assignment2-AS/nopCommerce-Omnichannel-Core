# Scenario Choice and Business Drivers

**Author:** Guilherme Silva | Architect  
**Date:** 2026-05-03  
**Scenario:** Scenario C - Omnichannel Commerce Core

---

## 1. Selected Scenario

The team chose Scenario C (Omnichannel Commerce Core), in which VerdeMart Retail evolves nopCommerce from a standalone web storefront into the authoritative commerce core of a wider operational ecosystem. Scenarios A and B were considered but set aside: Scenario A (Federated Commerce) presents an interesting multi-tenancy challenge but its pressure point is harder to induce and observe in a controlled demonstration. Scenario B (Regulated Commerce) shifts focus toward policy enforcement and audit trails, concerns that are real but less central to the kind of integration resilience we wanted to address. Scenario C was chosen because keeping the commerce platform reliable when the surrounding operational systems are not is a tension that is both architecturally clear and directly demonstrable at runtime.

---

## 2. Business Context

VerdeMart Retail currently runs five distinct channels: the online storefront (nopCommerce), physical retail locations with their own point-of-sale terminals, a warehouse management system handling inventory and fulfillment, shipping carrier integrations, and a customer support platform. These channels share a customer base but not a data model. Each one maintains its own state in isolation.

This was a reasonable starting point for a business whose primary surface was a single web shop. It becomes a liability the moment customers start expecting a consistent experience across channels. Today, a product sold at a physical store does not update the online catalog. That means a customer can complete an online purchase for an item that no longer exists in inventory. When the WMS goes down for maintenance, nopCommerce has no mechanism to keep accepting orders independently; in practice the checkout path inherits the reliability of whatever warehouse system sits behind it. Customers who place orders receive no fulfillment updates because the ERP holds that state and has no way to push it back. Support agents working the same customer call cannot see order state without switching tools, because operational data lives elsewhere.

None of these are feature gaps. They are the product of an architecture that was never designed to coordinate with external systems under adverse conditions. Adding REST calls between nopCommerce and the WMS or ERP would not fix the underlying problem. It would simply create more places where a slow or failed dependency can propagate back into the customer-facing checkout.

---

## 3. Strategic Goals

Three strategic goals define the target state for this evolution, ordered by priority.

**SG-1: Decouple order acceptance from operational system availability.** The reliability of nopCommerce's checkout must not be bounded by the reliability of downstream systems. A warehouse outage or an ERP maintenance window should have no customer-visible impact. Every failed checkout is lost revenue, and customers who encounter checkout failures at critical moments rarely return.

**SG-2: Achieve cross-channel visibility of stock, order state, and fulfillment progress.** Whether the trigger is an in-store sale, a warehouse adjustment, or an online purchase, stock changes must converge to a consistent view within a bounded time window. Order fulfillment progress must be visible through nopCommerce regardless of which operational system owns the actual fulfillment process. Overselling is commercially damaging and operationally expensive to unwind.

**SG-3: Remain operationally useful during and after external system degradation.** When a dependency is slow, unavailable, or returning inconsistent data, nopCommerce should degrade gracefully: accepting orders for later synchronisation, serving cached data with staleness indicators, and recovering automatically once the dependency stabilises. Requiring manual operator intervention to restore normal operation after a routine infrastructure event is not acceptable at scale.

---

## 4. Architectural Pressure Points

The strategic goals above translate into specific gaps in the current nopCommerce architecture. These were identified by examining how nopCommerce handles order processing and inventory management today, then reasoning about what breaks when external systems are introduced.

| Pressure Point | Current Behaviour | Conflict |
|---|---|---|
| PP-1: Synchronous external coupling | Any external integration would occur as a synchronous call within the request/response cycle | A slow or unavailable dependency degrades or blocks checkout directly (SG-1) |
| PP-2: No outbound event mechanism | nopCommerce has no built-in way to publish order or stock events to external consumers | External systems cannot react to state changes without polling or manual export (SG-2) |
| PP-3: No inbound state update contract | No defined integration point through which external systems can push state changes back into nopCommerce | Cross-channel visibility requires more than outbound notifications; it requires an inbound contract (SG-2) |
| PP-4: No degraded-mode behaviour | No circuit-breaker, retry, or fallback logic exists for external dependencies | Failures propagate rather than being contained at the integration boundary (SG-3) |
| PP-5: No reconciliation mechanism | No process exists for detecting and resolving inconsistencies that accumulate during an outage | After a dependency recovers, diverged data remains inconsistent indefinitely without manual intervention (SG-3) |

These pressure points directly motivated the quality attribute scenarios in [quality-attribute-scenarios.md](quality-attribute-scenarios.md) and the architectural decisions in the [ADR set](adr/README.md).