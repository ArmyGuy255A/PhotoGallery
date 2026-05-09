---
name: clean-architecture-guide
description: |
  Clean Architecture principles and patterns guide for PhotoGallery. This skill explains the layered architecture approach—Domain (core business logic), Infrastructure (data access, external services), and Presentation (API endpoints). Use this whenever designing PhotoGallery's overall structure, organizing code into layers, defining domain entities, creating repositories/specifications, or making architectural decisions about where business logic belongs. Covers dependency flows, domain-driven design patterns, and vertical slicing for feature organization. Referenced by yogo-architect skill for architectural compliance checks.
  
  This skill delegates to copilot-dev-team plugin meta-skills: `clean-architecture-review` (canonical Clean Architecture review checklist, layer rules, dependency direction), `folder-hygiene` (project layout), and `solid-dry-principles` (SOLID + DRY). Auto-trigger these when their conditions match. Plugin meta-skills are canonical — prefer them on conflict.
---

# Clean Architecture Guide for PhotoGallery

## Plugin Meta-Skills

This skill is the PhotoGallery-flavored layering guide; the canonical Clean Architecture rules live in the `copilot-dev-team` plugin's `clean-architecture-review` meta-skill. Defer to it on any conflict.

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| Reviewing layer placement / dependency direction | `clean-architecture-review` | — |
| Project folder structure questions | `folder-hygiene` | — |
| SOLID / DRY application within a layer | `solid-dry-principles` | — |
| Multi-implementation provider design | — | `provider-abstraction-pattern` |
| Splitting bounded contexts | — | `microservice-decomposition` |

## What is Clean Architecture?

Clean Architecture is a layered approach that organizes code to maximize **separation of concerns**, **testability**, and **framework independence**. It's also known as:
- **Hexagonal Architecture** (ports and adapters)
- **Onion Architecture** (concentric circles of dependency)
- **Ports and Adapters**

The core idea: **Dependencies point inward toward the domain**. The domain (core business logic) doesn't depend on infrastructure, frameworks, or external concerns.

## Why Clean Architecture for PhotoGallery?

✅ **Testable**: Domain logic has no framework dependencies, easy to unit test
✅ **Maintainable**: Clear separation makes code easier to understand and modify
✅ **Flexible**: Can swap implementations (Minio ↔ Azure, Google OAuth ↔ Facebook) without changing domain
✅ **Scalable**: Features organized vertically, making team scaling easier
✅ **Framework Independent**: Business logic doesn't depend on ASP.NET, EF, or libraries

**Referenced by:** PhotoGallery Architect Skill (validates SOLID/DRY compliance within this structure)

## The Three Layers

### Layer 1: Domain (Innermost - No Dependencies)
**The core business logic - knows nothing about frameworks, databases, or HTTP**

**Contains:**
- Entities (Album, Photo, AccessCode, etc.)
- Aggregates (groups of related entities)
- Value Objects (immutable objects like Money, Percentage)
- Domain Events (things that happened: "AlbumCreated", "PhotoUploaded")
- Specifications (reusable business rules queries)
- Interfaces (contracts that will be implemented outside the domain)

**Key Rule:** Domain **cannot depend** on any other layer

**Example:**
```csharp
// Domain Layer - No EF, no HTTP, no ASP.NET
public class Album
{
    public int Id { get; private set; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public DateTime CreatedDate { get; private set; }
    
    private List<Photo> _photos = new();
    public IReadOnlyList<Photo> Photos => _photos.AsReadOnly();
    
    public Album(string title, string description)
    {
        Title = title;
        Description = description;
        CreatedDate = DateTime.UtcNow;
    }
    
    public void AddPhoto(Photo photo)
    {
        _photos.Add(photo);
        // Raise domain event
        DomainEvents.Add(new PhotoAddedToDomainEvent(Id, photo.Id));
    }
}

// Specification - reusable query logic
public class AdminAlbumsSpecification : Specification<Album>
{
    public AdminAlbumsSpecification(string adminId)
    {
        Query.Where(a => a.CreatedBy == adminId);
    }
}

// Domain Event - something that happened
public class PhotoUploadedDomainEvent
{
    public int PhotoId { get; set; }
    public DateTime UploadedDate { get; set; }
}
```

### Layer 2: Infrastructure (Middle - Depends on Domain)
**Implements the interfaces defined in Domain. Handles concrete implementations for data, external services, file storage, etc.**

**Contains:**
- **Data Access**: EF Core DbContext, entity configurations, repositories
- **External Services**: Storage providers (Minio, Azure), email services, logging
- **Service Implementations**: Concrete implementations of domain interfaces
- **DTOs/Models**: Mapping between domain and infrastructure

**Key Rule:** Infrastructure **can depend on** Domain, but Domain **never** depends on Infrastructure

**Dependency Injection Configuration:** All infrastructure services registered here

**Example:**
```csharp
// Infrastructure Layer
public class AlbumRepository : RepositoryBase<Album>, IRepository<Album>
{
    private readonly ApplicationDbContext _context;
    
    public AlbumRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }
    
    public async Task<List<Album>> GetAdminAlbumsAsync(string adminId)
    {
        var spec = new AdminAlbumsSpecification(adminId);
        return await ApplySpecification(spec).ToListAsync();
    }
}

// Storage Service Implementation
public class MinioStorageProvider : IStorageProvider
{
    private readonly IMinioClient _minio;
    
    public MinioStorageProvider(IMinioClient minio)
    {
        _minio = minio;
    }
    
    public async Task<string> UploadAsync(Stream file, string key)
    {
        // Minio implementation details
        await _minio.PutObjectAsync(/* ... */);
        return key;
    }
}

// EF Core Configuration (Infrastructure)
public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasMany(x => x.Photos)
            .WithOne(x => x.Album)
            .HasForeignKey(x => x.AlbumId);
        
        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);
    }
}
```

### Layer 3: Presentation (Outermost - Depends on Both)
**API endpoints, controllers, Razor pages - receives HTTP requests and delegates to domain/infrastructure**

**Contains:**
- **Endpoints** (Ardalis.ApiEndpoints or Controllers)
- **Request DTOs** (input models for endpoints)
- **Response DTOs** (output models for endpoints)
- **Model Binding & Validation** (ASP.NET Core)
- **HTTP Routing & Status Codes**

**Key Rule:** Presentation **can depend on** Domain and Infrastructure, but domain logic should not live here

**Example:**
```csharp
// Presentation Layer - API Endpoint
[ApiController]
[Route("api/albums")]
public class AlbumsController : ControllerBase
{
    private readonly IRepository<Album> _albumRepository;
    private readonly IStorageProvider _storage;
    
    public AlbumsController(IRepository<Album> albumRepository, IStorageProvider storage)
    {
        _albumRepository = albumRepository;
        _storage = storage;
    }
    
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AlbumResponseDto>> CreateAlbum(CreateAlbumRequest request)
    {
        // Create domain entity (business logic)
        var album = new Album(request.Title, request.Description);
        
        // Persist (infrastructure)
        await _albumRepository.AddAsync(album);
        await _albumRepository.SaveChangesAsync();
        
        // Return response
        return Ok(new AlbumResponseDto { Id = album.Id, Title = album.Title });
    }
}

// Request DTO - no business logic
public class CreateAlbumRequest
{
    public string Title { get; set; }
    public string Description { get; set; }
}

// Response DTO - no business logic
public class AlbumResponseDto
{
    public int Id { get; set; }
    public string Title { get; set; }
}
```

## Dependency Flow (Golden Rule)

*→ consult `clean-architecture-review` for canonical layer placement and dependency direction validation*

```
Presentation Layer
      ↓ depends on ↓
Infrastructure Layer ←→ Domain Layer (implements interfaces)
```

**Key:** Domain never points upward. Domain only points inward (to itself).

```
❌ WRONG: Domain depending on Infrastructure
Domain → Infrastructure → Database
(Domain is now tightly coupled!)

✅ CORRECT: Infrastructure depends on Domain
Presentation → Infrastructure → Domain
Infrastructure implements Domain interfaces
```

## Cross-Cutting Concerns Live in Sub-Projects

*→ consult `folder-hygiene` for project-layout enforcement; consult `clean-architecture-review` for dependency-direction validation across project boundaries*

### The Rule

**Cross-cutting infrastructure concerns live in their own `.csproj` sub-project, not inline in the main web app project.**

A *cross-cutting concern* is reusable infrastructure that doesn't belong to any single feature or domain — it's used by many features and has no opinion about what those features do. Compare to *domain concerns* (Photos, Albums, AccessCodes, Users), which belong to PhotoGallery itself.

This mirrors the pattern adopted by VerdantIQ and was rolled into PhotoGallery alongside the `Authentication` and `Configuration` sub-projects.

### What Counts as Cross-Cutting

| Concern | Status | Project |
| --- | --- | --- |
| Authentication (token validators, JWT issuance) | ✅ Extracted | `Authentication.csproj` |
| Configuration (typed settings POCO + `IOptions` binding) | ✅ Extracted | `Configuration.csproj` |
| Storage (`IStorageProvider`, MinIO + Azure impls) | 🔄 Future candidate | currently inline in `PhotoGallery/Infrastructure/Storage/` |
| Email (`IEmailService`, Mock + Azure Communication Services impls) | 🔄 Future candidate | currently inline |
| Logging (Serilog wiring) | 🔄 Future candidate | currently inline |

**Domain concerns are NOT cross-cutting.** Photos, Albums, AccessCodes, and Users belong to the PhotoGallery web app. Eventually we may extract them into a `Domain.csproj`, but that is a *separate* decision and out of scope for this rule.

### The Five Rules

1. **Bare project names.** Project named after the concern, with no `PhotoGallery.` prefix. Examples: `Authentication`, `Configuration`, future `Storage`, `Email`. Matches VerdantIQ. Set `<RootNamespace>` and `<AssemblyName>` in the csproj to the bare name.

2. **Fixed substructure.** Each cross-cutting project uses these directories where applicable:
   - `Classes/` — concrete types (validators, factories, DTOs)
   - `Enums/` — enum types
   - `Helpers/` — pure static utilities
   - `Interfaces/` — public-facing contracts
   - `Services/` — DI-registered services
   - `DependencyInjection.cs` (file at project root) — exposes ONE `AddXyzServices()` extension method

3. **Single registration entry point.** The web app's `Program.cs` calls only `services.AddXyzServices()`. Never wire individual services from a cross-cutting project inline in `Program.cs`.

4. **No back-references.** Cross-cutting projects MAY depend on each other (e.g., `Authentication` references `Configuration`) but **MUST NOT** depend on the web app project. The arrow is one-way and compile-enforced.

5. **Typed configuration.** Inside services, prefer `IOptions<ConfigurationSettings>` (from the `Configuration` project) over `IConfiguration["..."]` magic strings. The web app may still use `IConfiguration` directly during startup — it's available on `WebApplicationBuilder` — but service classes should take typed options.

### Fixed Substructure (Example: `Authentication/`)

```
Authentication/                          # bare project name, no prefix
├── Authentication.csproj                # <RootNamespace>Authentication</RootNamespace>
├── DependencyInjection.cs               # public static AddAuthenticationServices(this IServiceCollection)
├── Classes/
│   ├── JwtTokenValidator.cs
│   └── GoogleTokenValidator.cs
├── Enums/
│   └── TokenSource.cs
├── Helpers/
│   └── ClaimsPrincipalExtensions.cs
├── Interfaces/
│   ├── ITokenIssuer.cs
│   └── ITokenValidator.cs
└── Services/
    ├── JwtTokenIssuer.cs
    └── GoogleAuthService.cs
```

`DependencyInjection.cs` is the **only** public entry point the web app touches:

```csharp
// Authentication/DependencyInjection.cs
namespace Authentication;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthenticationServices(this IServiceCollection services)
    {
        services.AddScoped<ITokenValidator, JwtTokenValidator>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<GoogleAuthService>();
        return services;
    }
}
```

```csharp
// PhotoGallery/Program.cs
builder.Services
    .AddConfigurationServices(builder.Configuration)
    .AddAuthenticationServices();
// ↑ that's it. No individual auth services wired inline.
```

### Dependency Graph

```
Configuration                ← depends on nothing (root of the graph)
     ↑
Authentication               ← may depend on Configuration
     ↑
PhotoGallery (web app)       ← references both
     ↑
PhotoGallery.Tests           ← references PhotoGallery (and the cross-cutting projects transitively)
```

The arrow points **toward** the dependency. Cross-cutting projects never point at PhotoGallery — that's the whole point. If you find yourself wanting `Authentication` to call into a PhotoGallery type, the type belongs in a cross-cutting project (or a future `Domain.csproj`), not in the web app.

### How to Add a New Cross-Cutting Concern (8-Step Recipe)

1. `dotnet new classlib -n <Name> -o <Name> --framework net9.0`
2. Set `<RootNamespace>` and `<AssemblyName>` in the csproj to the **bare** name (no `PhotoGallery.` prefix).
3. Add internal substructure: `Classes/`, `Enums/`, `Helpers/`, `Interfaces/`, `Services/` (omit any directory you don't yet need).
4. Add `DependencyInjection.cs` at the project root with a `public static IServiceCollection AddXyzServices(this IServiceCollection)` extension method.
5. `dotnet sln PhotoGallery.sln add <Name>/<Name>.csproj`
6. Add a `<ProjectReference>` to `PhotoGallery.csproj` (and `PhotoGallery.Tests.csproj` if tests need it).
7. Update `Dockerfile.backend` with the new `COPY <Name>/<Name>.csproj <Name>/` line so layer caching works during `dotnet restore`.
8. In `Program.cs`, call `services.AddXyzServices()` once. Done.

### Anti-Patterns to Avoid

| ❌ Anti-pattern | ✅ Do this instead |
| --- | --- |
| Putting JWT validation in `PhotoGallery/Services/AuthService.cs` | Put it in `Authentication/Services/` — compile-enforces no leakage |
| `PhotoGallery.Authentication` as the project name | Bare `Authentication` (matches VerdantIQ) |
| `Program.cs` calls `services.AddScoped<ITokenIssuer, JwtTokenIssuer>()` | `Program.cs` calls `services.AddAuthenticationServices()` only |
| `Authentication` references `PhotoGallery` "just to grab a User type" | The User type belongs in a cross-cutting or domain project, not the web app |
| Service reads `_config["Jwt:Issuer"]` via `IConfiguration` | Service takes `IOptions<ConfigurationSettings>` from `Configuration.csproj` |
| Mixing `Configuration/` (cross-cutting project) with `appsettings.json` (web-app file) in conversation | They are different things: the project owns *types*; `appsettings.json` owns *values* |

> **Why "bare names" matter.** When you read `using Authentication;` in a service, it reads as a concept ("this service uses authentication"). When you read `using PhotoGallery.Authentication;`, the namespace is asserting auth is *part of* PhotoGallery — which is exactly the coupling we're trying to break. Bare names make the cross-cutting nature explicit.

## How to Organize Code

*→ consult `folder-hygiene` for project structure validation and folder naming standards*

### Option 1: Layered Organization (Traditional)
```
PhotoGallery/
├── Domain/
│   ├── Entities/
│   │   ├── Album.cs
│   │   ├── Photo.cs
│   │   └── AccessCode.cs
│   ├── Interfaces/
│   │   ├── IRepository.cs
│   │   ├── IStorageProvider.cs
│   │   └── IImageProcessor.cs
│   └── Specifications/
│       ├── AdminAlbumsSpecification.cs
│       └── ExpiredAccessCodesSpecification.cs
├── Infrastructure/
│   ├── Data/
│   │   ├── ApplicationDbContext.cs
│   │   ├── Configurations/
│   │   └── Repositories/
│   ├── Storage/
│   │   ├── MinioStorageProvider.cs
│   │   └── AzureStorageProvider.cs
│   └── Services/
│       ├── ImageProcessingService.cs
│       └── EmailService.cs
└── Web/
    ├── Controllers/
    │   ├── AlbumsController.cs
    │   ├── PhotosController.cs
    │   └── AccessCodesController.cs
    ├── Models/
    │   ├── CreateAlbumRequest.cs
    │   └── AlbumResponseDto.cs
    └── Program.cs
```

### Option 2: Vertical Slice Organization (Feature-Based)
```
PhotoGallery/
├── Domain/
│   ├── SharedEntities/
│   │   └── User.cs
│   └── Interfaces/
│       ├── IRepository.cs
│       ├── IStorageProvider.cs
│       └── IImageProcessor.cs
├── Features/
│   ├── Albums/
│   │   ├── Domain/
│   │   │   ├── Album.cs
│   │   │   └── AlbumsSpecification.cs
│   │   ├── Infrastructure/
│   │   │   ├── AlbumRepository.cs
│   │   │   └── AlbumConfiguration.cs
│   │   └── Endpoints/
│   │       ├── CreateAlbum.cs
│   │       ├── ListAlbums.cs
│   │       └── GetAlbumDetail.cs
│   ├── Photos/
│   │   ├── Domain/
│   │   ├── Infrastructure/
│   │   └── Endpoints/
│   └── AccessCodes/
│       ├── Domain/
│       ├── Infrastructure/
│       └── Endpoints/
├── Infrastructure/
│   ├── Data/
│   ├── Storage/
│   └── Services/
└── Program.cs
```

### PhotoGallery's Recommended: Layered Structure
**Recommended for Phase 2+ implementation:**

```
PhotoGallery/
├── Domain/                           # Layer 1: Core business logic
│   ├── Entities/
│   │   ├── Album.cs
│   │   ├── Photo.cs
│   │   ├── PhotoVersion.cs
│   │   ├── AccessCode.cs
│   │   └── User.cs
│   ├── Interfaces/
│   │   ├── IRepository.cs
│   │   ├── IStorageProvider.cs
│   │   ├── IImageProcessor.cs
│   │   ├── IAuthService.cs
│   │   └── INotificationService.cs
│   ├── Specifications/
│   │   ├── ActiveAccessCodesSpec.cs
│   │   └── AdminAlbumsSpec.cs
│   └── Events/
│       ├── AlbumCreatedDomainEvent.cs
│       ├── PhotoUploadedDomainEvent.cs
│       └── AccessCodeExpiredDomainEvent.cs
│
├── Infrastructure/                  # Layer 2: Implementations
│   ├── Data/
│   │   ├── ApplicationDbContext.cs
│   │   ├── Migrations/
│   │   ├── Configurations/
│   │   │   ├── AlbumConfig.cs
│   │   │   ├── PhotoConfig.cs
│   │   │   └── AccessCodeConfig.cs
│   │   └── Repositories/
│   │       ├── RepositoryBase.cs
│   │       ├── AlbumRepository.cs
│   │       └── AccessCodeRepository.cs
│   ├── Storage/
│   │   ├── MinioStorageProvider.cs
│   │   ├── AzureStorageProvider.cs
│   │   └── StorageFactory.cs
│   ├── ImageProcessing/
│   │   └── ImageProcessingService.cs
│   ├── Authentication/
│   │   ├── GoogleAuthService.cs
│   │   └── JwtTokenService.cs
│   └── Notifications/
│       └── EmailNotificationService.cs
│
├── Controllers/                     # Layer 3: Presentation
│   ├── AlbumsController.cs
│   ├── PhotosController.cs
│   ├── AccessCodesController.cs
│   └── AuthController.cs
├── Models/
│   ├── Requests/
│   │   ├── CreateAlbumRequest.cs
│   │   └── CreateAccessCodeRequest.cs
│   └── Responses/
│       ├── AlbumResponseDto.cs
│       └── PhotoResponseDto.cs
├── Filters/
│   └── ExceptionHandlingFilter.cs
└── Program.cs
```

## Core Patterns

### 1. Repository Pattern
**Purpose:** Abstracts data access, allows changing database without changing domain

```csharp
// Domain interface (no dependencies)
public interface IRepository<T> where T : Entity
{
    Task<T> GetByIdAsync(int id);
    Task<IReadOnlyList<T>> ListAsync();
    Task<IReadOnlyList<T>> ListAsync(Specification<T> spec);
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task SaveChangesAsync();
}

// Infrastructure implementation
public class RepositoryBase<T> : IRepository<T> where T : Entity
{
    protected readonly ApplicationDbContext _context;
    
    protected RepositoryBase(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<T> GetByIdAsync(int id)
        => await _context.Set<T>().FindAsync(id);
    
    // ... other implementations
}
```

### 2. Specification Pattern
**Purpose:** Encapsulate complex query logic in domain-friendly way

```csharp
// Domain-focused
public class ActiveAccessCodesSpecification : Specification<AccessCode>
{
    public ActiveAccessCodesSpecification()
    {
        Query.Where(ac => ac.ExpirationDate == null || ac.ExpirationDate > DateTime.UtcNow)
            .Include(ac => ac.Album);
    }
}

// Usage in infrastructure
var spec = new ActiveAccessCodesSpecification();
var activeCodes = await _repository.ListAsync(spec);
```

### 3. Domain Events
**Purpose:** Decouple business logic from event handlers

```csharp
// Domain
public class Album : Entity
{
    public void AddPhoto(Photo photo)
    {
        _photos.Add(photo);
        // Raise event - something happened
        DomainEvents.Add(new PhotoAddedDomainEvent(Id, photo.Id));
    }
}

// Handler in infrastructure (subscribes to events)
public class PhotoAddedEventHandler : INotificationHandler<PhotoAddedDomainEvent>
{
    private readonly IImageProcessor _processor;
    
    public async Task Handle(PhotoAddedDomainEvent @event, CancellationToken ct)
    {
        // Process image when photo is added
        await _processor.QueueForProcessingAsync(@event.PhotoId);
    }
}
```

### 4. Dependency Injection
**Program.cs wires everything together:**

```csharp
// Domain services (no dependencies)
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));

// Infrastructure
builder.Services.AddScoped<ApplicationDbContext>();
builder.Services.AddScoped<IStorageProvider>(provider =>
    configuration["Storage:Provider"] == "minio"
        ? new MinioStorageProvider(config)
        : new AzureStorageProvider(config));

// Presentation
builder.Services.AddControllers();

// Mediator (optional, for handlers)
builder.Services.AddMediatR(typeof(Program));
```

## Testing Implications

### Unit Testing (Domain)
```csharp
[TestClass]
public class AlbumTests
{
    [TestMethod]
    public void AddPhoto_IncreasesPhotoCount()
    {
        // Arrange
        var album = new Album("Test", "Description");
        var photo = new Photo("test.jpg", "bucket/test.jpg");
        
        // Act
        album.AddPhoto(photo);
        
        // Assert
        Assert.AreEqual(1, album.Photos.Count);
    }
}
```

### Integration Testing (Infrastructure + Domain)
```csharp
[TestClass]
public class AlbumRepositoryTests
{
    private ApplicationDbContext _context;
    private AlbumRepository _repository;
    
    [TestInitialize]
    public void Setup()
    {
        _context = new ApplicationDbContext(new DbContextOptionsBuilder()
            .UseInMemoryDatabase("test").Options);
        _repository = new AlbumRepository(_context);
    }
    
    [TestMethod]
    public async Task AddAsync_PersistsAlbum()
    {
        // Arrange
        var album = new Album("Test", "Description");
        
        // Act
        await _repository.AddAsync(album);
        await _repository.SaveChangesAsync();
        
        // Assert
        var retrieved = await _repository.GetByIdAsync(album.Id);
        Assert.IsNotNull(retrieved);
    }
}
```

## Anti-Patterns to Avoid

*→ consult `solid-dry-principles` for enforcing single responsibility, DRY violations, and SOLID compliance*

### ❌ Domain Depending on Infrastructure
```csharp
// BAD: Domain knows about EF Core
public class Album
{
    public void Save(ApplicationDbContext context)
    {
        context.Albums.Add(this);
        context.SaveChanges();
    }
}
```

### ❌ Infrastructure Logic in Domain
```csharp
// BAD: Business logic in presentation layer
[HttpPost]
public async Task CreateAlbum(CreateAlbumRequest request)
{
    var album = new Album(request.Title, request.Description);
    
    // Business logic here - WRONG!
    if (request.Title.Length > 200)
        return BadRequest();
    
    _context.Albums.Add(album);
    await _context.SaveChangesAsync();
}
```

### ❌ Anemic Domain (No Business Logic)
```csharp
// BAD: Entity is just a data container
public class Album
{
    public int Id { get; set; }
    public string Title { get; set; }
    // ... getters/setters only, no behavior
}
```

**CORRECT:** Entities should encapsulate behavior:
```csharp
// GOOD: Rich domain model
public class Album
{
    public int Id { get; private set; }
    public string Title { get; private set; }
    
    private List<Photo> _photos = new();
    public IReadOnlyList<Photo> Photos => _photos.AsReadOnly();
    
    public void AddPhoto(Photo photo)
    {
        if (photo == null) throw new ArgumentNullException(nameof(photo));
        _photos.Add(photo);
        DomainEvents.Add(new PhotoAddedDomainEvent(Id, photo.Id));
    }
}
```

## Decision Points for PhotoGallery

### Where Does Business Logic Live?

**Domain:**
- Validating album title length
- Checking if user can create album (authorization)
- Calculating photo quality based on settings
- Enforcing access code expiration

**Infrastructure:**
- How to store files (Minio vs Azure)
- How to access the database (EF Core)
- How to compress images (ImageSharp)
- How to send notifications

**Presentation:**
- HTTP routing and status codes
- Request/response serialization
- Model binding and ASP.NET validation

### When to Use Specifications?
Use when: Query logic is complex or reused

```csharp
// Good: Reusable, testable
public class AdminAlbumsSpecification : Specification<Album>
{
    public AdminAlbumsSpecification(string adminId)
    {
        Query.Where(a => a.CreatedBy == adminId)
            .Include(a => a.Photos);
    }
}

// Less good: Simple, one-off query
var album = await _repo.GetByIdAsync(1); // Just use GetByIdAsync
```

### When to Use Domain Events?
Use when: You want to decouple logic

```csharp
// Instead of:
album.AddPhoto(photo);
await _imageProcessor.QueueAsync(photo.Id);  // Tightly coupled

// Better:
album.AddPhoto(photo);  // Domain event raised internally
// Handler subscribes: when PhotoAddedDomainEvent occurs → queue image

// Benefit: Album doesn't need to know about image processing
```

## PhotoGallery's Clean Architecture Benefits

✅ **Testable**: Domain logic (business rules) tested without database
✅ **Flexible**: Swap Minio → Azure, Google → Facebook without domain changes
✅ **Maintainable**: Clear separation makes code easier to understand
✅ **Scalable**: Features can be developed independently
✅ **Future-Proof**: New features added to new layers, existing code untouched

## Checklist for Clean Architecture Compliance

- [ ] Domain has no external dependencies (no EF, no ASP.NET, no HTTP)
- [ ] Infrastructure implements domain interfaces
- [ ] Presentation delegates to services/repositories
- [ ] Business logic is in domain, not controllers
- [ ] Dependency injection configures all services in Program.cs
- [ ] Tests can verify domain logic without database
- [ ] Each layer has a single, clear responsibility

---

**Key Takeaway:** Clean Architecture is about creating code that's independent of frameworks, testable, and focused on the domain. Dependencies point inward. The domain is the innermost circle that knows nothing about infrastructure or frameworks.
