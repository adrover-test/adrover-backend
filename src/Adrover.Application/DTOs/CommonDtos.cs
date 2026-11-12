namespace Adrover.Application.DTOs;

public class WalletDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public string Currency { get; set; } = "INR";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class WalletTopupRequest
{
    public decimal Amount { get; set; }
    public string PaymentId { get; set; } = string.Empty;
}

public class WalletTopupResponse
{
    public Guid TransactionId { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal NewBalance { get; set; }
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
}

public class WalletTransactionDto
{
    public Guid TransactionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class WalletTransactionHistoryResponse
{
    public List<WalletTransactionDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class NotificationDto
{
    public Guid NotificationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public Guid? RelatedId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class NotificationListResponse
{
    public List<NotificationDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Unread { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ApiErrorResponse
{
    public bool Success { get; set; } = false;
    public ErrorDetails Error { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ErrorDetails
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]>? Details { get; set; }
}

public class PaginationRequest
{
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 10;

    public void Validate()
    {
        if (Skip < 0) Skip = 0;
        if (Take <= 0) Take = 10;
        if (Take > 100) Take = 100;
    }
}

public class FileUploadResponse
{
    public string Url { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
    public int? Duration { get; set; }
}
