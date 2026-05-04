# Architectural Decision Records

This directory contains the ADRs for the VerdeMart Retail omnichannel evolution of nopCommerce.

Each ADR documents one significant architectural decision: what was decided, why, and what alternatives were considered and rejected.

## Index

| ID | Title | Status |
|----|-------|--------|
| [ADR-001](ADR-001-async-messaging-rabbitmq.md) | Asynchronous messaging via RabbitMQ for external system integration | Draft |
| [ADR-002](ADR-002-outbox-pattern.md) | Outbox Pattern for reliable order event delivery | Draft |
| [ADR-003](ADR-003-ordersync-adapter-extraction.md) | Extract OrderSync as independently deployable integration adapter | Draft |

## Status definitions

- **Draft**: under discussion, not yet implemented
- **Accepted**: decided and being implemented
- **Superseded**: replaced by a later ADR (link provided)
- **Deprecated**: no longer relevant
