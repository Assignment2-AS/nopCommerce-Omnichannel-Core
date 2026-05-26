#!/usr/bin/env bash
set -euo pipefail

WIREMOCK_URL="${WIREMOCK_URL:-http://localhost:8080}"

echo "[demo] Resetting WireMock to file-based mappings (clean state)..."

http_code=$(curl -s -o /dev/null -w "%{http_code}" \
  -X POST "$WIREMOCK_URL/__admin/mappings/reset")

if [ "$http_code" = "200" ]; then
  echo "[demo] All fault injections removed"
  echo "[demo] Active stubs restored:"
  echo "[demo]   ERP  — POST /api/orders       → 200 OK"
  echo "[demo]   WMS  — GET  /api/stock/{id}   → 200 OK + stock level"
else
  echo "[demo] ERROR: WireMock Admin API returned $http_code"
  exit 1
fi
