using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Plugin.Integration.OrderPublisher.Services;

namespace Nop.Plugin.Integration.OrderPublisher.Infrastructure;

/// <summary>
/// Registers the plugin's services in the ASP.NET Core DI container.
///
/// <para>
/// The <see cref="OutboxPublisherService"/> is registered as a hosted service
/// (<c>AddHostedService</c>) so ASP.NET Core starts and stops it automatically
/// with the application lifecycle.
/// </para>
/// <para>
/// RabbitMQ connection settings are read from <c>appsettings.json</c> under the
/// <c>RabbitMQ</c> section (see README.md for configuration instructions).
/// </para>
/// </summary>
public class NopStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    /// <param name="services">Collection of service descriptors</param>
    /// <param name="configuration">Configuration of the application</param>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        // Register the Outbox drain loop as a hosted background service.
        // IRepository<OutboxMessage> is Scoped -> the service creates its own scope
        // per polling iteration via IServiceScopeFactory (injected by the runtime).
        services.AddHostedService(provider =>
            new OutboxPublisherService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILogger<OutboxPublisherService>>(),
                configuration));

        // Register the WMS stock sync consumer as a hosted background service.
        // Listens on wms.stock.update and applies absolute stock levels to nopCommerce products.
        services.AddHostedService(provider =>
            new WmsStockSyncService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILogger<WmsStockSyncService>>(),
                configuration));
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    /// <param name="application">Builder for configuring an application's request pipeline</param>
    public void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Gets order of this startup configuration implementation.
    /// 3000 matches other integration plugins -> runs after core nopCommerce services.
    /// </summary>
    public int Order => 3000;
}
