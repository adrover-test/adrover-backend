using Microsoft.EntityFrameworkCore;
using Adrover.Domain.Entities;
using Adrover.Data.Context;

namespace Adrover.Data.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByPhoneAsync(string phone);
    Task<IEnumerable<User>> GetByRoleAsync(UserRole role);
}

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AdRoverDbContext context) : base(context) { }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _dbSet.Include(u => u.Wallet)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetByPhoneAsync(string phone)
    {
        return await _dbSet.Include(u => u.Wallet)
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Phone == phone);
    }

    public async Task<IEnumerable<User>> GetByRoleAsync(UserRole role)
    {
        return await _dbSet.Where(u => u.Role == role).ToListAsync();
    }
}

public interface IAdvertisementRepository : IRepository<Advertisement>
{
    Task<Advertisement?> GetWithDetailsAsync(Guid id);
    Task<IEnumerable<Advertisement>> GetByClientAsync(Guid clientId);
    Task<IEnumerable<Advertisement>> GetByStatusAsync(AdvertisementStatus status);
    Task<IEnumerable<Advertisement>> GetPendingApprovalsAsync();
    Task<IEnumerable<Advertisement>> GetActiveAsync();
    Task<int> GetTotalPlayCountAsync(Guid advertisementId);
}

public class AdvertisementRepository : Repository<Advertisement>, IAdvertisementRepository
{
    public AdvertisementRepository(AdRoverDbContext context) : base(context) { }

    public async Task<Advertisement?> GetWithDetailsAsync(Guid id)
    {
        return await _dbSet.Include(a => a.Client)
            .Include(a => a.Payment)
            .Include(a => a.AdPlays)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<IEnumerable<Advertisement>> GetByClientAsync(Guid clientId)
    {
        return await _dbSet.Include(a => a.AdPlays)
            .Where(a => a.ClientId == clientId)
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Advertisement>> GetByStatusAsync(AdvertisementStatus status)
    {
        return await _dbSet.Include(a => a.Client)
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Advertisement>> GetPendingApprovalsAsync()
    {
        return await GetByStatusAsync(AdvertisementStatus.PendingApproval);
    }

    public async Task<IEnumerable<Advertisement>> GetActiveAsync()
    {
        var now = DateTime.UtcNow;
        return await _dbSet.Include(a => a.Client)
            .Where(a => a.Status == AdvertisementStatus.Active && 
                       a.StartDate <= now && 
                       a.EndDate >= now)
            .ToListAsync();
    }

    public async Task<int> GetTotalPlayCountAsync(Guid advertisementId)
    {
        return await _context.AdPlays.CountAsync(p => p.AdvertisementId == advertisementId);
    }
}

public interface IPaymentRepository : IRepository<Payment>
{
    Task<Payment?> GetByOrderIdAsync(string orderId);
    Task<Payment?> GetByPaymentIdAsync(string paymentId);
    Task<IEnumerable<Payment>> GetByUserAsync(Guid userId);
    Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatus status);
    Task<decimal> GetTotalRevenueAsync();
}

public class PaymentRepository : Repository<Payment>, IPaymentRepository
{
    public PaymentRepository(AdRoverDbContext context) : base(context) { }

    public async Task<Payment?> GetByOrderIdAsync(string orderId)
    {
        return await _dbSet.Include(p => p.User)
            .Include(p => p.Advertisement)
            .FirstOrDefaultAsync(p => p.OrderId == orderId);
    }

    public async Task<Payment?> GetByPaymentIdAsync(string paymentId)
    {
        return await _dbSet.Include(p => p.User)
            .Include(p => p.Advertisement)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    }

    public async Task<IEnumerable<Payment>> GetByUserAsync(Guid userId)
    {
        return await _dbSet.Include(p => p.Advertisement)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatus status)
    {
        return await _dbSet.Include(p => p.User)
            .Include(p => p.Advertisement)
            .Where(p => p.Status == status)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<decimal> GetTotalRevenueAsync()
    {
        return await _dbSet.Where(p => p.Status == PaymentStatus.Completed)
            .SumAsync(p => p.Amount);
    }
}

public interface IWalletRepository : IRepository<Wallet>
{
    Task<Wallet?> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<WalletTransaction>> GetTransactionsAsync(Guid walletId, int skip = 0, int take = 10);
}

public class WalletRepository : Repository<Wallet>, IWalletRepository
{
    public WalletRepository(AdRoverDbContext context) : base(context) { }

    public async Task<Wallet?> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet.Include(w => w.Transactions)
            .FirstOrDefaultAsync(w => w.UserId == userId);
    }

    public async Task<IEnumerable<WalletTransaction>> GetTransactionsAsync(Guid walletId, int skip = 0, int take = 10)
    {
        return await _context.WalletTransactions
            .Where(t => t.WalletId == walletId)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
}

public interface INotificationRepository : IRepository<Notification>
{
    Task<IEnumerable<Notification>> GetByUserAsync(Guid userId, int skip = 0, int take = 10);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task<bool> MarkAsReadAsync(Guid notificationId);
    Task<bool> MarkAllAsReadAsync(Guid userId);
}

public class NotificationRepository : Repository<Notification>, INotificationRepository
{
    public NotificationRepository(AdRoverDbContext context) : base(context) { }

    public async Task<IEnumerable<Notification>> GetByUserAsync(Guid userId, int skip = 0, int take = 10)
    {
        return await _dbSet.Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        return await _dbSet.CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId)
    {
        var notification = await GetByIdAsync(notificationId);
        if (notification == null)
            return false;

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await UpdateAsync(notification);
        return true;
    }

    public async Task<bool> MarkAllAsReadAsync(Guid userId)
    {
        var notifications = await FindAsync(n => n.UserId == userId && !n.IsRead);
        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            _dbSet.Update(notification);
        }
        await _context.SaveChangesAsync();
        return true;
    }
}
