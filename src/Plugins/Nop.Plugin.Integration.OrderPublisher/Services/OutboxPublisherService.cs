using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nop.Data;
using Nop.Plugin.Integration.OrderPublisher.Domain;
using RabbitMQ.Client;

namespace Nop.Plugin.Integration.OrderPublisher.Services;

/// <summary>
/// Background service that drains the <see cref="OutboxMessage"/> table to RabbitMQ.
///
/// <para>
/// <b>Polling loop:</b> every 2 s, reads all rows where <c>ProcessedOnUtc IS NULL</c>
/// ordered by <c>CreatedOnUtc ASC</c> (FIFO), publishes each one as a persistent message
/// to the <c>order.placed</c> exchange with publisher confirms, and only marks
/// <c>ProcessedOnUtc = DateTime.UtcNow</c> after the broker ack is received.
/// </para>
/// <para>
/// <b>Why a BackgroundService and not the consumer itself:</b><br/>
/// The <see cref="Consumers.OrderPlacedConsumer"/> writes to the Outbox during the HTTP
/// request that places the order. Publishing to RabbitMQ in that same request would couple
/// checkout availability to broker availability. This service decouples the two: the order
/// always succeeds; the RabbitMQ publish is a separate concern that retries automatically.
/// </para>
/// <para>
/// <b>At-least-once delivery:</b><br/>
/// If the process crashes after publishing but before marking <c>ProcessedOnUtc</c>,
/// the message will be republished on restart. The <see cref="Domain.OutboxMessage.CorrelationId"/>
/// (= OrderId) allows the downstream <c>OrderSyncAdapter</c> to deduplicate via idempotency key.
/// This limitation is documented in <c>evidence/known-limitations.md</c>.
/// </para>
/// <para>
/// <b>IRepository lifetime:</b><br/>
/// <c>IRepository&lt;T&gt;</c> is registered as Scoped (see <c>NopDbStartup.cs</c>).
/// This Singleton BackgroundService creates a new DI scope per polling iteration via
/// <see cref="IServiceScopeFactory"/> to avoid captive dependency issues.
/// </para>
/// </summary>
public class OutboxPublisherService : BackgroundService
{
    #region Fields

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherService> _logger;

    // RabbitMQ connection -> kept alive for the lifetime of the service.
    // AutomaticRecoveryEnabled handles broker restarts (validated in spike/rabbitmq-spike).
    private IConnection _connection;
    private IModel _channel;

    private const string ExchangeName = "order.placed";
    private const string QueueName = "order.placed";
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PublisherConfirmTimeout = TimeSpan.FromSeconds(5);

    #endregion

    #region Ctor

    public OutboxPublisherService(IServiceScopeFactory scopeFactory, ILogger<OutboxPublisherService> logger, IConfiguration configuration)
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
            // Automatically reconnects after broker restart (R4 risk from spike)
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare the exchange as durable (survives broker restart)
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        // Declare the DLX and DLQ so the queue arguments match what OrderSyncAdapter declares
        _channel.ExchangeDeclare(
            exchange: "order.placed.dlx",
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: "order.placed.dlq",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        _channel.QueueBind(
            queue: "order.placed.dlq",
            exchange: "order.placed.dlx",
            routingKey: "order.placed.dlq");

        // Declare the queue as durable with DLX so both services agree on queue arguments
        _channel.QueueDeclare(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "order.placed.dlx",
                ["x-dead-letter-routing-key"] = "order.placed.dlq"
            });

        _channel.QueueBind(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: QueueName);

        // Enable publisher confirms: broker acks each message after persisting it
        _channel.ConfirmSelect();

        _logger.LogInformation(
            "[OutboxPublisher] Connected to RabbitMQ at {Host}:{Port}",
            factory.HostName, factory.Port);
    }

    private async Task DrainOutboxAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<OutboxMessage>>();

        // Read pending messages FIFO -> oldest first to preserve order
        var pending = (await repository.GetAllAsync(
            query => query
                .Where(m => m.ProcessedOnUtc == null)
                .OrderBy(m => m.CreatedOnUtc)
        )).ToList();

        if (pending.Count == 0)
            return;

        _logger.LogDebug("[OutboxPublisher] Draining {Count} pending message(s)", pending.Count);

        foreach (var message in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                PublishToRabbitMq(message);

                // Mark as processed ONLY after broker ack
                message.ProcessedOnUtc = DateTime.UtcNow;
                await repository.UpdateAsync(message, publishEvent: false);

                _logger.LogInformation(
                    "[OutboxPublisher] Published and marked processed: CorrelationId={CorrelationId} EventType={EventType}",
                    message.CorrelationId, message.EventType);
            }
            catch (Exception ex)
            {
                // Log and continue -> this message will be retried on the next poll
                _logger.LogError(ex,
                    "[OutboxPublisher] Failed to publish CorrelationId={CorrelationId}. Will retry on next poll.",
                    message.CorrelationId);
            }
        }
    }

    private void PublishToRabbitMq(OutboxMessage message)
    {
        var body = Encoding.UTF8.GetBytes(message.Payload);

        var properties = _channel.CreateBasicProperties();
        // Persistent delivery mode: message survives broker restart
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = Guid.NewGuid().ToString();
        properties.CorrelationId = message.CorrelationId;
        properties.Type = message.EventType;
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: QueueName,
            mandatory: true,
            basicProperties: properties,
            body: body);

        // Block until broker confirms persistence -> throws if timeout exceeded
        _channel.WaitForConfirmsOrDie(PublisherConfirmTimeout);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Main polling loop. Runs every <see cref="PollingInterval"/> until cancellation.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[OutboxPublisher] Service started. Polling every {Interval}s", PollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOutboxAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown -> expected when app is stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OutboxPublisher] Unhandled error in polling loop. Continuing after next interval.");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }

        _logger.LogInformation("[OutboxPublisher] Service stopped.");
    }

    /// <summary>
    /// Disposes the RabbitMQ channel and connection when the service is stopped.
    /// </summary>
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
            _logger.LogWarning(ex, "[OutboxPublisher] Error during RabbitMQ disposal.");
        }

        base.Dispose();
    }

    #endregion
}
