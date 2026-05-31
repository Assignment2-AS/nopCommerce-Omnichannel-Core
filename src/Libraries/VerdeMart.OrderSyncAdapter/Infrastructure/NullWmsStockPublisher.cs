using VerdeMart.OrderSyncAdapter.Models;

namespace VerdeMart.OrderSyncAdapter.Infrastructure;

/// <summary>
/// No-op implementation used in test contexts where RabbitMQ is not available.
/// Replaced by <c>RabbitMqWmsStockPublisher</c> in the worker host.
/// </summary>
public sealed class NullWmsStockPublisher : IWmsStockPublisher
{
    public Task PublishAsync(WmsStockPayload payload, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
