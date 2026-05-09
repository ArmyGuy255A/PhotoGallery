# Clean Architecture Quick Reference

One-page cheat sheet for PhotoGallery developers. Consult SKILL.md for details.

## Layer Organization

```
PhotoGallery/
├── Domain/                  # ← No dependencies on anything else
│   ├── Entities/            # Album, Photo, AccessCode, PhotoVersion, User
│   ├── Interfaces/          # IRepository, IStorageProvider, IImageProcessor
│   ├── Specifications/      # AdminAlbumsSpec, ActiveAccessCodesSpec
│   └── Events/              # AlbumCreatedDomainEvent, PhotoUploadedDomainEvent
│
├── Infrastructure/          # ← Depends on Domain only
│   ├── Data/
│   │   ├── ApplicationDbContext.cs
│   │   ├── Configurations/  # Fluent API entity configs
│   │   └── Repositories/    # Concrete repository implementations
│   ├── Storage/
│   │   ├── MinioStorageProvider.cs
│   │   ├── AzureStorageProvider.cs
│   │   └── StorageFactory.cs
│   └── Services/
│       ├── ImageProcessingService.cs
│       ├── GoogleAuthService.cs
│       └── JwtTokenService.cs
│
└── Web/                     # ← Depends on both
    ├── Controllers/         # AlbumsController, PhotosController
    ├── Models/
    │   ├── Requests/
    │   └── Responses/
    └── Filters/
```

## Cross-Cutting Sub-Project Pattern

**Rule:** Cross-cutting infrastructure concerns live in their own `.csproj` sub-project (bare name, no `PhotoGallery.` prefix), not inline in the web app.

**Currently extracted:** `Authentication`, `Configuration`. **Future candidates:** `Storage`, `Email`, `Logging`. **Not cross-cutting:** Photos, Albums, AccessCodes, Users (those belong to PhotoGallery).

### Fixed Substructure

```
<Name>/                         # bare project name (e.g. Authentication)
├── <Name>.csproj               # <RootNamespace>=<Name>
├── DependencyInjection.cs      # public static AddXyzServices() ext method
├── Classes/                    # concrete types (validators, factories, DTOs)
├── Enums/                      # enum types
├── Helpers/                    # pure static utilities
├── Interfaces/                 # public-facing contracts
└── Services/                   # DI-registered services
```

### Adding a Cross-Cutting Concern (8 Steps)

```
1. dotnet new classlib -n <Name> -o <Name> --framework net9.0
2. Set <RootNamespace> and <AssemblyName> in csproj to bare name
3. Add internal substructure: Classes/, Enums/, Helpers/, Interfaces/, Services/
4. Add DependencyInjection.cs with public static AddXyzServices() ext method
5. dotnet sln PhotoGallery.sln add <Name>/<Name>.csproj
6. Add <ProjectReference> in PhotoGallery.csproj (and optionally PhotoGallery.Tests.csproj)
7. Update Dockerfile.backend with the new COPY for restore caching
8. Program.cs calls services.AddXyzServices() once — done
```

### Dependency Arrow

```
Configuration ← Authentication ← PhotoGallery ← PhotoGallery.Tests
```

Cross-cutting projects never reference back into PhotoGallery (compile-enforced).

## Code Patterns

### Repository Pattern
```csharp
// Domain Interface (no dependencies)
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

// Infrastructure Implementation
public class AlbumRepository : RepositoryBase<Album>, IRepository<Album>
{
    public AlbumRepository(ApplicationDbContext context) : base(context) { }
    
    public async Task<List<Album>> GetAdminAlbumsAsync(string adminId)
    {
        var spec = new AdminAlbumsSpecification(adminId);
        return await ApplySpecification(spec).ToListAsync();
    }
}

// Usage in Controller
[HttpGet]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> ListAlbums()
{
    var albums = await _albumRepository.ListAsync(new AdminAlbumsSpecification(userId));
    return Ok(albums.Select(a => new AlbumResponseDto(a)));
}
```

### Specification Pattern
```csharp
// Domain - reusable query logic
public class AdminAlbumsSpecification : Specification<Album>
{
    public AdminAlbumsSpecification(string adminId)
    {
        Query.Where(a => a.CreatedBy == adminId)
            .Include(a => a.Photos)
            .OrderByDescending(a => a.CreatedDate);
    }
}

// Usage - applies specification to repository
var adminAlbums = await _albumRepository.ListAsync(
    new AdminAlbumsSpecification(currentUserId));
```

### Domain Events
```csharp
// Domain - raise event when something happens
public class Album : Entity
{
    private List<Photo> _photos = new();
    
    public void AddPhoto(Photo photo)
    {
        _photos.Add(photo);
        DomainEvents.Add(new PhotoAddedDomainEvent(Id, photo.Id));
    }
}

// Infrastructure - handle event
public class PhotoAddedEventHandler : INotificationHandler<PhotoAddedDomainEvent>
{
    private readonly IImageProcessor _processor;
    
    public PhotoAddedEventHandler(IImageProcessor processor)
    {
        _processor = processor;
    }
    
    public async Task Handle(PhotoAddedDomainEvent @event, CancellationToken ct)
    {
        await _processor.QueueForProcessingAsync(@event.PhotoId);
    }
}
```

### Dependency Injection
```csharp
// Program.cs - wire up all services
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));

builder.Services.AddScoped<ApplicationDbContext>();

// Storage provider based on configuration
builder.Services.AddScoped<IStorageProvider>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    return config["Storage:Provider"] == "minio"
        ? new MinioStorageProvider(config)
        : new AzureStorageProvider(config);
});

builder.Services.AddMediatR(typeof(Program));  // Register event handlers
```

## Testing Example

```csharp
// Unit Test - Domain logic (no dependencies)
[TestClass]
public class AlbumTests
{
    [TestMethod]
    public void AddPhoto_RaisesDomainEvent()
    {
        var album = new Album("Test Album", "Description");
        var photo = new Photo("test.jpg", "storage-key");
        
        album.AddPhoto(photo);
        
        Assert.AreEqual(1, album.DomainEvents.Count);
        Assert.IsInstanceOfType(
            album.DomainEvents[0], 
            typeof(PhotoAddedDomainEvent));
    }
}

// Integration Test - Infrastructure
[TestClass]
public class AlbumRepositoryTests
{
    private ApplicationDbContext _context;
    private AlbumRepository _repository;
    
    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("test-db")
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new AlbumRepository(_context);
    }
    
    [TestMethod]
    public async Task AddAsync_PersistsAlbum()
    {
        var album = new Album("Test", "Desc");
        await _repository.AddAsync(album);
        await _repository.SaveChangesAsync();
        
        var retrieved = await _repository.GetByIdAsync(album.Id);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("Test", retrieved.Title);
    }
}
```

## Decision Tree

**Should this code go in Domain?**
- [ ] Does it implement a business rule? → YES: Domain
- [ ] Does it access the database? → NO: Domain
- [ ] Does it depend on external libraries (EF, HTTP)? → NO: Domain
- [ ] Could it be unit tested without mocking infrastructure? → YES: Domain

**Should this code go in Infrastructure?**
- [ ] Does it implement a domain interface? → YES: Infrastructure
- [ ] Does it access data or external services? → YES: Infrastructure
- [ ] Does it depend on frameworks (EF, HttpClient)? → YES: Infrastructure
- [ ] Is it a concrete implementation of abstraction? → YES: Infrastructure

**Should this code go in Presentation?**
- [ ] Is it an HTTP endpoint? → YES: Presentation
- [ ] Does it handle requests/responses? → YES: Presentation
- [ ] Is it model binding or validation? → YES: Presentation
- [ ] Does it return ActionResult or IActionResult? → YES: Presentation

## Common Mistakes to Avoid

| ❌ Mistake | ✅ Correct |
|-----------|-----------|
| Domain depends on EF Core | Domain defines interface, Infrastructure implements it |
| Business logic in Controller | Business logic in Domain, Controller delegates |
| Repository in Presentation | Repository in Infrastructure |
| Querying directly in Controller | Use Specification, delegate to Repository |
| Entity models as DTOs | Create separate DTO classes |
| Anemic entities (data only) | Rich entities with behavior and business rules |

## Layer Boundaries

```
           │ Presentation
           │ - Controllers
           │ - Request/Response DTOs
           │ - HTTP handling
           │
           ↓ (depends on)
           │
    Infrastructure
    - DbContext
    - Repositories
    - Storage services
    - External API clients
    │
    ├─ implements domain interfaces
    └─ depends on ↓
    
    Domain
    - Entities
    - Interfaces
    - Business logic
    - Domain events
    
    (NEVER depends on anything above it)
```

## Checklist for Code Review

- [ ] Domain layer has no external dependencies? (No using statements for EF, HTTP, etc.)
- [ ] Infrastructure implements domain interfaces? (IRepository, IStorageProvider)
- [ ] Business logic lives in domain, not controllers?
- [ ] Controllers delegate to services/repositories?
- [ ] DTOs are separate from entities?
- [ ] Each entity has meaningful behavior (not just properties)?
- [ ] Dependency injection configured in Program.cs?
- [ ] Tests can run without database for domain logic?
- [ ] Cross-cutting concerns live in their own sub-project (not inline in PhotoGallery/)?
- [ ] Sub-projects expose only via `AddXyzServices()` extension method?
- [ ] Typed `IOptions<ConfigurationSettings>` instead of `IConfiguration["..."]` reads in services?
- [ ] Cross-cutting projects don't reference back into PhotoGallery (compile-enforced)?

## Key Principles

1. **Dependency Inversion** - Depend on abstractions (interfaces), not concrete classes
2. **Single Responsibility** - Each class has one reason to change
3. **Open/Closed** - Open for extension (new providers), closed for modification
4. **Layered Separation** - Each layer handles specific concerns
5. **Testability** - Domain logic tested without infrastructure

---

For detailed patterns, examples, and anti-patterns, see **SKILL.md**.
