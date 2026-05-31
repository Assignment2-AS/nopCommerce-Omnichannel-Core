using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using VerdeMart.OrderSyncAdapter.Infrastructure;
using VerdeMart.OrderSyncAdapter.Models;

namespace VerdeMart.OrderSyncAdapter.Implementation;

public sealed class WireMockOrderSyncAdapter : IOrderSyncAdapter
{
    private const string ClientName = "WireMockErp";
    private const string WmsClientName = "WireMockWms";
    private const string EndpointPath = "api/orders";
    private const string WmsEndpointPath = "api/wms/orders";
    private static readonly ConcurrentDictionary<int, string> WmsResponseCache = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWmsStockPublisher _stockPublisher;
    private readonly ILogger<WireMockOrderSyncAdapter> _logger;

    public WireMockOrderSyncAdapter(
        IHttpClientFactory httpClientFactory,
        IWmsStockPublisher stockPublisher,
        ILogger<WireMockOrderSyncAdapter> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _stockPublisher = stockPublisher ?? throw new ArgumentNullException(nameof(stockPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OrderSyncResult> SyncOrderAsync(NopOrderPayload order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Scope com OrderId para facilitar tracing distribuido e correlacao em logs.
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["OrderId"] = order.OrderId
        });

        var json = JsonSerializer.Serialize(order);

        _logger.LogInformation("Starting order sync with ERP and WMS via WireMock.");

        var erpTask = SendToSystemAsync(ClientName, EndpointPath, "ERP", order.OrderId, json, cancellationToken);
        var wmsTask = SendToSystemAsync(WmsClientName, WmsEndpointPath, "WMS", order.OrderId, json, cancellationToken);

        var results = await Task.WhenAll(erpTask, wmsTask);

        var erpResult = results[0];
        var wmsResult = results[1];

        if (erpResult.IsSuccess && wmsResult.IsSuccess)
        {
            _logger.LogInformation("Order synced successfully in ERP and WMS.");
            return OrderSyncResult.Success(200, "Order synchronized successfully in ERP and WMS.");
        }

        var statusCode = erpResult.StatusCode ?? wmsResult.StatusCode;
        var message = BuildFailureMessage(erpResult, wmsResult);
        var isTimeout = erpResult.IsTimeout || wmsResult.IsTimeout;

        _logger.LogError(
            "Sync incomplete. ERP success: {ErpSuccess}; WMS success: {WmsSuccess}.",
            erpResult.IsSuccess,
            wmsResult.IsSuccess);

        return OrderSyncResult.Failure(statusCode, message, isTimeout);
    }

    private async Task<OrderSyncResult> SendToSystemAsync(
        string clientName,
        string endpointPath,
        string systemName,
        int orderId,
        string json,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        try
        {
            // O adapter isola o detalhe do canal HTTP e devolve um resultado explicito ao dominio.
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                if (systemName == "WMS")
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    WmsResponseCache[orderId] = responseBody;
                }

                _logger.LogInformation(
                    "Sync successful on {SystemName}. StatusCode: {StatusCode}",
                    systemName, (int)response.StatusCode);
                return OrderSyncResult.Success((int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Sync failed on {SystemName}. StatusCode: {StatusCode}; Body: {Body}",
                systemName,
                (int)response.StatusCode,
                body);

            return OrderSyncResult.Failure(
                (int)response.StatusCode,
                $"{systemName} returned a non-success status code: {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout communicating with simulated {SystemName} (WireMock).", systemName);
            return OrderSyncResult.Failure(
                null,
                $"Timeout while calling {systemName} endpoint.",
                isTimeout: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP transport error communicating with simulated {SystemName} (WireMock).", systemName);
            return OrderSyncResult.Failure(
                null,
                $"HTTP error while calling {systemName} endpoint.");
        }
        catch (BrokenCircuitException)
        {
            if (systemName != "WMS")
            {
                throw;
            }

            WmsResponseCache.TryGetValue(orderId, out var cachedBody);

            _logger.LogWarning(
                "WMS circuit breaker open. Returning stale cached response for OrderId {OrderId}.",
                orderId);

            return OrderSyncResult.StaleFallback(
                200,
                string.IsNullOrWhiteSpace(cachedBody)
                    ? $"WMS circuit open. Using stale cached response for OrderId {orderId}."
                    : $"WMS circuit open. Using stale cached response for OrderId {orderId}: {cachedBody}");
        }
    }

    private async Task PublishStockUpdatesAsync(NopOrderPayload order, CancellationToken cancellationToken)
    {
        foreach (var item in order.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
                continue;

            try
            {
                await _stockPublisher.PublishAsync(
                    new WmsStockPayload { Sku = item.Sku, QuantityDelta = -item.Quantity },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Stock publish failure must not roll back a successful order sync.
                _logger.LogWarning(ex,
                    "[WmsStockPublisher] Failed to publish stock update for SKU={Sku}. Continuing.",
                    item.Sku);
            }
        }
    }

    private static string BuildFailureMessage(OrderSyncResult erpResult, OrderSyncResult wmsResult)
    {
        var failures = new List<string>();

        if (!erpResult.IsSuccess)
        {
            failures.Add($"ERP: {erpResult.Message}");
        }

        if (!wmsResult.IsSuccess)
        {
            failures.Add($"WMS: {wmsResult.Message}");
        }

        return string.Join(" | ", failures);
    }
}
