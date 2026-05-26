# nopCommerce: Omnichannel Commerce Core

Architectural evolution of [nopCommerce](https://www.nopcommerce.com/) towards an omnichannel platform, developed as part of Assignment 2 for the Software Architecture course.

**Scenario C**: Omnichannel Commerce Core

---

## Repository Structure

```
nopCommerce-Omnichannel-Core/
├── docs/
│   ├── adr/                                            # Architecture Decision Records (ADRs)
│   │   ├── ADR-001-async-messaging-rabbitmq.md         # ADR 1
│   │   ├── ADR-002-outbox-pattern.md                   # ADR 2
│   │   ├── ADR-003-ordersync-adapter-extraction.md     # ADR 3
│   │   ├── README.md                                   # ADR index
│   │   └── template.md                                 # Template for ADR's
│   ├── add-framework.md                                # ADD framework application
│   ├── Domain Model & Bounded Contexts.jpg             # Domain model and bounded context diagram
│   ├── external-systems-selection.md                   # Rationale for external system choices
│   ├── quality-attribute-scenarios.md                  # Quality attribute scenarios (QAS)
│   ├── risk-and-validation.md                          # Identified risks and validation strategies
│   ├── scenario-and-business-drivers.md                # Business context and key drivers
│   └── Target Architecture.jpg                         # Target architecture diagram
├── infrastructure/
│   └── docker-compose.yml                              # Docker Compose for supporting services
├── spike/                                              
│   └── rabbitmq-spike/                                 # Technical spike / proof-of-concept
│       ├── README.md                                   # Spike goal, setup and conclusions
│       ├── evidence/
│       │   ├── spike-output.md                         # Spike results and validation evidence
│       │   └── ...
│       └── ...
├── src/                                                # nopCommerce source code (original + extensions)
│   ├── ...
│   ├── Libraries/
│   ├── Plugins/
│   ├── Presentation/
│   └── ...
├── upgradescripts/
├── ...
├── docker-compose.yml
├── Dockerfile
├── ...
├── README.md                                                       # Structuring of the repository
└── Slides Part 01: nopCommerce - Omnichannel Commerce Core.pdf     # Part 01 Presentation Slides
```

---

## Documentation

All written documentation lives in the [`docs/`](docs/) folder:

| Document | Description |
|---|---|
| [docs/adr/](docs/adr/) | Architecture Decision Records index |
| [add-framework.md](docs/add-framework.md) | ADD (Attribute-Driven Design) framework application and traceability |
| [Domain Model & Bounded Contexts.jpg](docs/Domain%20Model%20%26%20Bounded%20Contexts.jpg) | Domain model and bounded context diagram |
| [external-systems-selection.md](docs/external-systems-selection.md) | Rationale for external system choices (RabbitMQ, WireMock) |
| [quality-attribute-scenarios.md](docs/quality-attribute-scenarios.md) | Quality attribute scenarios (QAS-1 to QAS-5) |
| [risk-and-validation.md](docs/risk-and-validation.md) | Identified risks (R1–R6) and validation strategies |
| [scenario-and-business-drivers.md](docs/scenario-and-business-drivers.md) | Business context, scenario description, and key drivers |
| [Target Architecture.jpg](docs/Target%20Architecture.jpg) | Target architecture diagram |

### Architecture Decision Records

| ADR | Decision |
|---|---|
| [ADR-001](docs/adr/ADR-001-async-messaging-rabbitmq.md) | Async messaging with RabbitMQ |
| [ADR-002](docs/adr/ADR-002-outbox-pattern.md) | Outbox pattern for reliable messaging |
| [ADR-003](docs/adr/ADR-003-ordersync-adapter-extraction.md) | OrderSync adapter extraction |

### Spike

| Document | Description |
|---|---|
| [spike/rabbitmq-spike/evidence/spike-output.md](spike/rabbitmq-spike/evidence/spike-output.md) | Spike results and validation evidence |
| [spike/rabbitmq-spike/README.md](spike/rabbitmq-spike/README.md) | Spike goal, setup and conclusions |

### Slides

The group presentation is available at the root of the repository:

- [`Slides Part 01: nopCommerce - Omnichannel Commerce Core.pdf`](Slides%20Part%2001:%20nopCommerce%20-%20Omnichannel%20Commerce%20Core.pdf)

---

## Running Locally

```bash
docker compose -f infrastructure/docker-compose.yml up
```

---

## Original nopCommerce

This repository is forked from [nopSolutions/nopCommerce](https://github.com/nopSolutions/nopCommerce). nopCommerce is a free, open-source ASP.NET Core eCommerce platform. See the original project for full platform documentation.
