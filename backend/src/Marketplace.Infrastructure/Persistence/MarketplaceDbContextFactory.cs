using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can create a context without booting the API
/// host (which would require a reachable database and a valid JWT key).
/// </summary>
public sealed class MarketplaceDbContextFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=Marketplace;Username=postgres;Password=postgres;Include Error Detail=true";

        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(MarketplaceDbContext).Assembly.FullName))
            .Options;

        return new MarketplaceDbContext(options);
    }
}
