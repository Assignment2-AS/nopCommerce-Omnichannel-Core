using VerdeMart.OrderSyncAdapter.Models;

namespace VerdeMart.OrderSyncAdapter.Infrastructure;

public interface IWmsStockPublisher
{
    Task PublishAsync(WmsStockPayload payload, CancellationToken cancellationToken = default);
}
