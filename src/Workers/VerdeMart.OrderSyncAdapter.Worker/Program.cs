using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VerdeMart.OrderSyncAdapter;
using VerdeMart.OrderSyncAdapter.Worker;
using VerdeMart.OrderSyncAdapter.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

var erpUrl = builder.Configuration.GetValue<string>("Erp:BaseUrl")
    ?? throw new InvalidOperationException("A configuração Erp:BaseUrl é obrigatória.");

builder.Services.AddOrderSyncAdapter(erpUrl);
builder.Services.AddHostedService<RabbitMqOrderConsumerWorker>();
builder.Services.AddHostedService<ReconciliationService>();

var host = builder.Build();
await host.RunAsync();
