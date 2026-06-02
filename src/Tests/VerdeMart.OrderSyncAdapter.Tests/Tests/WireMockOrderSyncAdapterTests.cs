using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using VerdeMart.OrderSyncAdapter.Implementation;
using VerdeMart.OrderSyncAdapter.Models;
using Xunit;

namespace VerdeMart.OrderSyncAdapter.Tests;

public sealed class WireMockOrderSyncAdapterTests
{
    [Fact]
    public async Task SyncOrderAsync_ShouldReturnSuccess_WhenErpReturns200()
    {
        // Arrange
        var httpClientFactory = BuildFactoryReturning(HttpStatusCode.OK);
        var loggerMock = new Mock<ILogger<WireMockOrderSyncAdapter>>();
        var sut = new WireMockOrderSyncAdapter(httpClientFactory.Object, loggerMock.Object);

        var order = BuildOrder();

        // Act
        var result = await sut.SyncOrderAsync(order);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.IsTimeout.Should().BeFalse();
    }

    [Fact]
    public async Task SyncOrderAsync_ShouldReturnFailure_WhenErpReturns500()
    {
        // Arrange
        var httpClientFactory = BuildFactoryReturning(HttpStatusCode.InternalServerError, "Internal error");
        var loggerMock = new Mock<ILogger<WireMockOrderSyncAdapter>>();
        var sut = new WireMockOrderSyncAdapter(httpClientFactory.Object, loggerMock.Object);

        var order = BuildOrder();

        // Act
        var result = await sut.SyncOrderAsync(order);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.IsTimeout.Should().BeFalse();
        result.Message.Should().Contain("non-success status code");
    }

    [Fact]
    public async Task SyncOrderAsync_ShouldReturnTimeoutFailure_WhenRequestTimesOut()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException("Simulated timeout"));

        var client = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8080/")
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(client);

        var loggerMock = new Mock<ILogger<WireMockOrderSyncAdapter>>();
        var sut = new WireMockOrderSyncAdapter(factoryMock.Object, loggerMock.Object);

        var order = BuildOrder();

        // Act
        var result = await sut.SyncOrderAsync(order);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().BeNull();
        result.IsTimeout.Should().BeTrue();
        result.Message.Should().Contain("Timeout");
    }

    private static Mock<IHttpClientFactory> BuildFactoryReturning(HttpStatusCode statusCode, string body = "")
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(body)
            });

        var client = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8080/")
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(client);

        return factoryMock;
    }

    private static NopOrderPayload BuildOrder() =>
        new()
        {
            OrderId = 123,
            CustomerId = 456,
            CurrencyCode = "EUR",
            TotalAmount = 199.99m,
            CreatedAtUtc = DateTime.UtcNow,
            Items = new[]
            {
                new NopOrderItemPayload
                {
                    Sku = "SKU-1",
                    Quantity = 2,
                    UnitPrice = 99.995m
                }
            }
        };
}
