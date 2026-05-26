using Nop.Data.Mapping;
using Nop.Plugin.Integration.OrderPublisher.Domain;

namespace Nop.Plugin.Integration.OrderPublisher.Data.Mapping;

public class OrderPublisherNameCompatibility : INameCompatibility
{
    public Dictionary<Type, string> TableNames => new()
    {
        { typeof(OutboxMessage), "Integration_OutboxMessage" }
    };

    public Dictionary<(Type, string), string> ColumnName => [];
}
