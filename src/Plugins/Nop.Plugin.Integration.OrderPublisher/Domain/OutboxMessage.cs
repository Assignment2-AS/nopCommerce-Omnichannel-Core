using Nop.Core;

namespace Nop.Plugin.Integration.OrderPublisher.Domain;

public class OutboxMessage : BaseEntity
{
    /// <summary>
    /// Event type identifier, e.g. "order.placed"
    /// </summary>
    public string EventType { get; set; }

    /// <summary>
    /// JSON-serialised event payload
    /// </summary>
    public string Payload { get; set; }

    /// <summary>
    /// UTC timestamp when the message was written to the Outbox
    /// </summary>
    public DateTime CreatedOnUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the message was successfully published to RabbitMQ.
    /// Null means the message is still pending delivery.
    /// </summary>
    public DateTime? ProcessedOnUtc { get; set; }

    /// <summary>
    /// Stable idempotency key — set to OrderId so the consumer can detect duplicates
    /// </summary>
    public string CorrelationId { get; set; }
}
