# Design Framework: Attribute-Driven Design (ADD)

**Author:** Guilherme Silva | Architect  
**Date:** 2026-05-03  
**Scenario:** Scenario C - Omnichannel Commerce Core

---

## 1. Framework Selection

The team selected ADD (Attribute-Driven Design) as the guiding methodology for this architectural evolution. ADD is a method in which every structural decision, from what components exist to how they communicate and where boundaries are drawn, is derived from quality attribute requirements rather than from a feature backlog or a preferred technology stack. The system is designed to satisfy measurable properties under specific conditions, and every design choice must be traceable to at least one of those properties.

---

## 2. Why ADD fits this scenario

The core problem in Scenario C is not a missing feature. nopCommerce already places orders, tracks inventory, and serves customers. What it cannot do is behave correctly under pressure: when the WMS goes down, when the ERP responds slowly, when a message is in-flight during a broker restart. These are quality attribute concerns, and a methodology that starts from features would produce a design that works on the happy path and fails precisely where it matters.

ADD forces an explicit answer to a question that feature-driven design defers: what does the system guarantee when things go wrong? The answer cannot be "it depends" or "we'll handle it later." It has to be expressed as a concrete stimulus, a specific environment, a measurable response. That specificity is what makes the resulting architecture defensible. When asked why RabbitMQ was chosen over direct HTTP calls, the answer is not about best practices. QAS-1 requires the checkout to succeed regardless of whether the WMS is reachable, and that constraint rules out any synchronous dependency on the order path.

The full traceability from business driver to QA scenario to architectural decision is visible in the table below and in the [README traceability summary](../docs/../README.md).

---

## 3. How ADD was applied

### Identifying architectural drivers

Starting from the business analysis in [scenario-and-business-drivers.md](scenario-and-business-drivers.md), five architectural drivers were identified: the commerce core must remain available when external systems fail; order events must not be silently lost; integration concerns must not affect storefront stability or deployment; stock state must converge across channels within a bounded window; and the system must recover automatically after a dependency outage. Each driver was classified by quality attribute type and assigned a priority based on its business impact.

| Driver | Quality Attribute | Priority | Source |
|---|---|---|---|
| Commerce core available when external systems fail | Availability | High | SG-1, PP-1 |
| Order events not silently lost | Reliability | High | SG-1, PP-2 |
| Integration concerns isolated from storefront | Modifiability / Deployability | High | PP-1, PP-4 |
| Stock state converges across channels | Consistency | Medium | SG-2, PP-3 |
| Automatic recovery after dependency outage | Recoverability | High | SG-3, PP-5 |

### Translating drivers into scenarios

Each driver was formalised as a quality attribute scenario using the standard stimulus-response format. The five scenarios are in [quality-attribute-scenarios.md](quality-attribute-scenarios.md). The scenarios make the drivers testable: rather than asserting that "the system must be resilient," QAS-1 specifies that when a customer places an order while the WMS is unavailable, 100% of orders must be accepted with zero loss and synchronisation must complete within two minutes of recovery.

### Deriving decisions from scenarios

Each scenario was used to drive structural decisions. Those decisions are recorded as ADRs by the Technical Lead.

| Scenario | Decision | Why the scenario requires it |
|---|---|---|
| QAS-1 (Availability) | ADR-001: Async messaging via RabbitMQ | Synchronous coupling makes order acceptance depend on WMS/ERP state; a broker absorbs availability mismatches |
| QAS-2 (Reliability) | ADR-002: Outbox Pattern | A direct publish inside the order handler creates a dual-write problem; the Outbox makes the event write atomic with the order write |
| QAS-3 (Recoverability) | ADR-003: OrderSyncAdapter extraction | Integration logic inside the nopCommerce process cannot be restarted independently; an extracted Worker Service enforces the boundary |
| QAS-4, QAS-5 | Addressed in Part 2 | Both require the messaging infrastructure from ADR-001 to be in place first |

### Risks and validation

ADD requires trade-offs and risks to be acknowledged explicitly, not deferred. Each decision introduces failure modes that must be validated before the architecture can be considered sound. This is covered in [risk-and-validation.md](risk-and-validation.md).

---

## 4. Why not the alternatives

**ACDM** is a collaborative method centred on structured stakeholder workshops and iterative documentation. Its strength is stakeholder alignment in large, complex organisational settings. For this project the team is small and the scenario is specific, so stakeholder alignment is not the primary risk. The primary risk is whether the chosen structure actually satisfies the quality attributes under the required conditions. ACDM does not provide a mechanism for driving design from quality attributes; it assumes the design will emerge from collaborative discussion.

**TOGAF / ADM** is an enterprise architecture framework built for organisations managing portfolios of systems across multiple business units, governance layers, and technology domains. It produces documentation at a breadth that is appropriate for that scale and inappropriate for a focused evolution of a single platform. Applying TOGAF to this scope would mean producing artefacts like capability models, transition architectures, and compliance assessments that have no bearing on whether the architecture handles a WMS outage correctly. The scope of this project is a subset of what TOGAF was designed to manage.