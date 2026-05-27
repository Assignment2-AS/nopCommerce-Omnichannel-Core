#!/usr/bin/env bash
set -euo pipefail

WIREMOCK_URL="${WIREMOCK_URL:-http://localhost:8080}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROFILE="$SCRIPT_DIR/../wiremock/fault-profiles/wms-unavailable.json"

echo "[demo] Injecting WMS fault: 503 on all GET /api/stock/* calls..."

http_code=$(curl -s -o /dev/null -w "%{http_code}" \
  -X POST "$WIREMOCK_URL/__admin/mappings" \
  -H "Content-Type: application/json" \
  -d @"$PROFILE")

if [ "$http_code" = "201" ]; then
  echo "[demo] WMS fault ACTIVE (503 Service Unavailable)"
  echo "[demo]"
  echo "[demo] Now place an order in nopCommerce and observe:"
  echo "[demo]   1. Checkout succeeds — nopCommerce is unaffected"
  echo "[demo]   2. OrderSyncAdapter retries 3x (Polly: 1s / 2s / 4s backoff)"
  echo "[demo]   3. After 3 failures, message moves to order.placed.dlq"
  echo "[demo]   4. After 5 consecutive failures, circuit breaker OPENS"
  echo "[demo]"
  echo "[demo] RabbitMQ Management UI: http://localhost:15672"
  echo "[demo] WireMock request journal: $WIREMOCK_URL/__admin/requests"
else
  echo "[demo] ERROR: WireMock Admin API returned $http_code (expected 201)"
  echo "[demo]   Is WireMock running?  docker compose -f infrastructure/docker-compose.yml up wiremock"
  exit 1
fi
