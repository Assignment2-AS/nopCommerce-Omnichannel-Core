#!/usr/bin/env bash
set -euo pipefail

WIREMOCK_URL="${WIREMOCK_URL:-http://localhost:8080}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROFILE="$SCRIPT_DIR/../wiremock/fault-profiles/wms-degraded.json"

echo "[demo] Injecting WMS fault: 10s delay + 503 on POST /api/wms/orders..."

http_code=$(curl -s -o /dev/null -w "%{http_code}" \
  -X POST "$WIREMOCK_URL/__admin/mappings" \
  -H "Content-Type: application/json" \
  -d @"$PROFILE")

if [ "$http_code" = "201" ]; then
  echo "[demo] WMS degraded fault ACTIVE (10s fixed delay then 503)"
  echo "[demo]   Useful for demonstrating circuit breaker timeout behaviour"
  echo "[demo]   Each WMS order-sync call blocks for 10s before failing — triggers timeout policy in OrderSyncAdapter"
else
  echo "[demo] ERROR: WireMock Admin API returned $http_code (expected 201)"
  exit 1
fi
