using System.Text.Json;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Integration.OrderPublisher.Domain;
using Nop.Services.Catalog;
using Nop.Services.Events;
using Nop.Services.Orders;

namespace Nop.Plugin.Integration.OrderPublisher.Consumers;

/// <summary>
/// Handles the <see cref="OrderPlacedEvent"/> by writing a pending <see cref="OutboxMessage"/>
/// to the database immediately after the order is persisted.
///
/// <para>
/// <b>Outbox Pattern -> durability contract:</b><br/>
/// nopCommerce uses LinqToDB with per-operation connections (see <c>BaseDataProvider.InsertEntityAsync</c>).
/// There is no shared ambient transaction between the order INSERT and this Outbox INSERT.
/// The ordering guarantee is instead provided by the event pipeline itself:
/// <c>EventPublisher.PublishAsync</c> is called <em>after</em> <c>InsertOrderAsync</c> completes —
/// in <c>src/Libraries/Nop.Services/Orders/OrderProcessingService.cs</c>:
/// line 802 saves the order (<c>await _orderService.InsertOrderAsync(order)</c>)
/// and only line 1617 fires the event (<c>await _eventPublisher.PublishAsync(new OrderPlacedEvent(order))</c>).
/// By the time this consumer runs, the order row already exists in the database.
/// </para>
/// <para>
/// <b>Residual risk (documented in evidence/known-limitations.md):</b><br/>
/// If the process crashes between the order INSERT and this Outbox INSERT, the order exists
/// but no Outbox row is written. This window is small (microseconds, in-process) but non-zero.
/// A compensating background scan (not in scope for this demo) could close it.
/// </para>
/// </summary>
public class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    #region Fields

    private readonly IRepository<OutboxMessage> _outboxRepository;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;

    #endregion

    #region Ctor

    public OrderPlacedConsumer(
        IRepository<OutboxMessage> outboxRepository,
        IOrderService orderService,
        IProductService productService)
    {
        _outboxRepository = outboxRepository;
        _orderService = orderService;
        _productService = productService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handle the order placed event.
    /// Serialises the relevant order fields to JSON and inserts an <see cref="OutboxMessage"/>
    /// with <c>ProcessedOnUtc = null</c> (pending delivery to RabbitMQ).
    /// </summary>
    /// <param name="eventMessage">The event message containing the placed order.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task HandleEventAsync(OrderPlacedEvent eventMessage)
    {
        var order = eventMessage.Order;

        // Build the payload with the fields required by the downstream OrderSyncAdapter.
        // camelCase naming is intentional: it matches the contract established in the
        // feasibility spike (spike/rabbitmq-spike/Program.cs) and expected by OrderSyncAdapter.
        var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
        var itemPayloads = new List<object>();
        foreach (var item in orderItems)
        {
            var product = await _productService.GetProductByIdAsync(item.ProductId);
            if (product is null || string.IsNullOrWhiteSpace(product.Sku))
                continue;
            itemPayloads.Add(new
            {
                sku = product.Sku,
                quantity = item.Quantity,
                unitPrice = item.UnitPriceExclTax
            });
        }

        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            customerId = order.CustomerId,
            totalAmount = order.OrderTotal,
            currency = order.CustomerCurrencyCode,
            timestamp = order.CreatedOnUtc.ToString("o"),  // ISO 8601 -> unambiguous across timezones
            items = itemPayloads
        }, new JsonSerializerOptions { WriteIndented = false });

        var outboxMessage = new OutboxMessage
        {
            EventType = "order.placed",
            Payload = payload,
            CreatedOnUtc = DateTime.UtcNow,
            ProcessedOnUtc = null,
            CorrelationId = order.Id.ToString()
        };

        // publishEvent: false -> we do not want to fire an EntityInserted event for the outbox row
        // itself, which would cause unnecessary cache invalidation and could create feedback loops.
        await _outboxRepository.InsertAsync(outboxMessage, publishEvent: false);
    }

    #endregion
}
