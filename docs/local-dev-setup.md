# Local Development Setup

> VerdeMart Retail · nopCommerce-Omnichannel-Core  
> Last updated: 2026-05-26

## Prerequisites

- Docker + Docker Compose
- .NET 10 SDK (`~/.dotnet/dotnet --version` should return `10.0.x`)

Install .NET 10 if missing:
```bash
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --version 10.0.100
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet
```

---

## 1. Start infrastructure

```bash
docker compose -f infrastructure/docker-compose.yml up -d
```

Services started:

| Container | Purpose | URL |
|-----------|---------|-----|
| `verdemart-rabbitmq` | Message broker | `http://localhost:15672` (Management UI) |
| `verdemart-sqlserver` | SQL Server 2022 | `localhost,1433` |
| `verdemart-wiremock` | ERP + WMS stubs | `http://localhost:8080/__admin/mappings` |

RabbitMQ credentials: `guest` / `guest`  
SQL Server credentials: `sa` / `VerdeMart_2026!`

---

## 2. Build the plugin

The compiled plugin output is not committed to the repository (it is in `.gitignore`).
Build it once before starting nopCommerce. Without this step the plugin will **not appear** in the admin plugin list:

```bash
cd src/Plugins/Nop.Plugin.Integration.OrderPublisher
~/.dotnet/dotnet build
```

This copies the plugin DLLs and `logo.png` (sourced from `plugin-logo.png` at the repo root) to `src/Presentation/Nop.Web/Plugins/Integration.OrderPublisher/`.  
Only needed again if you change the plugin code.

---

## 3. Run nopCommerce

**First run** (compiles the full solution: takes ~2 min):
```bash
cd src/Presentation/Nop.Web
~/.dotnet/dotnet run
```

**Subsequent runs** (skips the build step, starts faster):
```bash
cd src/Presentation/Nop.Web
~/.dotnet/dotnet run --no-build
```

> `--no-build` is required on Linux: the `ClearPluginAssemblies` build step exits with code 150 on subsequent runs, blocking `dotnet run` without it.

Storefront: `http://localhost:5050`  
Port is fixed in `src/Presentation/Nop.Web/App_Data/appsettings.json` under `Kestrel.Endpoints.Http.Url`.

```bash
"Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5050"
      }
    }
  },
```

---

## 4. First-time installation wizard

On first run, `http://localhost:5050` shows the nopCommerce installation wizard.

### Store information
| Field | Value |
|-------|-------|
| Admin email | admin@gmail.com |
| Admin password | `admin1234` |

### Database

Use the **manual fields** (do **not** tick "Enter raw connection string"):

| Field | Value |
|-------|-------|
| Database type | SQL Server |
| Server name | `localhost,1433` |
| Database name | `nopcommerce` |
| Username | `sa` |
| Password | `VerdeMart_2026!` |
| Create database if it doesn't exist | ✅ |

> **VPN note:** disable any VPN before running the installation wizard. An active VPN can intercept port 1433 and cause a pre-login TLS handshake timeout.

### Sample data
- Install sample data: ✅ (creates products, customers and orders for testing)

Click **Install** and wait ~2–3 minutes.

---

## 5. Install the OrderPublisher plugin

After the wizard completes:

1. Login to admin: `http://localhost:5050/admin`
2. Go to **Configuration → Local plugins**
3. Find **Order Publisher (VerdeMart Omnichannel)** → click **Install**
4. Restart the application when prompted (`~/.dotnet/dotnet run --no-build` again)

> **Plugin not in the list?** You skipped step 2: build the plugin first so the DLLs are present in `Nop.Web/Plugins/`.

> **Logo not showing?** The build in step 2 also copies `logo.png` to the plugin output folder. If the logo is missing, re-run `dotnet build` in the plugin directory.

This runs `SchemaMigration.cs` and creates the `Integration_OutboxMessage` table.

Verify:
```bash
echo "USE nopcommerce; SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Integration_OutboxMessage'" | docker exec -i verdemart-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'VerdeMart_2026!' -C
```

Expected output: one row returned.

---

## 6. RabbitMQ configuration

Already set in `src/Presentation/Nop.Web/App_Data/appsettings.json`:

```json
"RabbitMQ": {
  "Host": "localhost",
  "Port": "5672",
  "Username": "guest",
  "Password": "guest"
}
```

---

## 7. Verify end-to-end (Outbox Pipeline)

### Enable shipping for test orders

nopCommerce requires a shipping method to complete an order. The Fixed Rate Shipping plugin must be built, installed, and activated:

**1. Build all plugins** (needed once -> builds Fixed Rate Shipping alongside the others):
```bash
cd src
~/.dotnet/dotnet build NopCommerce.sln
```

> The build will report errors for some plugins (exit code 150 from `ClearPluginAssemblies`) -> this is expected on Linux. The shipping plugin is compiled successfully regardless.

**2. Install the plugins:**
1. Go to **Configuration → Local plugins**
2. Find **Fixed Rate Shipping** (`Shipping.FixedByWeightByTotal`) → click **Install**
3. Find **Check / Money Order** (`Payments.CheckMoneyOrder`) → click **Install**
4. Restart the application (`~/.dotnet/dotnet run --no-build`)

**3. Activate and configure:**

Shipping:
1. Go to **Configuration → Shipping → Shipping providers**
2. Find **Fixed Rate Shipping** → click **Edit** → **Configure** → set rate to `0` → **Save**
3. Go back and click **Activate**

Payment:
1. Go to **Configuration → Payment methods**
2. Find **Check / Money Order** → click **Activate**

### Place a test order

1. Place a test order on the storefront (`http://localhost:5050`)
2. Immediately query the Outbox:

```bash
echo "USE nopcommerce; SELECT CorrelationId, EventType, CreatedOnUtc, ProcessedOnUtc FROM Integration_OutboxMessage ORDER BY CreatedOnUtc DESC" | docker exec -i verdemart-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'VerdeMart_2026!' -C
```

- `ProcessedOnUtc` = NULL → row written, not yet published  
- `ProcessedOnUtc` = timestamp (after ~2 s) → published to RabbitMQ

3. Confirm message in RabbitMQ:  
   `http://localhost:15672` → Queues → `order.placed` → Get messages

---

## Tear down

```bash
docker compose -f infrastructure/docker-compose.yml down
# To also remove volumes (wipes the database):
docker compose -f infrastructure/docker-compose.yml down -v
```
