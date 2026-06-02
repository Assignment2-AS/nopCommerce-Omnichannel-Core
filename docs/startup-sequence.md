# Startup Sequence Diagram

**Author:** Carolina Reis | Technical Lead  
**Scenario:** Scenario C - Omnichannel Commerce Core  
**Last updated:** 2026-06-02

This diagram shows the initialization order of all containers and components, what each one waits for before it becomes operational, and the consequence of starting out of order.

---

## Diagram

```mermaid
sequenceDiagram
    actor Operator
    participant SQL as SQL Server
    participant RMQ as RabbitMQ
    participant WM as WireMock
    participant NOP as nopCommerce
    participant OSA as OrderSyncAdapter

    note over SQL,WM: Phase 1: Infrastructure (docker compose -f infrastructure/docker-compose.yml up -d)

    Operator->>SQL: starts sqlserver
    SQL-->>SQL: SQL Server engine starts<br/>listens on :1433
    note right of SQL: ~10-15s until first query accepted

    Operator->>RMQ: starts rabbitmq
    RMQ-->>RMQ: Broker starts, management plugin loads<br/>listens on :5672 + :15672
    note right of RMQ: healthcheck: rabbitmq-diagnostics ping<br/>interval 10s, 5 retries → ~50s worst case

    Operator->>WM: starts wiremock
    WM-->>WM: Loads mappings from ./wiremock/mappings<br/>listens on :8080
    note right of WM: Ready immediately after mapping load

    note over NOP,OSA: Phase 2: Application layer (dotnet run: both run locally)

    Operator->>NOP: cd src/Presentation/Nop.Web<br/>dotnet run --no-build
    NOP->>SQL: Open connection, run EF migrations<br/>ensure Integration_OutboxMessage table exists
    SQL-->>NOP: Schema ready
    NOP-->>NOP: ASP.NET Core pipeline starts<br/>listens on :5050
    NOP->>RMQ: OutboxPublisherService starts<br/>connects AMQP :5672
    RMQ-->>NOP: Connection accepted
    note right of NOP: OutboxPublisherService begins 2s poll loop<br/>WmsStockSyncService subscribes to stock.updated

    Operator->>OSA: cd src/Workers/VerdeMart.OrderSyncAdapter.Worker<br/>dotnet run
    OSA->>RMQ: Connect AMQP :5672<br/>AutomaticRecoveryEnabled = true
    RMQ-->>OSA: Connection accepted
    OSA->>RMQ: RabbitMqTopology.EnsureAsync<br/>DeclareExchange: order.placed.dlx<br/>DeclareQueue: order.placed.dlq<br/>DeclareQueue: order.placed (x-dead-letter-exchange)<br/>BindQueue: dlq → dlx
    RMQ-->>OSA: Topology confirmed
    OSA-->>OSA: RabbitMqOrderConsumerWorker: LISTENING<br/>ReconciliationService: waiting for CB closed event
    note right of OSA: BasicQos prefetchCount=1<br/>One message in-flight per consumer

    note over NOP,OSA: Phase 3: System fully operational

    note over SQL,OSA: All components healthy:<br/>Checkout available · Events flowing · Integration active
```

<div style="background-color: white; padding: 8px; display: inline-block;">
  <img src="Startup%20Sequence%20Diagram.png" alt="Startup Sequence Diagram"/>
</div>

---

## Startup dependencies

| Component | Start command | Hard dependency | Consequence if missing |
|---|---|---|---|
| `nopCommerce` | `dotnet run --no-build` (Nop.Web) | SQL Server `:1433` reachable | ASP.NET startup fails: EF migration cannot run |
| `nopCommerce` | — | RabbitMQ `:5672` (soft) | App starts, but `OutboxPublisherService` retries connection; no events published until RabbitMQ is up |
| `OrderSyncAdapter` | `dotnet run` (Worker) | RabbitMQ `:5672` | Worker exits: `CreateConnectionAsync` throws; must be restarted manually |
| `OrderSyncAdapter` | — | WireMock `:8080` | Worker starts; first sync attempt fails → Polly retries → DLQ. WireMock can be started after without data loss |
| `OrderSyncAdapter` | — | SQL Server | No dependency: the Worker has no database connection |

---

## Safe startup order

```
1. docker compose -f infrastructure/docker-compose.yml up -d
   (starts sqlserver, rabbitmq, wiremock: wait ~50s for rabbitmq healthcheck)
2. cd src/Presentation/Nop.Web && dotnet run --no-build
   (wait for SQL Server :1433 to be ready; RabbitMQ already up)
3. cd src/Workers/VerdeMart.OrderSyncAdapter.Worker && dotnet run
   (RabbitMQ must be healthy first)
```

Steps 1–3 reflect the actual local dev workflow: infrastructure runs in Docker, nopCommerce and OrderSyncAdapter run as local `dotnet run` processes.

---

## Recovery after component restart

| Restarted component | Effect | Automatic recovery |
|---|---|---|
| SQL Server | nopCommerce loses DB connection; checkout fails until reconnect | EF connection resilience retries on next request |
| RabbitMQ | Both `OutboxPublisherService` and `OrderSyncAdapter` lose AMQP connection | `AutomaticRecoveryEnabled = true` on both sides: reconnects and re-declares topology automatically |
| WireMock | In-flight HTTP calls to ERP/WMS fail; Polly retries absorb transient gap | When WireMock is back, Polly succeeds; DLQ messages are reconciled on next CB close |
| OrderSyncAdapter | No messages lost: `order.placed` queue is durable; messages accumulate | Restart Worker; `ReconciliationService` drains DLQ on startup if CB is already closed |
| nopCommerce | Checkout unavailable; Outbox poller stops; no new events published | Re-run `dotnet run --no-build`; Outbox resumes from last unprocessed row (`ProcessedAt IS NULL`) |
