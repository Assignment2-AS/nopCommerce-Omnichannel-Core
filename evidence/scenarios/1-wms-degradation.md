# Scenario 1: WMS Degradation (Partial Degradation)

## Objective
Demonstrate system resilience during WMS degradation, including retry handling, circuit breaker behavior, stale-response fallback when available, and DLQ routing so messages are not lost.

## Execution Steps and Evidence

1. **Activate Failure Stub (503):**
   - In the terminal, navigated to the `infrastructure/demo-scripts/` directory and executed the failure script: `./activate-wms-failure.sh`.

![](../activate-wms-failure.png)

2. **Place Order in nopCommerce:**
   - Accessed the storefront, added a product to the cart, and completed the checkout process.
   - nopCommerce successfully accepted the order, proving that the checkout flow still completed even while the downstream WMS call was degraded.

3. **RabbitMQ Queue and Fallback Verification:**
   - Observed the `OrderSyncAdapter.Worker` terminal logs, confirming that the WMS call timed out, retries were attempted, and the circuit breaker opened after repeated failures.
   - In the WMS path, once the circuit is open and a cached response exists, the adapter can serve a stale fallback response.
   - Verified that the main Worker forwarded the failed message to the DLQ.

![alt text](../circuit-breaker-open.png)

4. **Dead-Letter Queue (DLQ) Accumulation:**
   - In the RabbitMQ Management UI (`http://localhost:15672`), confirmed that the main queue (`order.placed`) is empty, while the failed message was safely routed to and accumulated in the `order.placed.dlq` queue.
   - Verified that no HTTP requests reached the WMS WireMock endpoint.
   
![alt text](../order-dlq.png)