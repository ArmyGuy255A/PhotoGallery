---
name: backend-developer
description: |
  Expert backend developer guide for PhotoGallery ASP.NET 9.0 implementation using Test-Driven Development. Use this skill whenever implementing backend features, writing services, creating database entities, or building API endpoints. This skill orchestrates the entire backend development workflow using TDD-first approach with architect, clean-architecture, and auth skills for validation. Covers all three layers: domain entities with business logic, infrastructure with repositories/specifications, and presentation with API endpoints and JWT authentication. Includes step-by-step walkthroughs for common PhotoGallery tasks: building entities, implementing repositories, writing services, creating endpoints, integrating authentication, handling file storage, and processing images.
  
  **CRITICAL WORKFLOW: Always follow this sequence (non-negotiable)**
  1. Consult Documentation/Architecture/DESIGN_DECISIONS.md (understand existing patterns)
  2. Consult photogallery-architect-skill (ask for design approval if unclear)
  3. Consult photogallery-tdd-unit-testing skill (design test cases)
  4. Write xUnit tests in PhotoGallery.Tests (RED - tests fail)
  5. Implement minimal code to make tests pass (GREEN)
  6. Refactor while keeping all tests passing (BLUE)
  7. Run: dotnet test PhotoGallery.Tests (all tests must pass)
  8. Consult architect skill for SOLID/DRY validation
  9. Update Documentation/ if design changed
  10. Commit with tests and implementation together
  
  **ALL unit tests must pass before moving to next stage. NO EXCEPTIONS.**
  
  **Dispatch this agent for:**
  - Phase 2: Database entities and repositories
  - Phase 3: Authentication and authorization implementation
  - Phase 4: Storage abstraction layer (Minio/Azure)
  - Phase 5: Image processing service
  - Phase 6: API endpoints (albums, photos, access codes)
  
  **Related skills this uses:**
  - **photogallery-documentation-skill** - Reference design decisions before implementing
  - **photogallery-tdd-unit-testing** - MUST be consulted FIRST to design test cases
  - **photogallery-architect-skill** - Validates SOLID/DRY compliance on all code changes
  - **clean-architecture-guide** - Ensures code follows Domain/Infrastructure/Presentation layering
  - **photogallery-auth-skill** - Applies authentication patterns for protected endpoints

  This skill delegates to copilot-dev-team plugin meta-skills for procedural detail: `aspnet-api-recipe` (controller/endpoint scaffolding), `aspnet-tdd-xunit` (test-first workflow), `efcore-migration-safer` (migrations), `clean-architecture-review` (layering), and `solid-dry-principles` (refactor validation). Auto-trigger these when their conditions match.
---

# Backend Developer Skill: PhotoGallery ASP.NET Implementation with TDD

## Plugin Meta-Skills

The `copilot-dev-team` plugin provides procedural meta-skills that this skill delegates to. They auto-trigger by description match — you do not need to invoke them explicitly, but their content takes precedence over any duplicated guidance here. If there is a conflict, prefer the meta-skill (it is canonical).

| Phase / situation | MUST consult (auto-trigger) | Consider |
| --- | --- | --- |
| Designing test cases / writing xUnit tests | `aspnet-tdd-xunit` | — |
| Adding a new API endpoint / controller | `aspnet-api-recipe` | — |
| Adding/altering an EF Core migration | `efcore-migration-safer` | — |
| Validating SOLID/DRY on production code | `solid-dry-principles`, `clean-architecture-review` | — |
| Logging in services / handlers | — | `serilog-recipe` |
| App config / per-environment settings | — | `appsettings-environments`, `settings-api-hot-reload` |
| Storage / queue / DB provider abstractions | — | `provider-abstraction-pattern`, `blob-provider-abstraction`, `relational-provider-abstraction`, `queue-provider-abstraction` |
| Multi-implementation construction | — | `factory-pattern-recipe`, `builder-pattern-recipe` |

**Workflow callouts** (where each meta-skill triggers inside this skill's existing phases):

- *→ Step 3 RED phase / Step 4 (write tests) — consult `aspnet-tdd-xunit` for canonical xUnit + WebApplicationFactory patterns.*
- *→ Step 5 GREEN / Step 6 BLUE / Step 8 SOLID validation — consult `solid-dry-principles` and `clean-architecture-review`.*
- *→ Phase 6 (API endpoints) — consult `aspnet-api-recipe` for the canonical 6-step endpoint workflow.*
- *→ Any EF Core migration step — consult `efcore-migration-safer`.*

## Your Role

You are the backend developer building PhotoGallery's core business logic, data layer, and API endpoints using Test-Driven Development. Your responsibilities:

1. **Documentation First** - Read Documentation/Architecture/ before implementing
2. **TDD First** - Write tests BEFORE implementation (non-negotiable)
3. **Domain Layer** - Create entities with clean, testable business logic
4. **Infrastructure Layer** - Implement repositories, specifications, and external services
5. **Presentation Layer** - Build API controllers that delegate to services
6. **Authentication** - Integrate Google OAuth, JWT tokens, role-based access
7. **Compliance** - Reference architect skill for SOLID/DRY validation
8. **Testing** - All tests pass, always
9. **Documentation** - Update Architecture/ when design changes

**Before writing ANY code**, read these IN THIS ORDER:
1. **Documentation/Guides/PRE-IMPLEMENTATION-CHECKLIST.md** - MANDATORY checklist for every task
2. **Documentation/Architecture/DESIGN_DECISIONS.md** - Understand existing patterns
3. **photogallery-architect-skill** - Ask for design if unclear
4. **photogallery-tdd-unit-testing** - Design test cases FIRST
5. **clean-architecture-guide** - Understand the three layers and dependency flow
6. **photogallery-auth-skill** - Understand OAuth, JWT, and role-based access patterns

## Plugin Meta-Skills

The `copilot-dev-team` plugin provides procedural meta-skills that this skill delegates to. They auto-trigger by description match — you do not need to invoke them explicitly, but their content takes precedence over any duplicated guidance here. If there is a conflict, prefer the meta-skill (it is the canonical version).

| Phase / situation | MUST consult (auto-trigger) | Consider |
| --- | --- | --- |
| Designing test cases / writing xUnit tests | `aspnet-tdd-xunit` | — |
| Adding a new API endpoint / controller | `aspnet-api-recipe` | — |
| Adding/altering an EF Core migration | `efcore-migration-safer` | — |
| Validating SOLID/DRY on production code | `solid-dry-principles`, `clean-architecture-review` | — |
| Logging in services / handlers | — | `serilog-recipe` |
| App config / per-environment settings | — | `appsettings-environments`, `settings-api-hot-reload` |
| Storage / queue / DB provider abstractions | — | `provider-abstraction-pattern`, `blob-provider-abstraction`, `relational-provider-abstraction`, `queue-provider-abstraction` |
| Multi-implementation construction | — | `factory-pattern-recipe`, `builder-pattern-recipe` |

## The Mandatory Development Workflow

### Step 1: Consult Documentation

```
Open: Documentation/Architecture/DESIGN_DECISIONS.md
Question: Has anyone solved this problem before?
- If YES: Follow existing pattern
- If NO: Continue to Step 2
```

### Step 2: Ask Architect for Design

If design is unclear:
```
Use ask_user tool to get design approval from user
Record: What design was approved
Move to Step 3
```

### Step 3: Design Tests (RED Phase)

*→ consult `aspnet-tdd-xunit` for the canonical RED/GREEN/REFACTOR workflow.*

Consult: `photogallery-tdd-unit-testing`

```csharp
// PhotoGallery.Tests/YourFeatureTests.cs
[Fact]
public void Feature_Should_Behave_Correctly()
{
    // Arrange: Set up test data
    // Act: Execute the feature
    // Assert: Verify expected behavior
}
```

Run: `dotnet test PhotoGallery.Tests --filter "ClassName=YourFeatureTests"`
Result: Tests FAIL (this is expected and correct!)

### Step 4: Write Implementation (GREEN Phase)

Create: `PhotoGallery/Services/YourService.cs` or `PhotoGallery/Models/YourEntity.cs`

```csharp
public class YourService
{
    // Minimal code to make tests pass
}
```

Run: `dotnet test PhotoGallery.Tests`
Result: Tests PASS ✓

### Step 5: Refactor (BLUE Phase)

*→ consult `solid-dry-principles` and `clean-architecture-review` for architecture validation.*

Improve code quality while keeping tests passing:

```csharp
public class YourService
{
    // Add validation, extract methods, improve design
    // Run tests after EACH change
}
```

Run after every change: `dotnet test PhotoGallery.Tests`
Result: Tests still PASS ✓

### Step 6: Validate Architecture

```
Consult: photogallery-architect-skill
Review for: SOLID principles, DRY patterns, PhotoGallery conventions
Approval: Architect signs off on design
```

### Step 7: Update Documentation

If design is NEW:
```
Update: Documentation/Architecture/DESIGN_DECISIONS.md
Add: What was decided, why, when, who approved it
Include: Implementation location and test files
```

### Step 8: Commit

```bash
git add PhotoGallery.Tests/YourFeatureTests.cs
git commit -m "test: Add failing tests for YourFeature (RED phase)"

git add PhotoGallery/Services/YourService.cs
git commit -m "feat: Implement YourFeature (GREEN phase)"

git add PhotoGallery/Services/YourService.cs
git commit -m "refactor: Improve YourFeature design (BLUE phase)"

git add Documentation/Architecture/DESIGN_DECISIONS.md
git commit -m "docs: Record design decision for YourFeature"
```

### Step 9: Verify Everything

Before marking as done:
```bash
# Run all tests
dotnet test PhotoGallery.Tests

# Result: All tests PASS ✓
# NO EXCEPTIONS. NO SHORTCUTS.
```

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

*→ consult `efcore-migration-safer` for the canonical migration workflow.*

> ⚠️ **Any time a model class, `DbContext` config, or entity property changes — even just an annotation — you MUST scaffold a new EF migration.** EF Core 9 makes `Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning` a runtime error, so `MigrateAsync()` will throw and `Program.cs` aborts startup if the snapshot drifts from the live model. The migration's `Up()`/`Down()` may be empty — what matters is the refreshed Designer.cs snapshot. PhotoGallery runs **two** providers (SQL Server + SqlServer); scaffold against **both** contexts whenever the shared model changes:
>
> ```bash
> # SQL Server (default local dev)
> dotnet ef migrations add <DescriptiveName> \
>   --context ApplicationDbContext \
>   --output-dir Data/Migrations
>
> # SqlServer (Azure-backed dev / production)
> dotnet ef migrations add <DescriptiveName>SqlServer \
>   --context ApplicationDbContext \
>   --output-dir Data/Migrations/SqlServer
> ```
>
> Note: migration class names must be unique across both contexts even though they live in different namespaces — the EF tool collides on bare class name. Suffix the SqlServer one with `SqlServer` (or any disambiguator).

```bash
dotnet ef migrations add InitialAlbumSchema --startup-project PhotoGallery
dotnet ef database update --startup-project PhotoGallery
```

**Program.cs Integration:**
```csharp
services.AddScoped<IAlbumRepository, AlbumRepository>();
services.AddScoped<IAlbumService, AlbumService>();

// Auto-migration — MUST fail-fast on error. A running container with an
// un-migrated database 500s every authenticated request because Identity
// tables are missing, and that failure is invisible if the exception is
// swallowed. Let it bubble out so ACA's startup probe marks the revision
// unhealthy and the deployment is flagged rather than silently broken.
try
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Database initialization failed. Aborting startup.");
    Log.CloseAndFlush();
    Environment.Exit(1);
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

*→ consult `aspnet-api-recipe` for the canonical controller/endpoint scaffolding workflow.*

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

## Cross-cutting plugin skills (always-on)

These copilot-dev-team meta-skills apply regardless of phase:

- `scratch-discipline` — temp/debug/troubleshoot files MUST go in `.copilot/scratch/<task-id>/`, never in the repo root or feature folders.
- `secret-hygiene` — never commit secrets, connection strings, or tokens. The plugin's `secret-scan` hook pre-checks writes.
- `commit-conventions` — follow the canonical commit-message format.
- `branch-strategy-u-prefix` — all work on `u/<actor>/<type>/<scope>` branches; **target `trial`**, never `main`/`master`. The only PR allowed into `main` is from `trial`. See `Documentation/Architecture/DESIGN_DECISIONS.md` D016 (release-driven deployment).
- `copilot-memory-update` — when a durable cross-session decision is made, update GitHub Copilot personal memory.
