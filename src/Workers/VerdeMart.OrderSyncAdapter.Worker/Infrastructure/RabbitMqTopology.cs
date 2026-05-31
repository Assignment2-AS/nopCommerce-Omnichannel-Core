using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace VerdeMart.OrderSyncAdapter.Worker.Infrastructure;

public static class RabbitMqTopology
{
    public const string OrderQueueName = "order.placed";
    public const string DeadLetterExchangeName = "order.placed.dlx";
    public const string DeadLetterQueueName = "order.placed.dlq";
    public const string DeadLetterRoutingKey = DeadLetterQueueName;

    public static async Task EnsureAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // DLQ receives messages rejected from the main queue for later reconciliation.
        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: DeadLetterQueueName,
            exchange: DeadLetterExchangeName,
            routingKey: DeadLetterRoutingKey,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
        };

        await channel.QueueDeclareAsync(
            queue: OrderQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken);
    }
}