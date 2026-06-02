#!/usr/bin/env bash
set -euo pipefail

RABBITMQ_URL="${RABBITMQ_URL:-http://localhost:15672}"
RABBITMQ_USER="${RABBITMQ_USER:-guest}"
RABBITMQ_PASS="${RABBITMQ_PASS:-guest}"
VHOST="%2F"
QUEUE="wms.stock.update"

SKU="${1:-}"
QUANTITY="${2:-}"

if [[ -z "$SKU" || -z "$QUANTITY" ]]; then
  echo "Usage: $0 <sku> <quantity_delta>"
  echo "  <sku>            Product SKU (e.g. AP_MBP_13)"
  echo "  <quantity_delta> Stock change to apply in nopCommerce (positive = restock, negative = adjustment)"
  echo ""
  echo "Examples:"
  echo "  $0 AP_MBP_13 50      # WMS reports restock of 50 units"
  echo "  $0 AP_MBP_13 -10     # WMS reports stock correction of -10 units"
  exit 1
fi

PAYLOAD=$(printf '{"Sku":"%s","QuantityDelta":%s}' "$SKU" "$QUANTITY")

echo "[demo] Publishing WMS stock update: SKU=$SKU QuantityDelta=$QUANTITY"

http_code=$(curl -s -o /dev/null -w "%{http_code}" \
  -u "$RABBITMQ_USER:$RABBITMQ_PASS" \
  -X POST "$RABBITMQ_URL/api/exchanges/$VHOST/wms.stock/publish" \
  -H "Content-Type: application/json" \
  -d "{
    \"properties\": {\"content_type\": \"application/json\", \"delivery_mode\": 2},
    \"routing_key\": \"wms.stock.update\",
    \"payload\": $(echo "$PAYLOAD" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))'),
    \"payload_encoding\": \"string\"
  }")

if [ "$http_code" = "200" ]; then
  echo "[demo] Message published. Watch nopCommerce logs for:"
  echo "[demo]   [WmsStockSync] SKU '$SKU' stock updated: ... (delta $QUANTITY)"
else
  echo "[demo] ERROR: RabbitMQ Management API returned $http_code (expected 200)"
  exit 1
fi
