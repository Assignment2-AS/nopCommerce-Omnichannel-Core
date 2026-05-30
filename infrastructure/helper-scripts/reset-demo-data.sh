#!/usr/bin/env bash
# Clears orders, cart items and outbox messages without touching nopCommerce
# settings, plugin installation or admin configuration.

set -e

SQL_CMD="docker exec -i verdemart-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'VerdeMart_2026!' -C"

echo "[reset] Clearing demo data..."

echo "USE nopcommerce;
DELETE FROM Integration_OutboxMessage;
DELETE FROM ShoppingCartItem;
DELETE FROM OrderItem;
DELETE FROM [Order];
PRINT 'Done';" | eval "$SQL_CMD"

echo "[reset] Done. Orders, cart items and outbox messages cleared."
