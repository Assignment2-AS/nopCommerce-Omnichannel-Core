using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.Plugin.Integration.OrderPublisher.Services;

/// <summary>
/// Background service that consumes <c>wms.stock.update</c> messages from RabbitMQ and
/// applies the WMS stock level to the corresponding nopCommerce product.
///
/// <para>
/// <b>Message contract (JSON):</b>
/// <code>{ "sku": "PROD-001", "quantity": 42 }</code>
/// <c>quantity</c> is the absolute on-hand level reported by the WMS, not a delta.
/// </para>
/// <para>
/// <b>Update strategy:</b> the service resolves the product by SKU via
/// <see cref="IProductService.GetProductBySkuAsync"/>, computes the delta between
/// the WMS quantity and the current <see cref="Product.StockQuantity"/>, and calls
/// <see cref="IProductService.AdjustInventoryAsync"/> so nopCommerce records a proper
/// stock history entry and evaluates low-stock thresholds.
/// Products with <c>ManageInventoryMethod != ManageStock</c> are skipped — they are
/// not quantity-managed and have no stock field to update.
/// </para>
/// <para>
/// <b>Idempotency:</b> applying the same absolute quantity twice is safe — the delta
/// will be zero on the second call and <see cref="IProductService.AdjustInventoryAsync"/>
/// returns early when <c>quantityToChange == 0</c>.
/// </para>
/// </summary>
public class WmsStockSyncService : BackgroundService
{
    #region Fields

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsStockSyncService> _logger;
    private IConnection _connection;
    private IModel _channel;

    private const string QueueName = "wms.stock.update";
    private const string ExchangeName = "wms.stock";

    #endregion

    #region Ctor

    public WmsStockSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<WmsStockSyncService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        InitialiseRabbitMq(configuration);
    }

    #endregion

    #region Utilities

    private void InitialiseRabbitMq(IConfiguration configuration)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672,
            UserName = configuration["RabbitMQ:Username"] ?? "guest",
            Password = configuration["RabbitMQ:Password"] ?? "guest",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        _channel.QueueBind(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: QueueName);

        // One message at a time: process and ack before the next delivery
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        _logger.LogInformation(
            "[WmsStockSync] Connected to RabbitMQ at {Host}:{Port}. Listening on {Queue}.",
            factory.HostName, factory.Port, QueueName);
    }

    private async Task ApplyStockUpdateAsync(string sku, int delta)
    {
        using var scope = _scopeFactory.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        var product = await productService.GetProductBySkuAsync(sku);

        if (product == null)
        {
            _logger.LogWarning("[WmsStockSync] SKU '{Sku}' not found in nopCommerce — message discarded.", sku);
            return;
        }

        if (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock)
        {
            _logger.LogDebug(
                "[WmsStockSync] SKU '{Sku}' uses {Method} — not quantity-managed, skipping.",
                sku, product.ManageInventoryMethod);
            return;
        }

        if (delta == 0)
        {
            _logger.LogDebug("[WmsStockSync] SKU '{Sku}' — delta is 0, no change.", sku);
            return;
        }

        var before = product.StockQuantity;

        await productService.AdjustInventoryAsync(
            product,
            quantityToChange: delta,
            message: "WMS sync");

        _logger.LogInformation(
            "[WmsStockSync] SKU '{Sku}' stock updated: {Before} → {After} (delta {Delta})",
            sku, before, before + delta, delta);
    }

    #endregion

    #region Methods

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += async (_, ea) =>
        {
            string? sku = null;
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Support both camelCase and PascalCase field names
                sku = (root.TryGetProperty("sku", out var skuEl) ? skuEl : root.GetProperty("Sku")).GetString();
                var delta = (root.TryGetProperty("quantityDelta", out var deltaEl) ? deltaEl : root.GetProperty("QuantityDelta")).GetInt32();

                if (string.IsNullOrWhiteSpace(sku))
                    throw new InvalidOperationException("Missing or empty 'sku' field.");

                await ApplyStockUpdateAsync(sku, delta);
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[WmsStockSync] Failed to process stock update for SKU '{Sku}'. Message requeued.",
                    sku ?? "<unknown>");

                // requeue: false -> message goes to DLQ if one is configured, preventing infinite retry loops
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

        // Keep the service alive until cancellation
        stoppingToken.WaitHandle.WaitOne();

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        try
        {
            _channel?.Close();
            _channel?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WmsStockSync] Error during RabbitMQ disposal.");
        }

        base.Dispose();
    }

    #endregion
}
