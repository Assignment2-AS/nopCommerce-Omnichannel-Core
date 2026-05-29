#!/usr/bin/env bash
set -euo pipefail

WIREMOCK_URL="${WIREMOCK_URL:-http://localhost:8080}"
WMS_UNAVAILABLE_ID="f1000001-0000-0000-0000-000000000000"
WMS_DEGRADED_ID="f1000002-0000-0000-0000-000000000000"

echo "[demo] Removing WMS fault injections..."

removed=0
for id in "$WMS_UNAVAILABLE_ID" "$WMS_DEGRADED_ID"; do
  http_code=$(curl -s -o /dev/null -w "%{http_code}" \
    -X DELETE "$WIREMOCK_URL/__admin/mappings/$id")
  if [ "$http_code" = "200" ]; then
    removed=$((removed + 1))
  fi
done

if [ "$removed" -gt 0 ]; then
  echo "[demo] WMS fault removed — normal stub active (200 OK on POST /api/wms/orders)"
  echo "[demo]"
  echo "[demo] Watch OrderSyncAdapter logs for:"
  echo "[demo]   - Circuit breaker: HALF-OPEN probe"
  echo "[demo]   - Circuit breaker: CLOSED (WMS responded 200)"
  echo "[demo]   - ReconciliationService: draining order.placed.dlq"
  echo "[demo]"
  echo "[demo] Verify idempotency in WireMock request journal:"
  echo "[demo]   $WIREMOCK_URL/__admin/requests"
  echo "[demo]   Each OrderId must appear exactly once in ERP POST /api/orders"
else
  echo "[demo] No active WMS fault found — already clean or never activated"
fi
