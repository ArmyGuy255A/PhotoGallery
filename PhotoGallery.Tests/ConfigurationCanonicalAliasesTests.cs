using Microsoft.Extensions.Configuration;
using PhotoGallery;

namespace PhotoGallery.Tests;

/// <summary>
/// TDD coverage for <see cref="ConfigurationCanonicalAliases.BridgeKeyVaultCanonicalNames"/>:
/// the cross-branch contract with the Terraform module pins canonical Key Vault
/// secret names that bind through .NET's double-underscore convention. Some of
/// those canonical paths don't match the existing strongly-typed binding tree
/// (notably <c>Authentication:Google:ClientId/ClientSecret</c> vs the existing
/// root-level <c>Google:ClientId/ClientSecret</c>). Rather than break every
/// consumer or duplicate secrets, we project the canonical paths back to the
/// legacy paths via an in-memory configuration layer.
///
/// The shim is one-way + non-destructive: it only fills the legacy path when
/// the canonical path is set AND the legacy path is empty. Either-or wins for
/// developers; nothing silently overrides explicit appsettings entries.
/// </summary>
public class ConfigurationCanonicalAliasesTests
{
    private static IConfigurationRoot Build(IDictionary<string, string?> values)
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(values);
        var config = builder.Build();
        ConfigurationCanonicalAliases.BridgeKeyVaultCanonicalNames(builder, config);
        return builder.Build();
    }

    [Fact]
    public void Bridges_AuthenticationGoogleClientId_ToGoogleClientId()
    {
        var config = Build(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "canonical-id"
        });

        Assert.Equal("canonical-id", config["Google:ClientId"]);
    }

    [Fact]
    public void Bridges_AuthenticationGoogleClientSecret_ToGoogleClientSecret()
    {
        var config = Build(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientSecret"] = "canonical-secret"
        });

        Assert.Equal("canonical-secret", config["Google:ClientSecret"]);
    }

    [Fact]
    public void DoesNotOverride_WhenLegacyPathAlreadySet()
    {
        // appsettings already supplies the legacy key — canonical KV name
        // should NOT clobber it. This keeps per-developer overrides predictable.
        var config = Build(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "from-keyvault",
            ["Google:ClientId"] = "from-appsettings"
        });

        Assert.Equal("from-appsettings", config["Google:ClientId"]);
    }

    [Fact]
    public void NoOp_WhenCanonicalPathIsAbsent()
    {
        var config = Build(new Dictionary<string, string?>
        {
            ["Google:ClientId"] = "from-appsettings"
        });

        Assert.Equal("from-appsettings", config["Google:ClientId"]);
        Assert.Null(config["Authentication:Google:ClientId"]);
    }
}
