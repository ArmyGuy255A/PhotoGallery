using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Configuration;

/// <summary>
/// Bootstraps the typed configuration system for PhotoGallery.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddConfigurationServices(builder.Configuration, out var settings);
/// </code>
///
/// Other code consumes <see cref="Microsoft.Extensions.Options.IOptions{ConfigurationSettings}"/>
/// via constructor injection. Direct <see cref="IConfiguration"/> reads with magic strings
/// are discouraged — see the photogallery-architect-skill review checklist.
///
/// Reference: clean-architecture-guide skill, "Cross-Cutting Concerns Live in Sub-Projects".
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Bind <see cref="ConfigurationSettings"/> from the supplied <paramref name="configuration"/>
    /// and register it as a singleton + an <c>IOptions&lt;ConfigurationSettings&gt;</c>.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application's IConfiguration (already built by the host)</param>
    /// <param name="settings">Out parameter: bound settings, useful for inline reads at startup
    /// where service resolution isn't available yet (e.g., choosing the storage provider).</param>
    public static IServiceCollection AddConfigurationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        out ConfigurationSettings settings)
    {
        services.Configure<ConfigurationSettings>(configuration);
        settings = new ConfigurationSettings();
        configuration.Bind(settings);
        services.AddSingleton(settings);
        return services;
    }
}
