using Adrover.Application.DTOs;

namespace Adrover.Application.Services;

/// <summary>
/// Service interface for wallet operations
/// </summary>
public interface IWalletService
{
    /// <summary>
    /// Get wallet balance for a user
    /// </summary>
    Task<WalletDto> GetWalletAsync(Guid userId);
    
    /// <summary>
    /// Topup wallet with amount
    /// </summary>
    Task<WalletTopupResponse> TopupWalletAsync(Guid userId, WalletTopupRequest request);
    
    /// <summary>
    /// Get wallet transaction history with pagination
    /// </summary>
    Task<WalletTransactionHistoryResponse> GetTransactionHistoryAsync(Guid userId, int skip = 0, int take = 10);
    
    /// <summary>
    /// Debit amount from wallet for advertisement payment
    /// </summary>
    Task<bool> DebitWalletAsync(Guid userId, decimal amount, string description, string? referenceId = null);
    
    /// <summary>
    /// Credit amount to wallet for refund
    /// </summary>
    Task<bool> CreditWalletAsync(Guid userId, decimal amount, string description, string? referenceId = null);
}
