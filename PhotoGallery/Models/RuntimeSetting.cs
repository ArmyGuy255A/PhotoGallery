namespace PhotoGallery.Models;

/// <summary>
/// Admin-editable runtime setting (key/value pair persisted to the DB so
/// the admin page can modify worker intervals, feature flags, etc. without
/// touching <c>appsettings.json</c> + redeploying).
///
/// Resolution order is set by <c>ISettingsResolver</c>:
/// <list type="number">
///   <item><c>RuntimeSettings</c> table (admin-editable).</item>
///   <item><c>IConfiguration</c> (appsettings.json + env vars + KeyVault).</item>
///   <item>Hard-coded default in the consumer.</item>
/// </list>
///
/// Secrets (connection strings, JWT keys, OAuth secrets) are intentionally
/// NOT stored here — they belong in Key Vault.
/// </summary>
public class RuntimeSetting
{
    public Guid Id { get; set; }

    /// <summary>Colon-separated config key, e.g. <c>PhotoProcessing:IntervalSeconds</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>String representation. Parsed by the consumer or the resolver.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>UI grouping label, e.g. "Processing", "Storage", "Watermark".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>One of <c>"int"</c>, <c>"bool"</c>, <c>"string"</c>, <c>"enum"</c>.</summary>
    public string DataType { get; set; } = "string";

    /// <summary>Human description rendered as the form-field hint.</summary>
    public string? Description { get; set; }

    /// <summary>Audit: last admin to update the value.</summary>
    public string? LastModifiedBy { get; set; }

    /// <summary>Audit: last update timestamp.</summary>
    public DateTime? LastModifiedAt { get; set; }
}
