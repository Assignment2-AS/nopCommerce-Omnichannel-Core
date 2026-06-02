# Container Diagram (C4 Level 3)

**Author:** Carolina Reis | Technical Lead  
**Scenario:** Scenario C - Omnichannel Commerce Core  
**Last updated:** 2026-06-02

This diagram shows all runtime containers, their technology, exposed ports, persistent volumes, and communication paths. It bridges the logical bounded-context view and the actual `infrastructure/docker-compose.yml`.

---

## Diagram

```mermaid
graph TB
    Browser["Browser\n─────────\nCustomer / Operator\nHTTP :80"]

    subgraph HOST["Host machine"]

        subgraph NOP["Commerce Core: nopCommerce (ASP.NET Core)"]
            direction TB
            NopWeb["nopcommerce\n─────────────────────\nASP.NET Core 10 / C#\nPort 80 exposed\n\nCheckout · Cart · Catalogue\nCustomer Management\nNop.Plugin.Integration.OrderPublisher\n  └ OrderPlacedConsumer (event hook)\n  └ OutboxPublisherService (background, 2s poll)\n  └ WmsStockSyncService (background, reads RabbitMQ)"]
        end

        subgraph DB["Data layer"]
            MSSQL["verdemart-sqlserver\n─────────────────────\nSQL Server 2022 Developer\nPort 1433 exposed\n\nnopCommerce tables\nIntegration_OutboxMessage\n(shared, same instance)"]
        end

        subgraph MQ["Messaging layer"]
            RMQ["verdemart-rabbitmq\n─────────────────────\nRabbitMQ 3 + Management\nPort 5672 (AMQP)\nPort 15672 (Management UI)\n\nQueues (durable):\n  order.placed\n  order.placed.dlq\nExchange:\n  order.placed.dlx (direct)"]
        end

        subgraph WORKER["Order Integration: OrderSyncAdapter (.NET Worker Service)"]
            direction TB
            OSA["VerdeMart.OrderSyncAdapter.Worker\n─────────────────────────────────\n.NET 10 Worker Service / C#\n(no exposed port: outbound only)\n\nRabbitMqOrderConsumerWorker\n  └ consumes order.placed\n  └ Polly retry (3×, exponential backoff)\n  └ NACK → DLQ on exhaustion\n  └ Circuit Breaker (WMS, opens after 5 failures,\n    half-open after 30s)\nReconciliationService\n  └ drains order.placed.dlq on CB close event\n  └ idempotency via in-memory CorrelationId store"]
        end

        subgraph STUBS["External System Stubs: WireMock"]
            WM["verdemart-wiremock\n─────────────────────\nWireMock 3.3.1\nPort 8080 exposed\n\nERP Stub: POST /api/orders\nWMS Stub: GET /api/stock/{id}\nAdmin API: /__admin/**\n(fault injection / request journal)"]
        end

        subgraph VOL["Named volumes"]
            V1[("rabbitmq-data\n/var/lib/rabbitmq")]
            V2[("sqlserver-data\n/var/opt/mssql")]
        end

    end

    %% External access
    Browser -->|"HTTP :80\ncheckout, catalogue, admin"| NopWeb

    %% nopCommerce ↔ SQL Server
    NopWeb -->|"ADO.NET / EF\nport 1433\norder + outbox write\n(single transaction)"| MSSQL

    %% nopCommerce → RabbitMQ (publish)
    NopWeb -->|"AMQP :5672\nOutboxPublisherService\npublishes order.placed\n(2s poll, publisher confirm)"| RMQ

    %% nopCommerce ← RabbitMQ (stock sync inbound)
    RMQ -->|"AMQP :5672\nWmsStockSyncService\nconsumes stock.updated\n(optional inbound channel)"| NopWeb

    %% RabbitMQ → OrderSyncAdapter
    RMQ -->|"AMQP :5672\ndelivers order.placed\nprefetch = 1"| OSA

    %% OrderSyncAdapter → WireMock
    OSA -->|"HTTP :8080\nPOST /api/orders (ERP)"| WM
    OSA -->|"HTTP :8080\nGET /api/stock/{id} (WMS)\n[through circuit breaker]"| WM

    %% OrderSyncAdapter → RabbitMQ (NACK → DLQ, reconciliation ACK)
    OSA -->|"AMQP :5672\nNACK → DLQ\nReconciliationService ACK"| RMQ

    %% Volume mounts
    RMQ --- V1
    MSSQL --- V2
```

![Container Diagram](Container%20Diagram.png)

---

## Container inventory

| Container | Image / Runtime | Port(s) | Volume | Role |
|---|---|---|---|---|
| `nopcommerce` | ASP.NET Core 10 (Dockerfile) | 80 | — | Commerce core + Outbox plugin + stock sync |
| `verdemart-sqlserver` | SQL Server 2022 Developer | 1433 | `sqlserver-data` | Persistent store for orders + outbox |
| `verdemart-rabbitmq` | RabbitMQ 3 + Management | 5672, 15672 | `rabbitmq-data` | Message broker: main queue + DLQ |
| `VerdeMart.OrderSyncAdapter.Worker` | .NET 10 Worker (local run) | — | — | Async integration layer to ERP + WMS |
| `verdemart-wiremock` | WireMock 3.3.1 | 8080 | `./wiremock/mappings` | ERP + WMS stubs; fault injection |

> Note: `VerdeMart.OrderSyncAdapter.Worker` is run locally (`dotnet run`) during development. In a production deployment it would be packaged as a container alongside the others.

---

## Key design decisions visible in this diagram

- **No direct checkout → ERP/WMS path.** The only connection from the Commerce Core to external systems goes through the Outbox → RabbitMQ → OrderSyncAdapter chain. Checkout latency and availability are fully decoupled from ERP/WMS state.
- **Single SQL Server instance, shared by Outbox.** The Outbox table lives in the same database as the nopCommerce order tables, which is what makes the atomic write (order + outbox in one transaction) possible without distributed coordination.
- **OrderSyncAdapter has no inbound port.** It is a pure consumer. External systems cannot call it directly; they respond only to requests it initiates.
- **WireMock serves both stubs on one port.** ERP and WMS are distinguished by path (`/api/orders` vs `/api/stock/*`), not by port. The Admin API (`/__admin/**`) on the same port is used for fault injection and request journal queries during testing.
