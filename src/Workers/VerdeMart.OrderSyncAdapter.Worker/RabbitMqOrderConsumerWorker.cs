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

namespace VerdeMart.OrderSyncAdapter.Worker;

public sealed class RabbitMqOrderConsumerWorker : BackgroundService
{
    private const string QueueName = "order.placed";
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
            throw new InvalidOperationException("A configuração RabbitMq:ConnectionString é obrigatória.");
        }

        // O worker fica isolado da infraestrutura web e só reage a eventos da fila.
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(false, false, null, null), stoppingToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var orderId = string.Empty;
            try
            {
                var payloadJson = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                var orderPayload = JsonSerializer.Deserialize<NopOrderPayload>(payloadJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (orderPayload is null)
                {
                    _logger.LogError("Mensagem inválida recebida na fila {QueueName}.", QueueName);
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
                    return;
                }

                orderId = orderPayload.OrderId;

                using var scope = _scopeFactory.CreateScope();
                var orderSyncAdapter = scope.ServiceProvider.GetRequiredService<IOrderSyncAdapter>();

                _logger.LogInformation("A processar encomenda {OrderId} da fila {QueueName}.", orderPayload.OrderId, QueueName);

                var result = await orderSyncAdapter.SyncOrderAsync(orderPayload, stoppingToken);

                if (result.IsSuccess)
                {
                    _logger.LogInformation("Encomenda {OrderId} sincronizada com sucesso. Ack da mensagem.", orderPayload.OrderId);
                    await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                    return;
                }

                _logger.LogError(
                    "Falha ao sincronizar a encomenda {OrderId}. Message: {Message}. Nack com requeue.",
                    orderPayload.OrderId,
                    result.Message);

                // Nack com requeue garante at-least-once delivery e evita perda da mensagem.
                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado ao processar a mensagem da fila {QueueName} para OrderId {OrderId}.", QueueName, orderId);
                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Worker RabbitMQ iniciado e à escuta da fila {QueueName}.", QueueName);

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
