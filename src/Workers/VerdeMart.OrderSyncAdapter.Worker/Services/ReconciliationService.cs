using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using VerdeMart.OrderSyncAdapter.Models;
using VerdeMart.OrderSyncAdapter.Infrastructure;
using VerdeMart.OrderSyncAdapter.Worker.Infrastructure;

namespace VerdeMart.OrderSyncAdapter.Worker.Services;

public sealed class ReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly WmsCircuitBreakerStateTracker _circuitBreakerStateTracker;
    private readonly ILogger<ReconciliationService> _logger;
    private readonly ConcurrentDictionary<int, byte> _processedOrders = new();

    public ReconciliationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        WmsCircuitBreakerStateTracker circuitBreakerStateTracker,
        ILogger<ReconciliationService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _circuitBreakerStateTracker = circuitBreakerStateTracker ?? throw new ArgumentNullException(nameof(circuitBreakerStateTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _configuration.GetValue<string>("RabbitMq:ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A configuração RabbitMq:ConnectionString é obrigatória.");
        }

        // O serviço de reconciliação consome a DLQ e tenta recuperar encomendas após a indisponibilidade.
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(false, false, null, null), stoppingToken);

        await RabbitMqTopology.EnsureAsync(channel, stoppingToken);

        _logger.LogInformation("ReconciliationService iniciado. A aguardar estado Closed do circuito do WMS.");

        if (_circuitBreakerStateTracker.IsClosed)
        {
            await DrainDeadLetterQueueAsync(channel, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await _circuitBreakerStateTracker.WaitForClosedTransitionAsync(stoppingToken);
            await DrainDeadLetterQueueAsync(channel, stoppingToken);
        }
    }

    private async Task DrainDeadLetterQueueAsync(IChannel channel, CancellationToken cancellationToken)
    {
        _logger.LogInformation("A drenar a DLQ {QueueName} após recuperação do WMS.", RabbitMqTopology.DeadLetterQueueName);

        var bufferedMessages = new List<(BasicGetResult Delivery, NopOrderPayload Payload)>();

        while (await channel.BasicGetAsync(RabbitMqTopology.DeadLetterQueueName, autoAck: false, cancellationToken) is { } delivery)
        {
            var payloadJson = Encoding.UTF8.GetString(delivery.Body.ToArray());
            var orderPayload = JsonSerializer.Deserialize<NopOrderPayload>(payloadJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (orderPayload is null)
            {
                _logger.LogError("Mensagem inválida encontrada na DLQ {QueueName}.", RabbitMqTopology.DeadLetterQueueName);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: cancellationToken);
                continue;
            }

            bufferedMessages.Add((delivery, orderPayload));
        }

        foreach (var item in bufferedMessages
                     .OrderBy(message => message.Payload.CreatedAtUtc)
                     .ThenBy(message => message.Payload.OrderId))
        {
            if (_processedOrders.ContainsKey(item.Payload.OrderId))
            {
                _logger.LogInformation("Encomenda {OrderId} já reconciliada anteriormente. Ack da mensagem na DLQ.", item.Payload.OrderId);
                await channel.BasicAckAsync(item.Delivery.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                continue;
            }

            using var scope = _scopeFactory.CreateScope();
            var orderSyncAdapter = scope.ServiceProvider.GetRequiredService<IOrderSyncAdapter>();

            _logger.LogInformation("A reconciliar encomenda {OrderId} a partir da DLQ por ordem de criação.", item.Payload.OrderId);

            var result = await orderSyncAdapter.SyncOrderAsync(item.Payload, cancellationToken);

            if (result.IsSuccess)
            {
                _processedOrders.TryAdd(item.Payload.OrderId, 0);
                _logger.LogInformation("Reconciliacao concluida com sucesso para a encomenda {OrderId}. Ack da DLQ.", item.Payload.OrderId);
                await channel.BasicAckAsync(item.Delivery.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                continue;
            }

            _logger.LogError(
                "Falha persistente na reconciliacao da encomenda {OrderId}. Message: {Message}. Requeue na DLQ.",
                item.Payload.OrderId,
                result.Message);

            // Na DLQ usamos requeue para tentar mais tarde sem perder a mensagem.
            await channel.BasicNackAsync(item.Delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: cancellationToken);
        }
    }
}
