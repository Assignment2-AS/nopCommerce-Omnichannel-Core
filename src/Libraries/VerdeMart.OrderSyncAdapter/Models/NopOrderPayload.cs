using System;
using System.Collections.Generic;

namespace VerdeMart.OrderSyncAdapter.Models;

public sealed class NopOrderPayload
{
    public int OrderId { get; init; }

    public int CustomerId { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public IReadOnlyCollection<NopOrderItemPayload> Items { get; init; } = Array.Empty<NopOrderItemPayload>();
}

public sealed class NopOrderItemPayload
{
    public string Sku { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }
}
