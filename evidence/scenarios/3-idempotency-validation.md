# Scenario 3: Idempotency Validation (R3 Risk Mitigation)

## Objective
Ensure that accidental event duplication within the message broker (at-least-once delivery anomalies) does not corrupt the state of the target systems (ERP/WMS), mitigating architectural risk R3.

## Execution Steps and Evidence

1. **Duplicate Message Injection:**
   - Accessed the RabbitMQ Management UI (`http://localhost:15672`).
   - In the `order.placed.dlq`, manually published an order JSON payload (for example, `"orderId": 999`) using the "Publish Message" feature.
   - Immediately published the **exact same JSON payload** a second time, simulating a duplicated event.

2. **Service Filtering:**
   - The `ReconciliationService` processed the first message, performed the necessary HTTP calls, and stored the `OrderId` in its in-memory `_processedOrders` dictionary.
   - Upon reading the second identical message from the queue, the service detected the cached `OrderId` and skipped reprocessing.

![alt text](../reconciliation.png)

3. **Validation in WireMock Journal:**
   - Accessed the WireMock request log and confirmed the existence of only **one** HTTP POST call to the `/api/orders` endpoint associated with that specific order.
   - The second message was safely acknowledged (BasicAck) and discarded from the DLQ without generating duplicate traffic.

![alt text](../wiremock.png)
(ERP and WMS)