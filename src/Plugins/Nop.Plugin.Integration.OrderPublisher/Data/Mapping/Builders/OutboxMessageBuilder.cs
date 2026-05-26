using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Integration.OrderPublisher.Domain;

namespace Nop.Plugin.Integration.OrderPublisher.Data.Mapping.Builders;

public class OutboxMessageBuilder : NopEntityBuilder<OutboxMessage>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(OutboxMessage.EventType)).AsString(100).NotNullable()
            .WithColumn(nameof(OutboxMessage.Payload)).AsString(int.MaxValue).NotNullable()
            .WithColumn(nameof(OutboxMessage.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(OutboxMessage.ProcessedOnUtc)).AsDateTime2().Nullable()
            .WithColumn(nameof(OutboxMessage.CorrelationId)).AsString(100).NotNullable();
    }
}
