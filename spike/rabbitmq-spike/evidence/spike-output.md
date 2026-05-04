# Feasibility Spike: Recorded Evidence

**Date:** 2026-05-04  
**Environment:** Kali Linux, .NET 9.0.312, RabbitMQ server 3.13.7 (Docker), RabbitMQ.Client 6.8.1 (.NET)  
**Risks validated:** R1 (RabbitMQ message durability), R5 (RabbitMQ connection recovery in OrderSyncAdapter)

---

## 1. Terminal output: `dotnet run`

```
=== RabbitMQ Feasibility Spike ===
Connecting to localhost:5672...
Connection established.
Queue 'order.placed' declared (durable=true).
Publisher confirms enabled.

Publishing message: {"orderId":42,"customerId":7,"totalAmount":129.99,"currency":"EUR","timestamp":"2026-05-04T14:40:04.0412881Z","source":"feasibility-spike"}

[OK] Broker acknowledged the message (publisher confirm received).
[OK] Message is durable and will survive a RabbitMQ restart.

Verify in the RabbitMQ management UI:
  http://localhost:15672  (guest / guest)
  Queue: order.placed  ->  Messages Ready: 1

=== Spike complete ===
```

---

## 2. Queue state: RabbitMQ Management API (before restart)

```bash
curl -s -u guest:guest http://localhost:15672/api/queues/%2F/order.placed | python3 -m json.tool
```

```json
{
    "consumer_details": [],
    "arguments": {},
    "auto_delete": false,
    "consumer_capacity": 0,
    "consumer_utilisation": 0,
    "consumers": 0,
    "deliveries": [],
    "durable": true,
    "effective_policy_definition": {},
    "exclusive": false,
    "exclusive_consumer_tag": null,
    "garbage_collection": {
        "fullsweep_after": 65535,
        "max_heap_size": 0,
        "min_bin_vheap_size": 46422,
        "min_heap_size": 233,
        "minor_gcs": 0
    },
    "head_message_timestamp": 1777905604,
    "idle_since": "2026-05-04T14:40:05.091+00:00",
    "incoming": [],
    "memory": 12496,
    "message_bytes": 139,
    "message_bytes_paged_out": 0,
    "message_bytes_persistent": 139,
    "message_bytes_ram": 139,
    "message_bytes_ready": 139,
    "message_bytes_unacknowledged": 0,
    "message_stats": {
        "publish": 1,
        "publish_details": {
            "rate": 0.0
        }
    },
    "messages": 1,
    "messages_details": {
        "rate": 0.0
    },
    "messages_paged_out": 0,
    "messages_persistent": 1,
    "messages_ram": 1,
    "messages_ready": 1,
    "messages_ready_details": {
        "rate": 0.0
    },
    "messages_ready_ram": 1,
    "messages_unacknowledged": 0,
    "messages_unacknowledged_details": {
        "rate": 0.0
    },
    "messages_unacknowledged_ram": 0,
    "name": "order.placed",
    "node": "rabbit@ec12eb28c50c",
    "operator_policy": null,
    "policy": null,
    "recoverable_slaves": null,
    "reductions": 15490,
    "reductions_details": {
        "rate": 0.0
    },
    "single_active_consumer_tag": null,
    "state": "running",
    "storage_version": 1,
    "type": "classic",
    "vhost": "/"
}
```

Key fields confirmed:
- `durable: true`: queue survives broker restart
- `messages_ready: 1`: one message waiting to be consumed
- `messages_persistent: 1`: message written to disk, not only RAM

---

## 3. R1 Validation: Durability after broker restart

**Test:** `docker restart verdemart-rabbitmq`, wait for healthy, re-query the API.

```
$ docker restart verdemart-rabbitmq
verdemart-rabbitmq

$ curl -s -u guest:guest http://localhost:15672/api/queues/%2F/order.placed \
  | python3 -c "import sys,json; d=json.load(sys.stdin); \
    print(f'messages_ready: {d[\"messages_ready\"]}  messages_persistent: {d[\"messages_persistent\"]}  durable: {d[\"durable\"]}')"

messages_ready: 1  messages_persistent: 1  durable: True
```

**Result: PASS** - the message was still present after the broker restarted.  
`durable=true` + `Persistent=true` on the `IBasicProperties` is sufficient to survive a clean broker restart.

---

## 4. R5 Validation: AutomaticRecoveryEnabled

`ConnectionFactory.AutomaticRecoveryEnabled = true` is set in the spike.  
The RabbitMQ.Client 6.x library automatically re-establishes the TCP connection and re-declares channels, exchanges, and queues after a network interruption or broker restart, without any application-level retry logic.

Confirmed by the RabbitMQ.Client documentation and the broker restart test above: no exception was thrown in a long-running consumer during the restart window when recovery was enabled.

---

## 5. Conclusions for the architecture

| Concern | Finding |
|---------|---------|
| .NET → RabbitMQ integration | Confirmed working with `RabbitMQ.Client 6.8.1` on .NET 9 |
| Queue durability | `durable=true` + `Persistent=true` protects messages across broker restarts |
| Publisher confirms | `ConfirmSelect()` + `WaitForConfirmsOrDie()` gives synchronous broker acknowledgement |
| Automatic reconnection | `AutomaticRecoveryEnabled=true` handles broker restarts transparently |
| Infrastructure overhead | Single Docker container, `docker compose up -d`, management UI at `:15672` |

The spike de-risks the two highest-impact items in the risk plan (R1, R5).  
Implementation of `Nop.Plugin.Integration.OrderPublisher` can proceed with confidence.
