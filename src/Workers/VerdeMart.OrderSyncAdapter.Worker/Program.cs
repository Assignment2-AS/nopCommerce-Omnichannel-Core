using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VerdeMart.OrderSyncAdapter;
using VerdeMart.OrderSyncAdapter.Worker;

var builder = Host.CreateApplicationBuilder(args);

var erpUrl = builder.Configuration.GetValue<string>("Erp:BaseUrl")
    ?? throw new InvalidOperationException("A configuração Erp:BaseUrl é obrigatória.");

builder.Services.AddOrderSyncAdapter(erpUrl);
builder.Services.AddHostedService<RabbitMqOrderConsumerWorker>();

var host = builder.Build();
await host.RunAsync();
