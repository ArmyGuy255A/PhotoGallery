using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace PhotoGallery.Data;

/// <summary>
/// Runtime selector for the EF Core database provider.
///
/// Configuration keys:
/// <list type="bullet">
///   <item><c>Database:Provider</c> — <c>Sqlite</c> (default, all-local) | <c>SqlServer</c> (Azure-backed dev / future Azure SQL).</item>
///   <item><c>ConnectionStrings:DefaultConnection</c> — required, resolved from Key Vault in the Azure-backed profile.</item>
/// </list>
///
/// Note: SqlServer migrations live under Data/Migrations/SqlServer/ and are
/// scaffolded against <see cref="ApplicationDbContextSqlServer"/>. The Sqlite
/// migrations under Data/Migrations/ are the default and bind to the base
/// <see cref="ApplicationDbContext"/>. Program.cs picks which context to
/// register based on the same <c>Database:Provider</c> key consumed here.
/// </summary>
public static class DatabaseProviderSelector
{
    public static void Apply(
        DbContextOptionsBuilder options,
        IConfiguration configuration)
    {
        var providerName = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. " +
                "For DevelopmentAzure, ensure Key Vault contains the secret " +
                "'ConnectionStrings--DefaultConnection' and KeyVault:Uri is set.");
        }

        switch (providerName.ToLowerInvariant())
        {
            case "sqlite":
                options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(5));
                break;
            case "sqlserver":
                // Azure-backed dev / future Azure SQL Database. EnableRetryOnFailure
                // because Azure SQL's transient errors are routine.
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.CommandTimeout(30);
                    sql.EnableRetryOnFailure(maxRetryCount: 3);
                });
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown database provider: {providerName}. Supported: Sqlite, SqlServer.");
        }

        options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
    }
}
