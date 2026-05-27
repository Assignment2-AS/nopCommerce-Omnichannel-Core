using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using VerdeMart.OrderSyncAdapter.Implementation;
using VerdeMart.OrderSyncAdapter.Infrastructure;

namespace VerdeMart.OrderSyncAdapter;

public static class ServiceCollectionExtensions
{
    private const string WireMockClientName = "WireMockErp";
    private const string WireMockWmsClientName = "WireMockWms";

    public static IServiceCollection AddOrderSyncAdapter(this IServiceCollection services, string erpBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(erpBaseUrl))
        {
            throw new ArgumentException("ERP base URL must be provided.", nameof(erpBaseUrl));
        }

        var baseUri = new Uri(erpBaseUrl, UriKind.Absolute);
        var retryIntervals = new[]
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4)
        };
        var wmsCircuitBreakerStateTracker = new WmsCircuitBreakerStateTracker();
        services.AddSingleton(wmsCircuitBreakerStateTracker);

        services.AddHttpClient(WireMockClientName, client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            // Retry para suportar falhas transitórias sem expor a instabilidade ao domínio.
            .AddPolicyHandler(GetRetryPolicy(retryIntervals))
            // Circuit Breaker para proteger o sistema quando o ERP estiver indisponível por vários erros seguidos.
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        services.AddHttpClient(WireMockWmsClientName, client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            // Mesma política de retry para o WMS, mantendo consistência de resiliência.
            .AddPolicyHandler(GetRetryPolicy(retryIntervals))
            // O circuito separado evita que a falha do WMS degrade o ERP por arrastamento.
            .AddPolicyHandler(GetCircuitBreakerPolicy(wmsCircuitBreakerStateTracker));

        services.AddScoped<IOrderSyncAdapter, WireMockOrderSyncAdapter>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(TimeSpan[] retryIntervals)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(retryIntervals);
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(WmsCircuitBreakerStateTracker stateTracker)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (_, _, _) => stateTracker.MarkOpened(),
                onReset: _ => stateTracker.MarkClosed(),
                onHalfOpen: () => stateTracker.MarkHalfOpen());
    }
}