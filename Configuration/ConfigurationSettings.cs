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
    public List<string> AdminUsers { get; set; } = new();
    public bool DISABLE_AUTH { get; set; }
    public string AllowedHosts { get; set; } = "*";
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
