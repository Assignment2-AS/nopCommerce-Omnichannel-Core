using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VerdeMart.OrderSyncAdapter;
using VerdeMart.OrderSyncAdapter.Infrastructure;
using VerdeMart.OrderSyncAdapter.Worker;
using VerdeMart.OrderSyncAdapter.Worker.Infrastructure;
using VerdeMart.OrderSyncAdapter.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

var erpUrl = builder.Configuration.GetValue<string>("Erp:BaseUrl")
    ?? throw new InvalidOperationException("Erp:BaseUrl configuration is required.");

builder.Services.AddOrderSyncAdapter(erpUrl);

// Override the null publisher registered by AddOrderSyncAdapter with the real RabbitMQ implementation.
builder.Services.AddSingleton<IWmsStockPublisher>(provider =>
    new RabbitMqWmsStockPublisher(
        builder.Configuration,
        provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RabbitMqWmsStockPublisher>>()));

builder.Services.AddHostedService<RabbitMqOrderConsumerWorker>();
builder.Services.AddHostedService<ReconciliationService>();

var host = builder.Build();
await host.RunAsync();
