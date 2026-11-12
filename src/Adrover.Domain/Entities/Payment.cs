namespace Adrover.Domain.Entities;

/// <summary>
/// Payment status enumeration
/// </summary>
public enum PaymentStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Refunded = 5
}

/// <summary>
/// Represents a payment transaction
/// </summary>
public class Payment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? AdvertisementId { get; set; }
    
    public string OrderId { get; set; } = string.Empty;
    public string? PaymentId { get; set; }
    public string? Signature { get; set; }
    
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    
    public string PaymentMethod { get; set; } = string.Empty;
    public string? CardLastFour { get; set; }
    public string? CardIssuer { get; set; }
    
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    
    // Navigation properties
    public virtual User? User { get; set; }
    public virtual Advertisement? Advertisement { get; set; }
}

/// <summary>
/// User wallet for balance management
/// </summary>
public class Wallet
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public string Currency { get; set; } = "INR";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    
    // Navigation properties
    public virtual User? User { get; set; }
    public virtual ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
}

/// <summary>
/// Wallet transaction history
/// </summary>
public class WalletTransaction
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string TransactionType { get; set; } = string.Empty; // Credit, Debit
    public string Description { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual Wallet? Wallet { get; set; }
}
