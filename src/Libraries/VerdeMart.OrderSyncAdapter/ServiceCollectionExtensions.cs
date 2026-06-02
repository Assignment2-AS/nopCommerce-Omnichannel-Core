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
            // Retry to handle transient failures without exposing instability to the domain.
            .AddPolicyHandler(GetRetryPolicy(retryIntervals))
            // Circuit breaker to protect the system when ERP is unavailable across multiple consecutive errors.
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        services.AddHttpClient(WireMockWmsClientName, client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            // Same retry policy for WMS, keeping resilience consistent across both clients.
            .AddPolicyHandler(GetRetryPolicy(retryIntervals))
            // Separate circuit prevents WMS failures from cascading into ERP degradation.
            .AddPolicyHandler(GetCircuitBreakerPolicy(wmsCircuitBreakerStateTracker));

        services.AddScoped<IOrderSyncAdapter, WireMockOrderSyncAdapter>();
        services.AddSingleton<IWmsStockPublisher, Infrastructure.NullWmsStockPublisher>();

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(TimeSpan[] retryIntervals)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(retryIntervals);
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(WmsCircuitBreakerStateTracker stateTracker)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (_, _, _) => stateTracker.MarkOpened(),
                onReset: _ => stateTracker.MarkClosed(),
                onHalfOpen: () => stateTracker.MarkHalfOpen());
    }
}