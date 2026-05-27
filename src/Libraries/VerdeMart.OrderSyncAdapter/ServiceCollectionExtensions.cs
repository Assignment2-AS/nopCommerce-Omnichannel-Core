using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using VerdeMart.OrderSyncAdapter.Implementation;

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

        services.AddHttpClient(WireMockClientName, client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            // Retry para suportar falhas transitórias sem expor a instabilidade ao domínio.
            .AddPolicyHandler(GetRetryPolicy())
            // Circuit Breaker para proteger o sistema quando o ERP estiver indisponível por vários erros seguidos.
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        services.AddHttpClient(WireMockWmsClientName, client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            // Mesma política de retry para o WMS, mantendo consistência de resiliência.
            .AddPolicyHandler(GetRetryPolicy())
            // O circuito separado evita que a falha do WMS degrade o ERP por arrastamento.
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        services.AddScoped<IOrderSyncAdapter, WireMockOrderSyncAdapter>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));
    }
}