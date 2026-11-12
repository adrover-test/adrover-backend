using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Adrover.Data.Context;

/// <summary>
/// Design-time factory for creating DbContext during migrations
/// This allows EF Core to create the context without needing the full DI container
/// </summary>
public class AdRoverDbContextFactory : IDesignTimeDbContextFactory<AdRoverDbContext>
{
    public AdRoverDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AdRoverDbContext>();
        
        // Use local SQL Server instance for development migrations
        optionsBuilder.UseSqlServer(
            "Server=.;Database=AdRover;Trusted_Connection=true;TrustServerCertificate=true;",
            b => b.MigrationsAssembly("Adrover.Data")
        );

        return new AdRoverDbContext(optionsBuilder.Options);
    }
}
