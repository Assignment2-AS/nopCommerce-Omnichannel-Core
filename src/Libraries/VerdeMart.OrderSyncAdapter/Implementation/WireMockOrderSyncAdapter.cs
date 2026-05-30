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
    private readonly ILogger<WireMockOrderSyncAdapter> _logger;

    public WireMockOrderSyncAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<WireMockOrderSyncAdapter> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
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

        _logger.LogInformation("A iniciar sincronizacao da encomenda com ERP e WMS via WireMock.");

        var erpTask = SendToSystemAsync(ClientName, EndpointPath, "ERP", order.OrderId, json, cancellationToken);
        var wmsTask = SendToSystemAsync(WmsClientName, WmsEndpointPath, "WMS", order.OrderId, json, cancellationToken);

        var results = await Task.WhenAll(erpTask, wmsTask);

        var erpResult = results[0];
        var wmsResult = results[1];

        if (erpResult.IsSuccess && wmsResult.IsSuccess)
        {
            _logger.LogInformation("Encomenda sincronizada com sucesso no ERP e no WMS.");
            return OrderSyncResult.Success(200, "Order synchronized successfully in ERP and WMS.");
        }

        var statusCode = erpResult.StatusCode ?? wmsResult.StatusCode;
        var message = BuildFailureMessage(erpResult, wmsResult);
        var isTimeout = erpResult.IsTimeout || wmsResult.IsTimeout;

        _logger.LogError(
            "Sincronizacao incompleta. ERP sucesso: {ErpSuccess}; WMS sucesso: {WmsSuccess}.",
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

                _logger.LogInformation("Sincronizacao concluida com sucesso no {SystemName}. StatusCode: {StatusCode}", systemName, (int)response.StatusCode);
                return OrderSyncResult.Success((int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Falha de sincronizacao com {SystemName}. StatusCode: {StatusCode}; Body: {Body}",
                systemName,
                (int)response.StatusCode,
                body);

            return OrderSyncResult.Failure(
                (int)response.StatusCode,
                $"{systemName} returned a non-success status code: {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout ao comunicar com o {SystemName} simulado (WireMock).", systemName);
            return OrderSyncResult.Failure(
                null,
                $"Timeout while calling {systemName} endpoint.",
                isTimeout: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Erro de transporte HTTP ao comunicar com o {SystemName} simulado (WireMock).", systemName);
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
                "Circuit breaker aberto para WMS. A devolver conteudo stale em cache para OrderId {OrderId}.",
                orderId);

            return OrderSyncResult.StaleFallback(
                200,
                string.IsNullOrWhiteSpace(cachedBody)
                    ? $"WMS circuit open. Using stale cached response for OrderId {orderId}."
                    : $"WMS circuit open. Using stale cached response for OrderId {orderId}: {cachedBody}");
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
