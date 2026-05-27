namespace VerdeMart.OrderSyncAdapter.Models;

public sealed class OrderSyncResult
{
    public bool IsSuccess { get; init; }

    public int? StatusCode { get; init; }

    public bool IsTimeout { get; init; }

    public bool IsStaleFallback { get; init; }

    public string Message { get; init; } = string.Empty;

    public static OrderSyncResult Success(int statusCode, string message = "Order synchronized successfully.") =>
        new()
        {
            IsSuccess = true,
            StatusCode = statusCode,
            Message = message
        };

    public static OrderSyncResult Failure(int? statusCode, string message, bool isTimeout = false) =>
        new()
        {
            IsSuccess = false,
            StatusCode = statusCode,
            IsTimeout = isTimeout,
            Message = message
        };

    public static OrderSyncResult StaleFallback(int? statusCode, string message) =>
        new()
        {
            IsSuccess = true,
            StatusCode = statusCode,
            IsStaleFallback = true,
            Message = message
        };
}
