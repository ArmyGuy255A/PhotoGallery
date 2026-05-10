using Microsoft.Extensions.Configuration;

namespace PhotoGallery;

/// <summary>
/// Bridges Key-Vault-canonical configuration paths to the strongly-typed
/// binding paths the rest of the codebase already consumes.
///
/// Background:
/// The platform engineer's Terraform module + this codebase have agreed on a
/// canonical Key Vault secret-naming contract (see
/// <c>Documentation/Runbooks/local-azure-dev.md</c> and the README):
/// <code>
///   ConnectionStrings--DefaultConnection
///   Storage--AzureBlob--AccountUrl
///   Authentication--Google--ClientId
///   Authentication--Google--ClientSecret
///   Authentication--Jwt--Key
///   Email--AzureCommunicationServices--ConnectionString
/// </code>
/// The Azure Key Vault configuration provider maps a secret named
/// <c>Foo--Bar--Baz</c> to the configuration key <c>Foo:Bar:Baz</c>.
///
/// Most of those line up with our existing binding tree, but two don't:
/// <list type="bullet">
///   <item><c>Authentication:Google:ClientId</c> vs <c>Google:ClientId</c></item>
///   <item><c>Authentication:Google:ClientSecret</c> vs <c>Google:ClientSecret</c></item>
/// </list>
/// Rather than rename every consumer or split KV secrets across the two paths,
/// we add a tiny, single-purpose in-memory layer that copies the canonical
/// path to the legacy path when (and only when) the legacy path is empty.
///
/// Properties:
/// <list type="bullet">
///   <item>Non-destructive: an explicit <c>Google:ClientId</c> in
///         <c>appsettings.json</c> always wins over the KV mirror.</item>
///   <item>One-way: legacy → canonical projection is not performed (we don't
///         want a stale appsettings.Development.json value masking a KV secret).</item>
///   <item>Single source of truth for the cross-branch naming contract;
///         platform engineer's Terraform module can mirror this list 1:1.</item>
/// </list>
/// </summary>
public static class ConfigurationCanonicalAliases
{
    /// <summary>
    /// Mapping from KV-canonical configuration key (left) to the in-tree
    /// binding path used by existing code (right). Keep in lockstep with the
    /// canonical secret-name table in the README + the local-azure-dev runbook.
    /// </summary>
    private static readonly (string Canonical, string Legacy)[] Aliases =
    {
        ("Authentication:Google:ClientId",     "Google:ClientId"),
        ("Authentication:Google:ClientSecret", "Google:ClientSecret"),
        // The following canonical names already match the existing binding
        // tree exactly and need no aliasing — listed here as breadcrumbs for
        // future maintainers:
        //   ConnectionStrings:DefaultConnection             ✓ matches
        //   Storage:AzureBlob:AccountUrl                    ✓ matches
        //   Authentication:Jwt:Key                          ✓ matches
        //   Email:AzureCommunicationServices:ConnectionString ✓ matches
    };

    /// <summary>
    /// Inspects the currently-built configuration and, for each canonical →
    /// legacy alias where the canonical value is present and the legacy value
    /// is empty, adds an in-memory configuration source that fills the legacy
    /// path. Called once at startup, after Key Vault has been registered.
    /// </summary>
    public static void BridgeKeyVaultCanonicalNames(IConfigurationBuilder builder, IConfiguration current)
    {
        var bridge = new Dictionary<string, string?>();
        foreach (var (canonical, legacy) in Aliases)
        {
            var canonicalValue = current[canonical];
            if (string.IsNullOrEmpty(canonicalValue))
            {
                continue;
            }
            var legacyValue = current[legacy];
            if (!string.IsNullOrEmpty(legacyValue))
            {
                // Explicit legacy wins; don't clobber per-developer overrides.
                continue;
            }
            bridge[legacy] = canonicalValue;
        }

        if (bridge.Count > 0)
        {
            builder.AddInMemoryCollection(bridge);
        }
    }
}
