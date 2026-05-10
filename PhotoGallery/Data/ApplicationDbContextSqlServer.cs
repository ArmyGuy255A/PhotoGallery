using Microsoft.EntityFrameworkCore;

namespace PhotoGallery.Data;

/// <summary>
/// Provider-specific subclass of <see cref="ApplicationDbContext"/> used when
/// <c>Database:Provider=SqlServer</c>.
///
/// Why a subclass instead of a single context with a runtime-switched provider?
/// EF Core ties migrations to a specific DbContext type via the
/// <c>[DbContext(typeof(...))]</c> attribute on each generated migration class.
/// To keep separate, non-conflicting migration sets for Sqlite (existing) and
/// SqlServer (new), we need distinct context types. The subclass inherits the
/// full model from the base, so there is no duplication of <c>DbSet&lt;T&gt;</c>
/// declarations or model configuration — only the type identity differs.
///
/// Runtime: <c>Program.cs</c> registers either <see cref="ApplicationDbContext"/>
/// (Sqlite) or this subclass (SqlServer) based on configuration, with a
/// forwarding scoped registration so consumers that inject the base type
/// keep working unchanged.
///
/// Migrations: SqlServer migrations live under <c>Data/Migrations/SqlServer/</c>
/// and target this subclass. Generate them with:
/// <code>
/// dotnet ef migrations add &lt;Name&gt; \
///   --project PhotoGallery --startup-project PhotoGallery \
///   --context ApplicationDbContextSqlServer \
///   --output-dir Data/Migrations/SqlServer
/// </code>
/// </summary>
public class ApplicationDbContextSqlServer : ApplicationDbContext
{
    public ApplicationDbContextSqlServer(DbContextOptions<ApplicationDbContextSqlServer> options)
        : base(options)
    {
    }
}
