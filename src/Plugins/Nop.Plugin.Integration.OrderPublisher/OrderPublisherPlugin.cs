using Nop.Services.Plugins;

namespace Nop.Plugin.Integration.OrderPublisher;

public class OrderPublisherPlugin : BasePlugin
{
    public override async Task InstallAsync()
    {
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        await base.UninstallAsync();
    }
}
