using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VerdeMart.OrderSyncAdapter.Models;

namespace VerdeMart.OrderSyncAdapter.Implementation;

public sealed class WireMockOrderSyncAdapter : IOrderSyncAdapter
{
    private const string ClientName = "WireMockErp";
    private const string EndpointPath = "api/orders";

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

        _logger.LogInformation("A iniciar sincronizacao da encomenda com ERP via WireMock.");

        var client = _httpClientFactory.CreateClient(ClientName);
        var json = JsonSerializer.Serialize(order);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        try
        {
            // O adapter isola o detalhe do canal HTTP e devolve um resultado explicito ao dominio.
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Sincronizacao concluida com sucesso. StatusCode: {StatusCode}", (int)response.StatusCode);
                return OrderSyncResult.Success((int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Falha de sincronizacao com ERP. StatusCode: {StatusCode}; Body: {Body}",
                (int)response.StatusCode,
                body);

            return OrderSyncResult.Failure(
                (int)response.StatusCode,
                $"ERP returned a non-success status code: {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout ao comunicar com o ERP simulado (WireMock).");
            return OrderSyncResult.Failure(
                null,
                "Timeout while calling ERP endpoint.",
                isTimeout: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Erro de transporte HTTP ao comunicar com o ERP simulado (WireMock).");
            return OrderSyncResult.Failure(
                null,
                "HTTP error while calling ERP endpoint.");
        }
    }
}
