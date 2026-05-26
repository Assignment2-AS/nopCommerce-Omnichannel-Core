#!/usr/bin/env bash
set -euo pipefail

WIREMOCK_URL="${WIREMOCK_URL:-http://localhost:8080}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROFILE="$SCRIPT_DIR/../wiremock/fault-profiles/erp-unavailable.json"

echo "[demo] Injecting ERP fault: 503 on all POST /api/orders calls..."

http_code=$(curl -s -o /dev/null -w "%{http_code}" \
  -X POST "$WIREMOCK_URL/__admin/mappings" \
  -H "Content-Type: application/json" \
  -d @"$PROFILE")

if [ "$http_code" = "201" ]; then
  echo "[demo] ERP fault ACTIVE (503 Service Unavailable)"
  echo "[demo]   OrderSyncAdapter will retry 3x (Polly exponential backoff)"
  echo "[demo]   After exhaustion, message is published to order.placed.dlq"
else
  echo "[demo] ERROR: WireMock Admin API returned $http_code (expected 201)"
  exit 1
fi
