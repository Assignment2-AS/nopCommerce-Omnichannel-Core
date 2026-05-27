#!/usr/bin/env bash
set -euo pipefail

WIREMOCK_URL="${WIREMOCK_URL:-http://localhost:8080}"
ERP_UNAVAILABLE_ID="f1000003-0000-0000-0000-000000000000"

echo "[demo] Removing ERP fault injection..."

http_code=$(curl -s -o /dev/null -w "%{http_code}" \
  -X DELETE "$WIREMOCK_URL/__admin/mappings/$ERP_UNAVAILABLE_ID")

if [ "$http_code" = "200" ]; then
  echo "[demo] ERP fault removed — normal stub active (200 OK on POST /api/orders)"
elif [ "$http_code" = "404" ]; then
  echo "[demo] No active ERP fault found — already clean or never activated"
else
  echo "[demo] WARNING: unexpected response $http_code"
fi
