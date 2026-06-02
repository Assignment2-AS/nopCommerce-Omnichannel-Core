using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using VerdeMart.OrderSyncAdapter.Infrastructure;
using VerdeMart.OrderSyncAdapter.Models;

namespace VerdeMart.OrderSyncAdapter.Worker.Infrastructure;

/// <summary>
/// Publishes WMS stock level updates to the <c>wms.stock.update</c> queue so that
/// the nopCommerce plugin (<c>WmsStockSyncService</c>) can apply them to product stock.
/// </summary>
public sealed class RabbitMqWmsStockPublisher : IWmsStockPublisher, IAsyncDisposable
{
    private readonly ILogger<RabbitMqWmsStockPublisher> _logger;
    private IConnection _connection = null!;
    private IChannel _channel = null!;

    private const string ExchangeName = "wms.stock";
    private const string RoutingKey = "wms.stock.update";

    public RabbitMqWmsStockPublisher(
        IConfiguration configuration,
        ILogger<RabbitMqWmsStockPublisher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitialiseRabbitMq(configuration);
    }

    private void InitialiseRabbitMq(IConfiguration configuration)
    {
        var connectionString = configuration.GetValue<string>("RabbitMq:ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("RabbitMq:ConnectionString configuration is required.");

        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

        _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false).GetAwaiter().GetResult();

        _channel.QueueDeclareAsync(
            queue: RoutingKey,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null).GetAwaiter().GetResult();

        _channel.QueueBindAsync(
            queue: RoutingKey,
            exchange: ExchangeName,
            routingKey: RoutingKey).GetAwaiter().GetResult();

        _logger.LogInformation(
            "[WmsStockPublisher] Connected to RabbitMQ. Publishing to {Exchange} → {RoutingKey}.",
            ExchangeName, RoutingKey);
    }

    public async Task PublishAsync(WmsStockPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var json = JsonSerializer.Serialize(payload);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json"
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[WmsStockPublisher] Stock update published: SKU={Sku} QuantityDelta={QuantityDelta}.",
            payload.Sku, payload.QuantityDelta);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel is not null)
                await _channel.CloseAsync();
            if (_connection is not null)
                await _connection.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WmsStockPublisher] Error closing RabbitMQ connection.");
        }
    }
}
