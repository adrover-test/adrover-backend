using Adrover.Application.DTOs;
using Adrover.Application.Exceptions;
using Adrover.Data.Repositories;
using Adrover.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Adrover.Application.Services;

/// <summary>
/// Service for wallet operations
/// </summary>
public class WalletService : IWalletService
{
    private readonly IRepository<Wallet> _walletRepository;
    private readonly IRepository<WalletTransaction> _transactionRepository;
    private readonly IRepository<User> _userRepository;
    private readonly ILogger<WalletService> _logger;

    public WalletService(
        IRepository<Wallet> walletRepository,
        IRepository<WalletTransaction> transactionRepository,
        IRepository<User> userRepository,
        ILogger<WalletService> logger)
    {
        _walletRepository = walletRepository;
        _transactionRepository = transactionRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<WalletDto> GetWalletAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new NotFoundException("User not found");
        }

        var wallet = await _walletRepository.FirstOrDefaultAsync(w => w.UserId == userId);
        
        if (wallet == null)
        {
            wallet = new Wallet
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Balance = 0,
                Currency = "INR",
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            
            await _walletRepository.AddAsync(wallet);
            _logger.LogInformation("Wallet created for user: {UserId}", userId);
        }

        return MapToWalletDto(wallet);
    }

    public async Task<WalletTopupResponse> TopupWalletAsync(Guid userId, WalletTopupRequest request)
    {
        if (request.Amount <= 0)
        {
            _logger.LogWarning("Invalid topup amount: {Amount}", request.Amount);
            throw new ValidationException("Topup amount must be greater than 0");
        }

        var wallet = await _walletRepository.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
        {
            _logger.LogWarning("Wallet not found for user: {UserId}", userId);
            throw new NotFoundException("Wallet not found");
        }

        var previousBalance = wallet.Balance;
        wallet.Balance += request.Amount;
        wallet.LastUpdated = DateTime.UtcNow;

        await _walletRepository.UpdateAsync(wallet);

        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = "Topup",
            Amount = request.Amount,
            BalanceBefore = previousBalance,
            BalanceAfter = wallet.Balance,
            Description = $"Wallet topup - {request.Amount} INR",
            ReferenceId = request.PaymentId,
            CreatedAt = DateTime.UtcNow
        };

        await _transactionRepository.AddAsync(transaction);

        _logger.LogInformation("Wallet topup successful for user {UserId}: {Amount} INR", userId, request.Amount);

        return new WalletTopupResponse
        {
            TransactionId = transaction.Id,
            PreviousBalance = previousBalance,
            NewBalance = wallet.Balance,
            Amount = request.Amount,
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<WalletTransactionHistoryResponse> GetTransactionHistoryAsync(Guid userId, int skip = 0, int take = 10)
    {
        var wallet = await _walletRepository.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
        {
            _logger.LogWarning("Wallet not found for user: {UserId}", userId);
            throw new NotFoundException("Wallet not found");
        }

        // Validate pagination
        if (skip < 0) skip = 0;
        if (take <= 0) take = 10;
        if (take > 100) take = 100;

        var transactions = await _transactionRepository.FindAsync(t => t.WalletId == wallet.Id);
        
        var total = transactions.Count();
        var items = transactions
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(MapToTransactionDto)
            .ToList();

        _logger.LogInformation("Retrieved wallet transaction history for user {UserId}: {Count} items", userId, items.Count);

        return new WalletTransactionHistoryResponse
        {
            Items = items,
            Total = total,
            Skip = skip,
            Take = take
        };
    }

    public async Task<bool> DebitWalletAsync(Guid userId, decimal amount, string description, string? referenceId = null)
    {
        if (amount <= 0)
        {
            _logger.LogWarning("Invalid debit amount: {Amount}", amount);
            throw new ValidationException("Debit amount must be greater than 0");
        }

        var wallet = await _walletRepository.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
        {
            _logger.LogWarning("Wallet not found for user: {UserId}", userId);
            throw new NotFoundException("Wallet not found");
        }

        if (wallet.Balance < amount)
        {
            _logger.LogWarning("Insufficient balance for user {UserId}: Available={Available}, Required={Required}", 
                userId, wallet.Balance, amount);
            throw new ValidationException("Insufficient wallet balance");
        }

        var previousBalance = wallet.Balance;
        wallet.Balance -= amount;
        wallet.LastUpdated = DateTime.UtcNow;

        await _walletRepository.UpdateAsync(wallet);

        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = "Debit",
            Amount = amount,
            BalanceBefore = previousBalance,
            BalanceAfter = wallet.Balance,
            Description = description,
            ReferenceId = referenceId,
            CreatedAt = DateTime.UtcNow
        };

        await _transactionRepository.AddAsync(transaction);

        _logger.LogInformation("Wallet debit successful for user {UserId}: {Amount} INR", userId, amount);

        return true;
    }

    public async Task<bool> CreditWalletAsync(Guid userId, decimal amount, string description, string? referenceId = null)
    {
        if (amount <= 0)
        {
            _logger.LogWarning("Invalid credit amount: {Amount}", amount);
            throw new ValidationException("Credit amount must be greater than 0");
        }

        var wallet = await _walletRepository.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet == null)
        {
            _logger.LogWarning("Wallet not found for user: {UserId}", userId);
            throw new NotFoundException("Wallet not found");
        }

        var previousBalance = wallet.Balance;
        wallet.Balance += amount;
        wallet.LastUpdated = DateTime.UtcNow;

        await _walletRepository.UpdateAsync(wallet);

        var transaction = new WalletTransaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            TransactionType = "Credit",
            Amount = amount,
            BalanceBefore = previousBalance,
            BalanceAfter = wallet.Balance,
            Description = description,
            ReferenceId = referenceId,
            CreatedAt = DateTime.UtcNow
        };

        await _transactionRepository.AddAsync(transaction);

        _logger.LogInformation("Wallet credit successful for user {UserId}: {Amount} INR", userId, amount);

        return true;
    }

    private static WalletDto MapToWalletDto(Wallet wallet)
    {
        return new WalletDto
        {
            Id = wallet.Id,
            UserId = wallet.UserId,
            Balance = wallet.Balance,
            Currency = wallet.Currency,
            CreatedAt = wallet.CreatedAt,
            LastUpdated = wallet.LastUpdated
        };
    }

    private static WalletTransactionDto MapToTransactionDto(WalletTransaction transaction)
    {
        return new WalletTransactionDto
        {
            TransactionId = transaction.Id,
            Type = transaction.TransactionType,
            Amount = transaction.Amount,
            BalanceBefore = transaction.BalanceBefore,
            BalanceAfter = transaction.BalanceAfter,
            Description = transaction.Description,
            Timestamp = transaction.CreatedAt
        };
    }
}
