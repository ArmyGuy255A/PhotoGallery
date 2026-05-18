namespace Configuration;

/// <summary>
/// Strongly-typed root configuration POCO for PhotoGallery.
/// Mirrors the structure of <c>appsettings.json</c> so consumers can inject
/// <see cref="Microsoft.Extensions.Options.IOptions{ConfigurationSettings}"/>
/// instead of using magic-string keys against <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
///
/// Reference: see the photogallery-architect-skill "Adding a Cross-Cutting Concern" recipe
/// for the convention this file follows.
/// </summary>
public class ConfigurationSettings
{
    public ConnectionStrings ConnectionStrings { get; set; } = new();
    public Google Google { get; set; } = new();
    public Auth Auth { get; set; } = new();
    public Authentication Authentication { get; set; } = new();
    public Storage Storage { get; set; } = new();
    public BlobStorage BlobStorage { get; set; } = new();
    public Email Email { get; set; } = new();
    public PhotoProcessing PhotoProcessing { get; set; } = new();
    public Frontend Frontend { get; set; } = new();
    public Cors Cors { get; set; } = new();
    public List<string> AdminUsers { get; set; } = new();
    public bool DISABLE_AUTH { get; set; }
    public string AllowedHosts { get; set; } = "*";

    /// <summary>
    /// Reverse-proxy mount point this backend serves under. Empty string
    /// (default) means the app serves at root, which is the local-dev shape
    /// (raw <c>dotnet run</c>, no proxy). Set to a leading-slash path like
    /// <c>"/photogallery"</c> when sitting behind <c>nginx-appeid</c> so
    /// <c>app.UsePathBase(BasePath)</c> strips the prefix into
    /// <c>HttpContext.Request.PathBase</c> and routing / link generation
    /// reason about the real mount point.
    ///
    /// Bound at JSON root (no nesting section) per the project's
    /// <c>configuration.Bind(settings)</c> convention — see
    /// <see cref="DependencyInjection.AddConfigurationServices"/>.
    /// Reference: epic #159 / story #160 (Configurable base path for
    /// reverse-proxy deployment).
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}

public class ConnectionStrings
{
    public string DefaultConnection { get; set; } = string.Empty;
}

public class Google
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}

public class Auth
{
    public string AdminEmail { get; set; } = string.Empty;
}

public class Authentication
{
    public Jwt Jwt { get; set; } = new();
}

public class Jwt
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
}

public class Storage
{
    public string Type { get; set; } = "Minio";
    public Minio Minio { get; set; } = new();
    public AzureStorage Azure { get; set; } = new();
}

public class Minio
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseSSL { get; set; }
    public string BucketName { get; set; } = string.Empty;
}

public class AzureStorage
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
}

public class BlobStorage
{
    public int PreSignedUrlTTLDays { get; set; } = 7;
    public int PreSignedUrlRefreshWindowDays { get; set; } = 5;
    public int RefreshWorkerIntervalHours { get; set; } = 24;
    public List<string> CachedQualities { get; set; } = new();
    public bool VerifyCachedUrls { get; set; } = true;
}

public class Email
{
    public string Provider { get; set; } = "mock";
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = string.Empty;
    public AzureCommunicationServices AzureCommunicationServices { get; set; } = new();
}

public class AzureCommunicationServices
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class PhotoProcessing
{
    public int IntervalSeconds { get; set; } = 5;
}

/// <summary>
/// Frontend integration settings. Currently used by the backend's CORS
/// policy to whitelist the SPA origin without hardcoding it. Configure
/// via Frontend:Url in appsettings.json or the Frontend__Url env var.
/// </summary>
public class Frontend
{
    /// <summary>
    /// Base URL of the SPA. Defaults to the Angular dev server on :4300.
    /// Override per-environment (e.g. https://photogallery.example.com).
    /// </summary>
    public string Url { get; set; } = "http://localhost:4300";
}

/// <summary>
/// CORS allowlist configuration. The named "AllowFrontendDev" policy in
/// Program.cs unions <see cref="AllowedOrigins"/> with
/// <see cref="Frontend.Url"/>, deduplicates, drops empties, and feeds the
/// result into <c>WithOrigins(...)</c>.
///
/// Binds from <c>Cors:AllowedOrigins</c> in appsettings, or via
/// <c>Cors__AllowedOrigins__0</c>, <c>Cors__AllowedOrigins__1</c>, ... env
/// vars (the ACA env injection pattern used by the dev Terraform).
/// </summary>
public class Cors
{
    /// <summary>
    /// Additional origins to allow beyond <see cref="Frontend.Url"/>. Each
    /// must be a scheme+host (+optional port) with NO trailing slash, e.g.
    /// <c>https://wonderful-tree.azurestaticapps.net</c>.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = new();
}
