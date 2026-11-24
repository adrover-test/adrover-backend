using Microsoft.EntityFrameworkCore;
using Adrover.Domain.Entities;

namespace Adrover.Data.Context;

/// <summary>
/// Main database context for Adrover application
/// </summary>
public class AdRoverDbContext : DbContext
{
    public AdRoverDbContext(DbContextOptions<AdRoverDbContext> options) : base(options) { }

    // DbSets
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Advertisement> Advertisements { get; set; } = null!;
    public DbSet<AdPlay> AdPlays { get; set; } = null!;
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<Wallet> Wallets { get; set; } = null!;
    public DbSet<WalletTransaction> WalletTransactions { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<Enquiry> Enquiries { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<Driver> Drivers { get; set; } = null!;
    public DbSet<DriverAssignment> DriverAssignments { get; set; } = null!;
    public DbSet<DriverPlayback> DriverPlaybacks { get; set; } = null!;
    public DbSet<DriverPayout> DriverPayouts { get; set; } = null!;
    public DbSet<DriverNotification> DriverNotifications { get; set; } = null!;
    public DbSet<DriverIssue> DriverIssues { get; set; } = null!;
    public DbSet<DriverIssueHistory> DriverIssueHistory { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Phone).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(20).IsRequired();
            entity.Property(e => e.FullName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.ProfileImageUrl).HasMaxLength(500);
            
            entity.HasMany(e => e.RefreshTokens)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.Advertisements)
                .WithOne(e => e.Client)
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasMany(e => e.Payments)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.Wallet)
                .WithOne(e => e.User)
                .HasForeignKey<Wallet>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.Notifications)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.AuditLogs)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.Property(e => e.Token).HasMaxLength(500).IsRequired();
        });

        // Advertisement configuration
        modelBuilder.Entity<Advertisement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClientId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SubmittedAt);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Brand).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.TargetArea).HasMaxLength(500).IsRequired();
            entity.Property(e => e.VideoUrl).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.ApprovedBy).HasMaxLength(255);
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);
            
            entity.HasOne(e => e.Payment)
                .WithOne(e => e.Advertisement)
                .HasForeignKey<Payment>(e => e.AdvertisementId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.HasMany(e => e.AdPlays)
                .WithOne(e => e.Advertisement)
                .HasForeignKey(e => e.AdvertisementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AdPlay configuration
        modelBuilder.Entity<AdPlay>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AdvertisementId);
            entity.HasIndex(e => e.PlayedAt);
            entity.Property(e => e.DriverPhone).HasMaxLength(20).IsRequired();
            entity.Property(e => e.VehicleNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.TotalPlays).HasDefaultValue(1);
        });

        // Payment configuration
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OrderId).IsUnique();
            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.OrderId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PaymentId).HasMaxLength(100);
            entity.Property(e => e.Signature).HasMaxLength(500);
            entity.Property(e => e.Currency).HasMaxLength(10).IsRequired();
            entity.Property(e => e.PaymentMethod).HasMaxLength(50).IsRequired();
            entity.Property(e => e.CardLastFour).HasMaxLength(4);
            entity.Property(e => e.CardIssuer).HasMaxLength(50);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Metadata)
                .HasColumnType("nvarchar(max)")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>()
                );
        });

        // Wallet configuration
        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.Currency).HasMaxLength(10).IsRequired();
            
            entity.HasMany(e => e.Transactions)
                .WithOne(e => e.Wallet)
                .HasForeignKey(e => e.WalletId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WalletTransaction configuration
        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.WalletId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.TransactionType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.ReferenceId).HasMaxLength(100);
        });

        // Notification configuration
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.IsRead);
            entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Message).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(100).IsRequired();
        });

        // Enquiry configuration
        modelBuilder.Entity<Enquiry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Phone);
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Phone).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(255).IsRequired();
            entity.Property(e => e.SourceChannel).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
        });

        // Driver configuration
        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Phone).IsUnique();
            entity.HasIndex(e => e.VehicleNumber).IsUnique();
            entity.HasIndex(e => e.ScreenId).IsUnique();
            entity.Property(e => e.FullName).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.VehicleNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.VehicleModel).HasMaxLength(120).IsRequired();
            entity.Property(e => e.ServiceArea).HasMaxLength(120).IsRequired();
            entity.Property(e => e.ScreenId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(255);
            entity.Property(e => e.Address).HasMaxLength(300);
            entity.Property(e => e.PreferredLanguage).HasMaxLength(2);
            entity.Property(e => e.PhotoBlobName).HasMaxLength(500);
        });

        modelBuilder.Entity<DriverAssignment>(entity =>
        {
            entity.HasIndex(e => new { e.DriverId, e.AdvertisementId, e.ScheduledAt }).IsUnique();
            entity.Property(e => e.RatePerPlay).HasPrecision(18, 4);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.HasOne(e => e.Driver).WithMany().HasForeignKey(e => e.DriverId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Advertisement).WithMany().HasForeignKey(e => e.AdvertisementId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DriverPlayback>(entity =>
        {
            entity.HasIndex(e => e.RequestId).IsUnique();
            entity.HasIndex(e => e.PlayedAt);
            entity.Property(e => e.Amount).HasPrecision(18, 4);
            entity.HasOne(e => e.Assignment).WithMany().HasForeignKey(e => e.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DriverPayout>(entity =>
        {
            entity.HasIndex(e => e.TransactionId).IsUnique();
            entity.Property(e => e.TransactionId).HasMaxLength(100);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Reference).HasMaxLength(200);
            entity.HasOne<Driver>().WithMany().HasForeignKey(e => e.DriverId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DriverNotification>(entity =>
        {
            entity.HasIndex(e => new { e.DriverId, e.CreatedAt });
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.Message).HasMaxLength(2000);
            entity.Property(e => e.Type).HasMaxLength(100);
            entity.HasOne<Driver>().WithMany().HasForeignKey(e => e.DriverId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DriverIssue>(entity =>
        {
            entity.HasIndex(e => new { e.DriverId, e.RequestId }).IsUnique();
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Language).HasMaxLength(2);
            entity.Property(e => e.ContactMobile).HasMaxLength(20);
            entity.Property(e => e.VehicleRegistration).HasMaxLength(20);
            entity.Property(e => e.AttachmentBlobName).HasMaxLength(500);
            entity.Property(e => e.AdminResponse).HasMaxLength(2000);
            entity.HasOne<Driver>().WithMany().HasForeignKey(e => e.DriverId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Advertisement>().WithMany().HasForeignKey(e => e.AdvertisementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.History).WithOne().HasForeignKey(e => e.IssueId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<DriverIssueHistory>(entity =>
        {
            entity.ToTable("DriverIssueHistory");
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Note).HasMaxLength(2000);
            entity.Property(e => e.By).HasMaxLength(10);
        });
        foreach (var type in new[] { typeof(Driver), typeof(DriverAssignment), typeof(DriverPlayback),
            typeof(DriverPayout), typeof(DriverNotification), typeof(DriverIssue), typeof(DriverIssueHistory) })
        {
            foreach (var property in modelBuilder.Entity(type).Metadata.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
            {
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                    value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
                    value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            }
        }

        // AuditLog configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Action).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EntityName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
        });
    }
}
