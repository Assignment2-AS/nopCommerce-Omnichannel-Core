using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using VerdeMart.OrderSyncAdapter.Models;
using VerdeMart.OrderSyncAdapter.Worker.Infrastructure;

namespace VerdeMart.OrderSyncAdapter.Worker;

public sealed class RabbitMqOrderConsumerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqOrderConsumerWorker> _logger;

    public RabbitMqOrderConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RabbitMqOrderConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _configuration.GetValue<string>("RabbitMq:ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("RabbitMq:ConnectionString configuration is required.");
        }

        // Worker is isolated from the web infrastructure and only reacts to queue events.
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(false, false, null, null), stoppingToken);

        await RabbitMqTopology.EnsureAsync(channel, stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            int? orderId = null;
            try
            {
                var payloadJson = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                var orderPayload = JsonSerializer.Deserialize<NopOrderPayload>(payloadJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (orderPayload is null)
                {
                    _logger.LogError("Invalid message received on queue {QueueName}.", RabbitMqTopology.OrderQueueName);
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    return;
                }

                orderId = orderPayload.OrderId;

                using var scope = _scopeFactory.CreateScope();
                var orderSyncAdapter = scope.ServiceProvider.GetRequiredService<IOrderSyncAdapter>();

                _logger.LogInformation("Processing order {OrderId} from queue {QueueName}.", orderPayload.OrderId, RabbitMqTopology.OrderQueueName);

                var result = await orderSyncAdapter.SyncOrderAsync(orderPayload, stoppingToken);

                if (result.IsSuccess)
                {
                    _logger.LogInformation("Order {OrderId} synced successfully. Acking message.", orderPayload.OrderId);
                    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                    return;
                }

                _logger.LogError(
                    "Failed to sync order {OrderId}. Message: {Message}. Routing to DLQ.",
                    orderPayload.OrderId,
                    result.Message);

                // Nack without requeue routes the message to the DLQ when sync fails definitively.
                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing message from queue {QueueName} for OrderId {OrderId}.", RabbitMqTopology.OrderQueueName, orderId);
                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: RabbitMqTopology.OrderQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("RabbitMQ worker started. Listening on queue {QueueName}.", RabbitMqTopology.OrderQueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Encerramento limpo do host.
        }
    }
}
