# Dual-provider EF Core migrations runbook

PhotoGallery ships **two** EF Core migration sets that target a single domain
model. Each set is bound to a different DbContext type so EF can apply the
right set per provider without conflict.

| Provider  | DbContext                          | Folder                       |
| --------- | ---------------------------------- | ---------------------------- |
| Sqlite    | `ApplicationDbContext` (base)      | `Data/Migrations/`           |
| SqlServer | `ApplicationDbContextSqlServer`    | `Data/Migrations/SqlServer/` |

Runtime picks the right context based on `Database:Provider`:

- `Sqlite` (default, all-local dev / xUnit / CI) → `ApplicationDbContext`
- `SqlServer` (Azure-backed dev / Azure SQL prod) → `ApplicationDbContextSqlServer`

Consumers always inject `ApplicationDbContext`; when the SqlServer subclass is
registered, a forwarding scoped DI registration makes the same instance
resolvable through the base type.

## Adding a new migration

### Sqlite (default — all changes start here)

```pwsh
dotnet ef migrations add <Name> `
  --project PhotoGallery `
  --startup-project PhotoGallery `
  --context ApplicationDbContext
```

Files land in `PhotoGallery/Data/Migrations/`.

### SqlServer (mirror the same schema change)

After adding the Sqlite migration, scaffold the equivalent against the
SqlServer-typed context:

```pwsh
dotnet ef migrations add <SameName> `
  --project PhotoGallery `
  --startup-project PhotoGallery `
  --context ApplicationDbContextSqlServer `
  --output-dir Data/Migrations/SqlServer
```

The `SqlServerDesignTimeDbContextFactory` constructs the context with a
placeholder connection string — no live SqlServer instance is required to
scaffold. Files land in `PhotoGallery/Data/Migrations/SqlServer/`.

**Keep the two sets in lockstep.** Every domain-model change must produce a
matching migration in both folders. The xUnit test
`SqlServerInitialCreateMigration_IsBoundToSqlServerContext` covers the binding
attribute but does not catch drift between provider-specific migration sets.

## Verifying locally

### Sqlite (anyone can do this)

```pwsh
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project PhotoGallery
```

`Database.MigrateAsync()` applies all migrations from `Data/Migrations/`.
A fresh `app.db` is generated on first run.

### SqlServer (requires reachable database)

PhotoGallery's Azure-backed dev profile expects a real Azure SQL database
provisioned by Terraform (`u/copilot/feat/azure-dev-baseline`). Until that's
applied, you can verify the SqlServer migration locally against LocalDB:

```pwsh
$env:ASPNETCORE_ENVIRONMENT = "Trial"
$env:Database__Provider = "SqlServer"
$env:ConnectionStrings__DefaultConnection = "Server=(localdb)\\mssqllocaldb;Database=PhotoGallery;Trusted_Connection=true;"
# Bypass Key Vault for the smoke test:
$env:KeyVault__Uri = ""

dotnet run --project PhotoGallery
```

On startup, `ApplicationDbContextSqlServer.Database.MigrateAsync()` applies
the SqlServer-folder migrations. Verify by inspecting LocalDB:

```pwsh
sqlcmd -S "(localdb)\mssqllocaldb" -d PhotoGallery -Q "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId"
```

You should see the SqlServer-folder migration ids (e.g. `20260510195536_InitialCreate`).

### Against Azure SQL (post-Terraform-apply)

After the platform engineer runs `terraform apply`:

1. KeyVault secret `ConnectionStrings--DefaultConnection` is provisioned with
   the Azure SQL connection string.
2. Developer authenticates: `az login` with the dev access role.
3. Run with the Trial launch profile:

   ```pwsh
   dotnet run --project PhotoGallery --launch-profile Trial
   ```

4. The app pulls the connection string from Key Vault, picks
   `ApplicationDbContextSqlServer`, and migrates Azure SQL.

## Removing a SqlServer migration

```pwsh
dotnet ef migrations remove `
  --project PhotoGallery `
  --startup-project PhotoGallery `
  --context ApplicationDbContextSqlServer
```

`--context` is mandatory — without it EF defaults to `ApplicationDbContext`
and tries to remove a Sqlite migration.

## Troubleshooting

### "No migrations configuration type was found"

You forgot `--context`. EF can't infer which context to scaffold against when
two are registered.

### "The model for context X has pending changes"

The domain model has drifted from the latest migration for one provider. Add
a new migration in both folders. Don't edit existing migrations.

### Sqlite path applies SqlServer migrations (or vice versa)

Means one of the migration files got tagged with the wrong
`[DbContext(typeof(...))]` attribute — likely from a scaffold without
`--context`. Delete the offending file and rescaffold.
