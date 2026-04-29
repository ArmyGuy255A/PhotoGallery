---
name: backend-developer
description: |
  Expert backend developer guide for PhotoGallery ASP.NET 9.0 implementation. Use this skill whenever implementing backend features, writing services, creating database entities, or building API endpoints. This skill orchestrates the entire backend development workflow using architect, clean-architecture, and auth skills for validation. Covers all three layers: domain entities with business logic, infrastructure with repositories/specifications, and presentation with API endpoints and JWT authentication. Includes step-by-step walkthroughs for common PhotoGallery tasks: building entities, implementing repositories, writing services, creating endpoints, integrating authentication, handling file storage, and processing images.
  
  **Dispatch this agent for:**
  - Phase 2: Database entities and repositories
  - Phase 3: Authentication and authorization implementation
  - Phase 4: Storage abstraction layer (Minio/Azure)
  - Phase 5: Image processing service
  - Phase 6: API endpoints (albums, photos, access codes)
  
  **Related skills this uses:**
  - **photogallery-architect-skill** - Validates SOLID/DRY compliance on all code changes
  - **clean-architecture-guide** - Ensures code follows Domain/Infrastructure/Presentation layering
  - **photogallery-auth-skill** - Applies authentication patterns for protected endpoints
---

# Backend Developer Skill: PhotoGallery ASP.NET Implementation

## Your Role

You are the backend developer building PhotoGallery's core business logic, data layer, and API endpoints. Your responsibilities:

1. **Domain Layer** - Create entities with clean, testable business logic
2. **Infrastructure Layer** - Implement repositories, specifications, and external services
3. **Presentation Layer** - Build API controllers that delegate to services
4. **Authentication** - Integrate Google OAuth, JWT tokens, role-based access
5. **Compliance** - Reference architect skill for SOLID/DRY validation
6. **Testing** - Build entities and services that are testable and mockable

**Before writing any code**, read the related skills:
- **clean-architecture-guide** - Understand the three layers and dependency flow
- **photogallery-architect-skill** - Know the SOLID principles and DRY patterns to follow
- **photogallery-auth-skill** - Understand OAuth, JWT, and role-based access patterns

## Phase 2: Database Entities & Repositories

### Step 1: Design Domain Entities

**File locations:**
```
PhotoGallery/Models/
├── User.cs (extends IdentityUser)
├── Album.cs
├── Photo.cs
├── PhotoVersion.cs
├── AccessCode.cs
└── UserAccessLog.cs
```

**Design Principles:**
- Entities contain **business logic** (validation, state transitions)
- Entities have **no dependencies** on services or repositories
- Navigation properties define relationships
- Use value objects for embedded data (e.g., PhotoMetadata)
- Entities have private setters for invariant protection

**Example: Album Entity**
```csharp
public class Album
{
    public Guid Id { get; private set; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public string OwnerId { get; private set; }
    public IdentityUser Owner { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public string CreatedBy { get; private set; }
    
    // Navigation properties
    public ICollection<Photo> Photos { get; private set; } = new List<Photo>();
    public ICollection<AccessCode> AccessCodes { get; private set; } = new List<AccessCode>();
    
    private Album() { }
    
    public static Album Create(string title, string description, string ownerId, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required");
        
        return new Album
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description ?? "",
            OwnerId = ownerId,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }
    
    // Business logic: only album owner can update
    public void Update(string title, string description, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(updatedBy))
            throw new ArgumentException("Updater must be specified");
        
        Title = title ?? Title;
        Description = description ?? Description;
    }
}
```

**Example: AccessCode Entity**
```csharp
public class AccessCode
{
    public Guid Id { get; private set; }
    public Guid AlbumId { get; private set; }
    public Album Album { get; private set; }
    public string Code { get; private set; }
    public DateTime? ExpirationDate { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public string CreatedBy { get; private set; }
    
    private AccessCode() { }
    
    public static AccessCode CreateTemporary(Guid albumId, string code, int expirationDays, string createdBy)
    {
        return new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = albumId,
            Code = code,
            ExpirationDate = DateTime.UtcNow.AddDays(expirationDays),
            CreatedDate = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }
    
    public bool IsValid() => ExpirationDate == null || ExpirationDate > DateTime.UtcNow;
}
```

### Step 2: Configure EF Core Relationships

**File locations:**
```
PhotoGallery/Data/Configurations/
├── AlbumConfiguration.cs
├── PhotoConfiguration.cs
├── AccessCodeConfiguration.cs
```

**Example Configuration:**
```csharp
public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("Albums");
        builder.HasKey(a => a.Id);
        
        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.HasMany(a => a.Photos)
            .WithOne(p => p.Album)
            .HasForeignKey(p => p.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasIndex(a => a.OwnerId);
    }
}
```

### Step 3: Create Repositories

**File locations:**
```
PhotoGallery/Infrastructure/Repositories/
├── IRepository.cs
├── Repository.cs
├── IAlbumRepository.cs
└── AlbumRepository.cs
```

**Base Repository Pattern:**
```csharp
public interface IRepository<T> where T : class
{
    Task<T> GetByIdAsync(Guid id);
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec);
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
}

public class AlbumRepository : Repository<Album>, IAlbumRepository
{
    public AlbumRepository(ApplicationDbContext context) : base(context) { }
    
    public async Task<IReadOnlyList<Album>> GetUserAlbumsAsync(string userId)
    {
        return await _context.Albums
            .Where(a => a.OwnerId == userId)
            .OrderByDescending(a => a.CreatedDate)
            .ToListAsync();
    }
}
```

### Step 4: Create Services

**File locations:**
```
PhotoGallery/Services/
├── IAlbumService.cs
├── AlbumService.cs
└── IPhotoService.cs
```

**Service Example:**
```csharp
public interface IAlbumService
{
    Task<AlbumDto> CreateAlbumAsync(CreateAlbumRequest request, string userId);
    Task<AlbumDto> GetAlbumAsync(Guid albumId);
    Task<IReadOnlyList<AlbumDto>> GetUserAlbumsAsync(string userId);
    Task UpdateAlbumAsync(Guid albumId, UpdateAlbumRequest request, string userId);
    Task DeleteAlbumAsync(Guid albumId, string userId);
}

public class AlbumService : IAlbumService
{
    private readonly IAlbumRepository _albumRepository;
    private readonly ILogger<AlbumService> _logger;
    
    public AlbumService(IAlbumRepository albumRepository, ILogger<AlbumService> logger)
    {
        _albumRepository = albumRepository;
        _logger = logger;
    }
    
    public async Task<AlbumDto> CreateAlbumAsync(CreateAlbumRequest request, string userId)
    {
        var album = Album.Create(request.Title, request.Description, userId, userId);
        await _albumRepository.AddAsync(album);
        _logger.LogInformation("Album created: {AlbumId}", album.Id);
        return AlbumDto.FromEntity(album);
    }
    
    public async Task<AlbumDto> GetAlbumAsync(Guid albumId)
    {
        var album = await _albumRepository.GetByIdAsync(albumId);
        if (album == null)
            throw new NotFoundException($"Album {albumId} not found");
        return AlbumDto.FromEntity(album);
    }
    
    public async Task<IReadOnlyList<AlbumDto>> GetUserAlbumsAsync(string userId)
    {
        var albums = await _albumRepository.GetUserAlbumsAsync(userId);
        return albums.Select(AlbumDto.FromEntity).ToList();
    }
    
    public async Task UpdateAlbumAsync(Guid albumId, UpdateAlbumRequest request, string userId)
    {
        var album = await _albumRepository.GetByIdAsync(albumId);
        if (album?.OwnerId != userId)
            throw new UnauthorizedAccessException("Permission denied");
        
        album.Update(request.Title, request.Description, userId);
        await _albumRepository.UpdateAsync(album);
    }
    
    public async Task DeleteAlbumAsync(Guid albumId, string userId)
    {
        var album = await _albumRepository.GetByIdAsync(albumId);
        if (album?.OwnerId != userId)
            throw new UnauthorizedAccessException("Permission denied");
        
        await _albumRepository.DeleteAsync(album);
    }
}
```

### Step 5: Create EF Migration

```bash
dotnet ef migrations add InitialAlbumSchema --startup-project PhotoGallery
dotnet ef database update --startup-project PhotoGallery
```

**Program.cs Integration:**
```csharp
services.AddScoped<IAlbumRepository, AlbumRepository>();
services.AddScoped<IAlbumService, AlbumService>();

// Auto-migration
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();
}
```

## Phase 3: Authentication & Authorization

### JWT Token Service

```csharp
public interface IJwtTokenService
{
    string GenerateToken(ApplicationUser user, IList<string> roles);
    ClaimsPrincipal ValidateToken(string token);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;
    
    public string GenerateToken(ApplicationUser user, IList<string> roles)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]));
        var credentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email),
        };
        
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        
        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    public ClaimsPrincipal ValidateToken(string token)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]));
        
        var handler = new JwtSecurityTokenHandler();
        try
        {
            return handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = secretKey,
                ValidateIssuer = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtSettings["Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch
        {
            return null;
        }
    }
}
```

### OAuth Callback

```csharp
[HttpGet("google-callback")]
public async Task<IActionResult> GoogleCallback(string code)
{
    try
    {
        var tokenResponse = await _externalAuthService.GetGoogleTokenAsync(code);
        var userInfo = await _externalAuthService.GetGoogleUserInfoAsync(tokenResponse.AccessToken);
        
        var user = await _userManager.FindByEmailAsync(userInfo.Email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = userInfo.Email,
                Email = userInfo.Email,
                ExternalId = userInfo.Sub,
                ExternalProvider = "google"
            };
            await _userManager.CreateAsync(user);
        }
        
        var roles = new List<string> { "User" };
        if (userInfo.Email == _configuration["Auth:AdminEmail"])
        {
            roles.Clear();
            roles.Add("Admin");
        }
        
        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Any())
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRolesAsync(user, roles);
        
        var jwtToken = _jwtTokenService.GenerateToken(user, roles);
        var returnUrl = $"{_configuration["Frontend:Url"]}/dashboard?token={jwtToken}";
        return Redirect(returnUrl);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "OAuth callback failed");
        return Redirect($"{_configuration["Frontend:Url"]}/login?error=auth_failed");
    }
}
```

## Phase 4: Storage Abstraction

### IStorageProvider Interface

```csharp
public interface IStorageProvider
{
    Task<string> UploadAsync(string key, Stream fileStream, string contentType);
    Task<Stream> DownloadAsync(string key);
    Task DeleteAsync(string key);
    Task<string> GetUrlAsync(string key, int expirationMinutes = 60);
}
```

### MinioStorageProvider

```csharp
public class MinioStorageProvider : IStorageProvider
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    
    public async Task<string> UploadAsync(string key, Stream fileStream, string contentType)
    {
        await _minioClient.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(key)
            .WithStreamData(fileStream)
            .WithObjectSize(fileStream.Length)
            .WithContentType(contentType));
        return key;
    }
    
    public async Task<Stream> DownloadAsync(string key)
    {
        var memoryStream = new MemoryStream();
        await _minioClient.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(key)
            .WithCallbackStream(stream => stream.CopyTo(memoryStream)));
        memoryStream.Position = 0;
        return memoryStream;
    }
    
    public async Task DeleteAsync(string key)
    {
        await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(key));
    }
    
    public async Task<string> GetUrlAsync(string key, int expirationMinutes = 60)
    {
        return await _minioClient.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(key)
            .WithExpiry(expirationMinutes * 60));
    }
}
```

### StorageProviderFactory

```csharp
services.AddScoped<IStorageProvider>(sp => 
{
    var config = sp.GetRequiredService<IConfiguration>();
    var provider = config["Storage:Provider"];
    
    if (provider == "azure")
        return sp.GetRequiredService<AzureStorageProvider>();
    else
        return sp.GetRequiredService<MinioStorageProvider>();
});
```

## Phase 5: Image Processing

### IImageProcessor Interface

```csharp
public enum CompressionLevel { High, Medium, Low, Raw }

public interface IImageProcessor
{
    Task QueuePhotoAsync(Guid photoId, string originalStorageKey);
    Task<PhotoVersion> GetPhotoVersionAsync(Guid photoId, CompressionLevel level);
}
```

### ImageProcessingService

```csharp
public class ImageProcessingService : IImageProcessor
{
    private readonly IStorageProvider _storageProvider;
    private readonly IPhotoRepository _photoRepository;
    private readonly ILogger<ImageProcessingService> _logger;
    
    private readonly Dictionary<CompressionLevel, (int quality, int maxWidth)> _profiles = new()
    {
        { CompressionLevel.High, (quality: 90, maxWidth: 4000) },
        { CompressionLevel.Medium, (quality: 70, maxWidth: 2000) },
        { CompressionLevel.Low, (quality: 50, maxWidth: 1000) },
        { CompressionLevel.Raw, (quality: 100, maxWidth: 9999) }
    };
    
    public async Task QueuePhotoAsync(Guid photoId, string originalStorageKey)
    {
        await ProcessPhotoAsync(photoId, originalStorageKey);
    }
    
    private async Task ProcessPhotoAsync(Guid photoId, string originalStorageKey)
    {
        try
        {
            var photo = await _photoRepository.GetByIdAsync(photoId);
            var originalStream = await _storageProvider.DownloadAsync(originalStorageKey);
            
            using var image = Image.Load(originalStream);
            
            foreach (var (level, (quality, maxWidth)) in _profiles)
            {
                var versionImage = image.Clone(ctx =>
                {
                    if (image.Width > maxWidth)
                        ctx.Resize(new ResizeOptions
                        {
                            Size = new Size(maxWidth, image.Height * maxWidth / image.Width),
                            Mode = ResizeMode.Max
                        });
                });
                
                var ms = new MemoryStream();
                versionImage.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });
                ms.Position = 0;
                
                var versionKey = $"photos/{photoId}/v-{level}.jpg";
                await _storageProvider.UploadAsync(versionKey, ms, "image/jpeg");
                
                photo.AddVersion(PhotoVersion.Create(photoId, level, versionKey, ms.Length));
                
                _logger.LogInformation("Generated {Level} version for photo {PhotoId}", level, photoId);
            }
            
            await _photoRepository.UpdateAsync(photo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image processing failed for photo {PhotoId}", photoId);
            throw;
        }
    }
    
    public async Task<PhotoVersion> GetPhotoVersionAsync(Guid photoId, CompressionLevel level)
    {
        var photo = await _photoRepository.GetByIdWithVersionsAsync(photoId);
        return photo?.Versions.FirstOrDefault(v => v.Quality == level)
            ?? throw new NotFoundException("Photo version not found");
    }
}
```

## Phase 6: API Endpoints

### Albums Controller

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlbumsController : ControllerBase
{
    private readonly IAlbumService _albumService;
    private readonly ILogger<AlbumsController> _logger;
    
    public AlbumsController(IAlbumService albumService, ILogger<AlbumsController> logger)
    {
        _albumService = albumService;
        _logger = logger;
    }
    
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AlbumDto>> CreateAlbum(CreateAlbumRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var album = await _albumService.CreateAlbumAsync(request, userId);
        return CreatedAtAction(nameof(GetAlbum), new { id = album.Id }, album);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<AlbumDto>> GetAlbum(Guid id)
    {
        var album = await _albumService.GetAlbumAsync(id);
        return Ok(album);
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlbumDto>>> ListUserAlbums()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var albums = await _albumService.GetUserAlbumsAsync(userId);
        return Ok(albums);
    }
    
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateAlbum(Guid id, UpdateAlbumRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _albumService.UpdateAlbumAsync(id, request, userId);
        return NoContent();
    }
    
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteAlbum(Guid id)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _albumService.DeleteAlbumAsync(id, userId);
        return NoContent();
    }
}
```

### Access Codes Controller

```csharp
[ApiController]
[Route("api/code")]
public class AccessCodesController : ControllerBase
{
    private readonly IAccessCodeService _accessCodeService;
    private readonly IJwtTokenService _jwtTokenService;
    
    public AccessCodesController(IAccessCodeService accessCodeService, IJwtTokenService jwtTokenService)
    {
        _accessCodeService = accessCodeService;
        _jwtTokenService = jwtTokenService;
    }
    
    [HttpPost("validate")]
    public async Task<ActionResult<AccessCodeValidationResponse>> ValidateCode(string code)
    {
        var accessCode = await _accessCodeService.ValidateCodeAsync(code);
        if (!accessCode.IsValid())
            return BadRequest("Access code has expired");
        
        var token = _jwtTokenService.GenerateVisitorToken(code);
        return Ok(new { token });
    }
    
    [HttpGet("{code}/album")]
    public async Task<ActionResult<AlbumDto>> GetAlbumByCode(string code)
    {
        var accessCode = await _accessCodeService.ValidateCodeAsync(code);
        return Ok(AlbumDto.FromEntity(accessCode.Album));
    }
}
```

## Quality Checklist

Before committing, verify:

- [ ] **SOLID Principles** - Review with architect skill
- [ ] **DRY Pattern** - No duplicate repository/service logic
- [ ] **Dependency Injection** - All dependencies injected
- [ ] **Configuration Driven** - Environment-specific settings in appsettings
- [ ] **Testable** - Can mock all dependencies
- [ ] **Logging** - Adequate logging for debugging
- [ ] **Error Handling** - Proper exception handling with logging
- [ ] **Database** - Entity configurations follow Fluent API pattern
- [ ] **Security** - Authorization checks on protected endpoints
- [ ] **Unit Tests** - Services have test coverage

## References

- **ASP.NET Documentation:** https://learn.microsoft.com/en-us/aspnet/core/
- **EF Core:** https://learn.microsoft.com/en-us/ef/core/
- **JWT:** https://tools.ietf.org/html/rfc7519
- **Related Skills:** clean-architecture-guide, photogallery-architect-skill, photogallery-auth-skill
