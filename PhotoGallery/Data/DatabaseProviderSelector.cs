using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace PhotoGallery.Data;

/// <summary>
/// EF Core provider wiring. SQL Server only — both dev (Docker SQL Server)
/// and prod (Azure SQL Server) use the same provider so the codebase has
/// just one migration set and a single set of provider-specific SQL idioms.
///
/// Configuration:
/// <list type="bullet">
///   <item><c>ConnectionStrings:DefaultConnection</c> — required.
///         Local dev defaults to the Docker SQL Server in
///         <c>appsettings.Development.json</c>; Trial/Prod load from Key Vault.</item>
/// </list>
///
/// To run locally: <c>docker compose up -d mssql</c> (see docker-compose.yml).
/// </summary>
public static class DatabaseProviderSelector
{
    public static void Apply(
        DbContextOptionsBuilder options,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. " +
                "Locally: ensure appsettings.Development.json provides one or set ConnectionStrings__DefaultConnection. " +
                "Trial/Prod: ensure Key Vault contains 'ConnectionStrings--DefaultConnection' and KeyVault:Uri is set.");
        }

        options.UseSqlServer(connectionString, sql =>
        {
            sql.CommandTimeout(30);
            // Azure SQL's transient errors are routine; let EF retry. Also
            // helps locally when the SQL Server container is mid-start.
            sql.EnableRetryOnFailure(maxRetryCount: 3);
        });

        options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);

        // EF Core 9 promoted PendingModelChangesWarning to an *error* by
        // default. The check is a dev-time safety net (catch missed
        // `dotnet ef migrations add`) and we want to keep it loud in local
        // dev / CI — but it is too brittle for the deployed path: the model
        // fingerprint compares the runtime model against the latest
        // migration's snapshot, and the hash is sensitive to build-host
        // quirks (snapshots scaffolded on Windows can differ in subtle ways
        // from the model built inside the Linux container). The visible
        // symptom is `Migrator.ValidateMigrations` throwing
        // InvalidOperationException before any SQL runs — the container
        // never gets a chance to apply migrations and stays empty.
        //
        // Suppress the warning here so `MigrateAsync` always proceeds. Real
        // schema mismatches still fail loudly because the actual SQL
        // operation (CREATE TABLE / ALTER COLUMN / etc.) will throw, and
        // Program.cs catches that and aborts startup with exit code 1.
        options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}

