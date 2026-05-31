namespace VerdeMart.OrderSyncAdapter.Models;

public sealed class WmsStockPayload
{
    public string Sku { get; init; } = string.Empty;

    /// <summary>
    /// Relative quantity change to apply to nopCommerce stock.
    /// Negative = stock consumed (order placed), positive = stock returned/restocked.
    /// </summary>
    public int QuantityDelta { get; init; }
}
