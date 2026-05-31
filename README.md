# nopCommerce: Omnichannel Commerce Core

Architectural evolution of [nopCommerce](https://www.nopcommerce.com/) towards an omnichannel platform, developed as part of Assignment 2 for the Software Architecture course.

**Scenario C**: Omnichannel Commerce Core

---

## Table of Contents

- [Repository Structure](#repository-structure)
- [Documentation](#documentation)
  - [Architecture Decision Records](#architecture-decision-records)
  - [Local Development Setup](#local-development-setup)
  - [Evidence](#evidence)
  - [Spike](#spike)
  - [Slides](#slides)
- [Demo Setup](#demo-setup)
  - [Prerequisites](#prerequisites)
  - [Step 1: Start supporting services](#step-1-start-supporting-services)
  - [Step 2: Build and run nopCommerce](#step-2-build-and-run-nopcommerce)
  - [Step 3: Start OrderSyncAdapter](#step-3-start-ordersyncadapter)
  - [Step 4: Place a test order (happy path)](#step-4-place-a-test-order-happy-path)
  - [Step 5: Activate WMS degradation scenario](#step-5-activate-wms-degradation-scenario)
  - [Step 6: Restore WMS and observe recovery](#step-6-restore-wms-and-observe-recovery)
  - [Step 7: Restore all (clean slate for next run)](#step-7-restore-all-clean-slate-for-next-run)
  - [Available Scripts](#available-scripts)
  - [Useful URLs](#useful-urls)
- [Original nopCommerce](#original-nopcommerce)

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
│   ├── architecture-report.md                          # Architecture report
│   ├── bounded-context-view.md                         # Bounded context view (updated Part 2)
│   ├── Domain Model & Bounded Contexts.jpg             # Domain model and bounded context diagram
│   ├── external-systems-selection.md                   # Rationale for external system choices
│   ├── local-dev-setup.md                              # Local development setup guide
│   ├── quality-attribute-scenarios.md                  # Quality attribute scenarios (QAS)
│   ├── risk-and-validation.md                          # Identified risks and validation strategies
│   ├── runtime-interaction-diagrams.md                 # Sequence diagrams: normal, degraded, recovery
│   ├── scenario-and-business-drivers.md                # Business context and key drivers
│   └── Target Architecture.jpg                         # Target architecture diagram
├── evidence/
│   ├── scenarios/                                      # Executed test scenarios with evidence
│   │   ├── idempotency-validation.md                   # Duplicate message handling validation
│   │   ├── wms-degradation.md                          # WMS 503 fault injection scenario
│   │   └── wms-recovery.md                             # Automatic reconciliation after WMS recovery
│   ├── known-limitations.md                            # Known limitations and trade-offs
│   ├── measurements.md                                 # Outbox latency, reconnection time, success rate
│   └── *.png                                           # Evidence screenshots
├── infrastructure/
│   ├── demo-scripts/                                   # Fault injection and restore scripts
│   ├── helper-scripts/                                 # Utility scripts (e.g. reset-demo-data.sh)
│   ├── wiremock/                                       # WireMock mappings and fault profiles
│   └── docker-compose.yml                              # Docker Compose for supporting services
├── spike/                                              
│   └── rabbitmq-spike/                                 # Technical spike / proof-of-concept
│       ├── evidence/
│       │   ├── ui/                                     # Management UI screenshots
│       │   |   └── *.png                               
│       │   ├── *.png                                   # Spike output screenshots
│       │   └── spike-output.md                         # Spike results and validation evidence
│       ├── ...
│       └── README.md                                   # Spike goal, setup and conclusions
├── src/                                                # nopCommerce source code (original + extensions)
│   ├── Build/                                          # Build tooling (ClearPluginAssemblies)
│   ├── Libraries/                                      # nopCommerce core libraries (unchanged)
│   ├── Plugins/
│   │   ├── Nop.Plugin.Integration.OrderPublisher/      # Outbox plugin (added: Part 2)
│   │   └── ...                                         # Original nopCommerce plugins
│   ├── Presentation/                                   # nopCommerce web application
│   ├── Tests/                                          # nopCommerce test projects
│   ├── Workers/                                        # Worker services
│   │   └── VerdeMart.OrderSyncAdapter.Worker/          # OrderSyncAdapter (added: Part 2)
│   └── ...
├── upgradescripts/
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
| [architecture-report.md](docs/architecture-report.md) | Architecture report: current state, evolution path, traceability |
| [bounded-context-view.md](docs/bounded-context-view.md) | Bounded context view with Mermaid diagram (updated Part 2) |
| [Domain Model & Bounded Contexts.jpg](docs/Domain%20Model%20%26%20Bounded%20Contexts.jpg) | Domain model and bounded context diagram |
| [external-systems-selection.md](docs/external-systems-selection.md) | Rationale for external system choices (RabbitMQ, WireMock) |
| [local-dev-setup.md](docs/local-dev-setup.md) | Local development setup and first-time installation guide |
| [quality-attribute-scenarios.md](docs/quality-attribute-scenarios.md) | Quality attribute scenarios (QAS-1 to QAS-5) |
| [risk-and-validation.md](docs/risk-and-validation.md) | Identified risks (R1–R6) and validation strategies |
| [runtime-interaction-diagrams.md](docs/runtime-interaction-diagrams.md) | Sequence diagrams: normal flow, WMS degradation, recovery |
| [scenario-and-business-drivers.md](docs/scenario-and-business-drivers.md) | Business context, scenario description, and key drivers |
| [Target Architecture.jpg](docs/Target%20Architecture.jpg) | Target architecture diagram |

### Architecture Decision Records

| ADR | Decision |
|---|---|
| [ADR-001](docs/adr/ADR-001-async-messaging-rabbitmq.md) | Async messaging with RabbitMQ |
| [ADR-002](docs/adr/ADR-002-outbox-pattern.md) | Outbox pattern for reliable messaging |
| [ADR-003](docs/adr/ADR-003-ordersync-adapter-extraction.md) | OrderSync adapter extraction |

### Local Development Setup

Full setup instructions, including first-time installation, plugin build, and OrderSyncAdapter startup:

- [docs/local-dev-setup.md](docs/local-dev-setup.md)

### Evidence

| Document | Description |
|---|---|
| [evidence/scenarios/idempotency-validation.md](evidence/scenarios/idempotency-validation.md) | Duplicate message handling validation |
| [evidence/scenarios/wms-degradation.md](evidence/scenarios/wms-degradation.md) | WMS 503 fault injection scenario |
| [evidence/scenarios/wms-recovery.md](evidence/scenarios/wms-recovery.md) | Automatic reconciliation after WMS recovery |
| [evidence/known-limitations.md](evidence/known-limitations.md) | At-least-once delivery, polling latency, WireMock scope, identity gap |
| [evidence/measurements.md](evidence/measurements.md) | Outbox latency, reconnection time, success rate |

### Spike

| Document | Description |
|---|---|
| [spike/rabbitmq-spike/evidence/spike-output.md](spike/rabbitmq-spike/evidence/spike-output.md) | Spike results and validation evidence |
| [spike/rabbitmq-spike/README.md](spike/rabbitmq-spike/README.md) | Spike goal, setup and conclusions |

### Slides

The group presentation is available at the root of the repository:

- [`Slides Part 01: nopCommerce - Omnichannel Commerce Core.pdf`](Slides%20Part%2001:%20nopCommerce%20-%20Omnichannel%20Commerce%20Core.pdf)

---

## Demo Setup

This section explains how to reproduce the full end-to-end demo locally, including the
degradation and recovery scenarios.

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine + Compose v2)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server accessible locally (or use the container in `docker-compose.yml`)

### Step 1: Start supporting services

```bash
docker compose -f infrastructure/docker-compose.yml up -d
```

This starts:

| Container | Purpose | Port |
|---|---|---|
| `verdemart-rabbitmq` | Message broker | AMQP `:5672`, Management UI `:15672` |
| `verdemart-wiremock` | ERP + WMS stub | HTTP `:8080` |

Verify RabbitMQ is up: open `http://localhost:15672` (user: `guest`, password: `guest`).  
Verify WireMock is up: `http://localhost:8080/__admin/mappings`

### Step 2: Build and run nopCommerce

```bash
# Build the plugin first (required before first run)
cd src/Plugins/Nop.Plugin.Integration.OrderPublisher
~/.dotnet/dotnet build

# Start nopCommerce
cd src/Presentation/Nop.Web
~/.dotnet/dotnet run --no-build
```

On first run, navigate to `http://localhost:5000/install` and complete the installation
wizard (SQL Server: `localhost,1433`, user: `sa`, password: `VerdeMart_2026!`).

> **Note:** the default port in this repo is `5000`. If you need a different port, update `Kestrel.Endpoints.Http.Url` in `src/Presentation/Nop.Web/App_Data/appsettings.json` and the Store URL in the nopCommerce admin.

After installation, go to **Admin → Configuration → Local plugins** and install the
**Order Publisher (VerdeMart Omnichannel)** plugin. This creates the `Integration_OutboxMessage` table.

> For full setup details see [docs/local-dev-setup.md](docs/local-dev-setup.md).

### Step 3: Start OrderSyncAdapter

```bash
cd src/Workers/VerdeMart.OrderSyncAdapter.Worker
~/.dotnet/dotnet run
```

The adapter connects to RabbitMQ on `localhost:5672` and begins consuming the
`order.placed` queue. Confirm it is running by watching the console output for
messages from `RabbitMqOrderConsumerWorker`.

### Step 4: Place a test order (happy path)

1. Open `http://localhost:5000` in a browser.
2. Browse to any product and add it to the cart.
3. Complete checkout with a test customer account.
4. Observe:
   - **RabbitMQ Management UI** (`http://localhost:15672/#/queues/%2F/order.placed`):
     message count briefly increases then drops to 0 after the adapter processes it.
   - **WireMock request journal** (`http://localhost:8080/__admin/requests`):
     a `POST /api/orders` call from `OrderSyncAdapter` to the ERP stub.
   - **`Integration_OutboxMessage` table** (via SQL Server Management Studio or
     `docker exec` into the DB container): the row has `ProcessedOnUtc IS NOT NULL`.

### Step 5: Activate WMS degradation scenario

```bash
bash infrastructure/demo-scripts/activate-wms-failure.sh
```

This pushes the `wms-unavailable.json` fault profile to WireMock's Admin API,
making all WMS stock queries return HTTP 503.

Place another order. Observe:
- The **storefront accepts the order**: checkout does not fail.
- The **Outbox row** is written and subsequently published to RabbitMQ.
- **`OrderSyncAdapter` logs** show WMS calls failing with 503 and retries with
  exponential backoff.
- After 3 ERP retries are exhausted (for orders where ERP also fails), the message is
  routed to `order.placed.dlq` (visible in the RabbitMQ Management UI).
- The `order.placed` queue keeps accumulating messages from the Outbox publisher.

### Step 6: Restore WMS and observe recovery

```bash
bash infrastructure/demo-scripts/restore-wms.sh
```

This removes the fault profile from WireMock, restoring normal 200 OK responses.

Observe:
- `OrderSyncAdapter`'s circuit breaker closes (half-open → closed after first successful probe).
- `ReconciliationService` drains `order.placed.dlq` in order of creation.
- **WireMock request journal** shows one `POST /api/orders` per `OrderId`: confirming
  idempotency: no order is forwarded to the ERP twice.
- All rows in `Integration_OutboxMessage` end up with `ProcessedOnUtc IS NOT NULL`.

### Step 7: Restore all (clean slate for next run)

```bash
bash infrastructure/demo-scripts/restore-all.sh
```

### Available Scripts

#### `infrastructure/demo-scripts/`

Fault-injection and restore scripts that drive WireMock via its Admin API. All scripts respect the `WIREMOCK_URL` environment variable (default: `http://localhost:8080`).

| Script | Purpose |
|---|---|
| `activate-erp-failure.sh` | Injects a 503 on all `POST /api/orders` ERP calls: forces DLQ routing after 3 retries |
| `activate-wms-degraded.sh` | Injects a 10 s delay + 503 on `POST /api/wms/orders`: triggers timeout/circuit-breaker behaviour |
| `activate-wms-failure.sh` | Injects a 503 on all `POST /api/wms/orders` calls: simulates WMS hard failure |
| `restore-all.sh` | Resets all WireMock mappings to file-based defaults (clean slate) |
| `restore-erp.sh` | Removes the active ERP fault mapping, restoring normal 200 OK responses |
| `restore-wms.sh` | Removes the active WMS fault mapping, restoring normal 200 OK responses |

#### `infrastructure/helper-scripts/`

| Script | Purpose |
|---|---|
| `reset-demo-data.sh` | Clears orders, cart items, and outbox messages from SQL Server without touching nopCommerce settings or plugin configuration: useful for re-running demo scenarios from a clean state |

### Useful URLs

| URL | Description |
|---|---|
| `http://localhost:5000` | nopCommerce storefront |
| `http://localhost:5000/admin` | nopCommerce admin panel |
| `http://localhost:15672` | RabbitMQ Management UI |
| `http://localhost:8080/__admin/requests` | WireMock request journal |
| `http://localhost:8080/__admin/mappings` | WireMock active mappings |

---

## Original nopCommerce

This repository is forked from [nopSolutions/nopCommerce](https://github.com/nopSolutions/nopCommerce). nopCommerce is a free, open-source ASP.NET Core eCommerce platform. See the original project for full platform documentation.
