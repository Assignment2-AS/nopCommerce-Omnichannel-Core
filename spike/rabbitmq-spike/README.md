# RabbitMQ Feasibility Spike

**Scenario:** Scenario C - Omnichannel Commerce Core  
**Purpose:** Validate that .NET can connect to RabbitMQ, declare a durable queue, and publish a persistent message with publisher confirms before investing in the full plugin implementation.

**Risks validated:**
- [R1: RabbitMQ message durability](../../docs/risk-plan.md#r1-rabbitmq-message-durability) (durable queue + persistent delivery)
- [R5: RabbitMQ connection recovery in OrderSyncAdapter](../../docs/risk-plan.md#r5-rabbitmq-connection-recovery-in-ordersyncadapter) (automatic reconnection after broker restart)

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download) or later
- Docker

---

## 1. Start RabbitMQ

From the `infrastructure/` directory at the root of this repository:

```bash
docker compose up -d rabbitmq
```

Wait until the management UI is available at [http://localhost:15672](http://localhost:15672) (credentials: `guest` / `guest`).

---

## 2. Run the spike

From this directory (`spike/rabbitmq-spike/`):

```bash
dotnet run
```

Expected output:

```
=== RabbitMQ Feasibility Spike ===
Connecting to localhost:5672...
Connection established.
Queue 'order.placed' declared (durable=true).
Publisher confirms enabled.

Publishing message: {"orderId":42,"customerId":7,"totalAmount":129.99,...}

[OK] Broker acknowledged the message (publisher confirm received).
[OK] Message is durable and will survive a RabbitMQ restart.

Verify in the RabbitMQ management UI:
  http://localhost:15672  (guest / guest)
  Queue: order.placed  ->  Messages Ready: 1

=== Spike complete ===
```

---

## 3. Verify durability (R1 validation)

1. Run the spike - message appears in the queue.
2. Restart the RabbitMQ container:
   ```bash
   docker compose restart rabbitmq
   ```
3. Refresh the management UI at [http://localhost:15672](http://localhost:15672).
4. Navigate to **Queues → order.placed**.
5. The message is still present with **Messages Ready: 1**.

This confirms that `durable=true` + `DeliveryMode.Persistent` protects messages across broker restarts.

---

## 4. Verify automatic reconnection (R5 validation)

In `Program.cs`, add a `Console.ReadLine()` pause after the publish confirm so the process stays alive during the broker restart.

**Before** (line 86, after `WaitForConfirmsOrDie`):
```csharp
Console.WriteLine("\n[OK] Broker acknowledged the message (publisher confirm received).");
Console.WriteLine($"[OK] Message is durable and will survive a RabbitMQ restart.");
```

**After** (insert two lines between them):
```csharp
Console.WriteLine("\n[OK] Broker acknowledged the message (publisher confirm received).");
Console.WriteLine("[WAITING] Restart RabbitMQ now, then press Enter to continue...");
Console.ReadLine();
Console.WriteLine($"[OK] Message is durable and will survive a RabbitMQ restart.");
```

Then:
1. Run `dotnet run`: it pauses at the `ReadLine`.
2. In a second terminal: `docker compose -f infrastructure/docker-compose.yml restart rabbitmq`
3. Wait ~10 seconds for the broker to come back up.
4. Press Enter in the spike terminal.
5. No exception is thrown: `AutomaticRecoveryEnabled = true` re-established the connection transparently.

---

## What this is not

This spike is a standalone console app (it has no dependency on nopCommerce).  
It exists only to prove the integration is viable before building the full `Nop.Plugin.Integration.OrderPublisher`.  
See the evidence directory for recorded output: `spike/rabbitmq-spike/evidence/`.
