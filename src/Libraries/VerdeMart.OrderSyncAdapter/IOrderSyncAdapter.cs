using System.Threading;
using System.Threading.Tasks;
using VerdeMart.OrderSyncAdapter.Models;

namespace VerdeMart.OrderSyncAdapter;

public interface IOrderSyncAdapter
{
    // Contrato explicito do Adapter Pattern para desacoplar nopCommerce do ERP.
    Task<OrderSyncResult> SyncOrderAsync(NopOrderPayload order, CancellationToken cancellationToken = default);
}
