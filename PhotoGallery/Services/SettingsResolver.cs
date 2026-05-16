using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;

namespace PhotoGallery.Services;

/// <summary>
/// Default <see cref="ISettingsResolver"/>. Hits the DB first (admin override),
/// falls back to <see cref="IConfiguration"/> (appsettings + env + Key Vault),
/// returns the caller-supplied fallback if neither has a value.
/// </summary>
public class SettingsResolver : ISettingsResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;

    public SettingsResolver(IServiceProvider serviceProvider, IConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        // Each resolve runs in its own scope so we never hold an
        // ApplicationDbContext open across async boundaries from a
        // singleton-style consumer.
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.RuntimeSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row != null && !string.IsNullOrEmpty(row.Value))
            return row.Value;
        return _config[key];
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
    {
        var raw = await GetAsync(key, ct);
        return int.TryParse(raw, out var v) ? v : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default)
    {
        var raw = await GetAsync(key, ct);
        return bool.TryParse(raw, out var v) ? v : fallback;
    }
}
