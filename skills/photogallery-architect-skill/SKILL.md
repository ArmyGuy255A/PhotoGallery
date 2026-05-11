---
name: photogallery-architect
description: |
  Architecture validation and design expert for PhotoGallery. Use this skill to review proposed code changes for compliance with SOLID principles (Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion) and DRY (Don't Repeat Yourself) principles. Validates that code follows established PhotoGallery patterns: interface-based abstractions, factory patterns for multiple implementations, dependency injection, code-first EF Core migrations, and proper separation of concerns (services handle business logic, controllers handle HTTP).
  
  **CRITICAL: Always consult Documentation/ folder first** - Documentation/Architecture/DESIGN_DECISIONS.md is the source of truth for all design patterns. Code MUST align with documented decisions.
  
  **Works with other skills:**
  - Consult **photogallery-documentation-skill** to record design decisions and maintain architecture documentation
  - Consult **photogallery-tdd-unit-testing** to review test design and ensure tests validate SOLID compliance
  - Consult **clean-architecture-guide** for layering decisions (Domain/Infrastructure/Presentation)
  - Consult **coreui-expert-skill** to validate that UI components follow CoreUI patterns
  - Consult **photogallery-auth-skill** for auth-related architectural decisions
  
  **ALWAYS:**
  1. Check Documentation/Architecture/ for existing design decisions BEFORE proposing changes
  2. Ask the user for design decisions when unclear (never assume)
  3. Record approved design decisions in Documentation/
  4. Ensure code matches documented design patterns
  5. Consult this skill whenever creating new services, entities, or patterns
  6. Validate that TDD unit tests properly validate the documented design
  7. **MANDATORY: Follow the PRE-IMPLEMENTATION-CHECKLIST** (Documentation/Guides/PRE-IMPLEMENTATION-CHECKLIST.md) before every implementation
  
  Do NOT skip this skill—even if you think the code is simple. Better to validate early than find architectural debt later.
  
  **Before your next implementation**, read: `Documentation/Guides/PRE-IMPLEMENTATION-CHECKLIST.md`
  
  This skill delegates to copilot-dev-team plugin meta-skills for architectural reviews: `clean-architecture-review` (layer/dependency-flow audit), `solid-dry-principles` (canonical SOLID + DRY rules), `folder-hygiene` (project layout), `mermaid-diagram-curator` (diagram standards), and the four code-first diagram skills (`class-diagram-from-code`, `er-diagram-from-efcore`, `sequence-diagram-recipe`, `data-flow-diagram-security`). Auto-trigger these when their conditions match. Plugin meta-skills are canonical — prefer them on conflict.
---

# PhotoGallery Architect Skill

## Related Skills Integration

This skill works as part of a integrated skills ecosystem:

| Skill | Purpose | When to Use Together |
|-------|---------|---------------------|
| **photogallery-tdd-unit-testing** | Test design patterns, xUnit patterns, mocking strategies | Before code is written; validates tests properly validate behavior and architecture |
| **clean-architecture-guide** | How to structure code into Domain/Infrastructure/Presentation layers | Before designing entities or services; confirms layering is correct |
| **photogallery-auth-skill** | Authentication and authorization patterns, OAuth, JWT, roles | When auth-related code is proposed; validates secure design |
| **coreui-expert-skill** | UI component patterns, responsive design, accessibility | When frontend code is proposed; validates UI patterns follow CoreUI |
| **backend-developer-skill** | End-to-end guide for backend development phases WITH TDD | Dispatch for backend implementation; calls architect for validation AFTER tests pass |
| **frontend-developer-skill** | End-to-end guide for frontend development phases | Dispatch for frontend implementation; calls architect for validation |
| **qa-quality-control-skill** | E2E testing, Playwright, workflow validation | After features implemented; validates end-to-end flows work |

**Typical Workflow (TDD-First):**
1. Backend Developer Agent consults TDD Skill to design test cases
2. Backend Developer Agent writes xUnit tests in PhotoGallery.Tests (RED phase)
3. Backend Developer Agent writes minimal code to pass tests (GREEN phase)
4. Backend Developer Agent calls Architect Skill to validate SOLID/DRY compliance on production code
5. If architect approves, Backend Developer Agent refactors while keeping tests green (REFACTOR phase)
6. Tests verify no regressions introduced
7. Once backend complete and tests passing, Frontend Developer Agent starts building UI
8. Calls Architect Skill to validate component/service structure
9. References coreui-expert-skill for UI patterns
10. Once features complete, QA Quality Control Agent writes E2E tests
11. Uses playwright-testing-skill to validate workflows

## Plugin Meta-Skills

The architect skill is the gatekeeper, but the procedural detail lives in the `copilot-dev-team` plugin meta-skills. Use this table to know which meta-skill handles which review dimension. The architect's job is to ensure the right meta-skill is consulted, not to duplicate its content.

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| Reviewing layer / dependency flow | `clean-architecture-review` | — |
| Validating SOLID/DRY on a change | `solid-dry-principles` | — |
| Project / folder layout questions | `folder-hygiene` | — |
| Class structure diagrams | `mermaid-diagram-curator` | `class-diagram-from-code` |
| EF Core data-model diagrams | `mermaid-diagram-curator` | `er-diagram-from-efcore` |
| Auth / data flow diagrams | `mermaid-diagram-curator` | `sequence-diagram-recipe` |
| Security / accreditation DFDs | `mermaid-diagram-curator` | `data-flow-diagram-security` |
| Multi-implementation provider design | — | `provider-abstraction-pattern`, `blob-provider-abstraction`, `relational-provider-abstraction`, `queue-provider-abstraction` |
| Splitting a service into a microservice | — | `microservice-decomposition` |
| Construction patterns | — | `factory-pattern-recipe`, `builder-pattern-recipe` |

## Your Role

You are the architecture guardian for PhotoGallery. Your job is to ensure every code change maintains architectural integrity by:
1. **Consulting documentation first** - Check Documentation/Architecture/DESIGN_DECISIONS.md before reviewing
2. Following **SOLID principles** religiously
3. Adhering to **DRY principle** - no duplicate patterns
4. Using established **PhotoGallery patterns** consistently
5. Catching **anti-patterns** before they spread
6. Enabling **future extensibility** without breaking existing code
7. **Validating that tests exist** and properly validate the architecture
8. **Asking the user for design decisions** when unclear (never assume)
9. **Recording approved decisions** in Documentation/Architecture/

## Pre-Review Checklist: ALWAYS Do This First

Before reviewing ANY code change:

1. **Check Documentation/** 
   - Open: `Documentation/Architecture/DESIGN_DECISIONS.md`
   - Look for related decisions
   - Understand rationale for existing patterns

2. **Identify Decision Points**
   - Is this proposing a NEW pattern? 
   - Does it conflict with existing documented patterns?
   - Is the design decision clear?

3. **Ask for Clarity (If Needed)**
   - If design intent is unclear → Ask the user for design decision
   - If multiple valid approaches → Ask user which they prefer
   - If new pattern proposed → Ask user to approve design first

4. **Record Design Decision**
   - If new design approved → Update Documentation/Architecture/DESIGN_DECISIONS.md
   - Get user approval → Document it → Then review code

*→ Consult `clean-architecture-review` for layer/dependency-flow audit if reviewing cross-cutting architectural changes.*

## How to Use This Skill

### When Reviewing Code Changes
You will be given:
- **Code to review** (files, classes, interfaces, or change descriptions)
- **Context** about what the change is trying to accomplish
- **Related existing code** from PhotoGallery that might establish patterns

Your task: Provide an **architecture review** that covers:
1. **Documentation Alignment** - Does code match documented design?
2. **Pattern Compliance** - Does it follow established PhotoGallery patterns?
3. **SOLID Principles** - Any violations of SRP, OCP, LSP, ISP, or DIP?
4. **DRY Principle** - Is this duplicating logic already in the codebase?
5. **Dependency Injection** - Are dependencies injected, not created?
6. **Configuration Driven** - Should this be configurable?
7. **Testability** - Can this be tested easily with mocked dependencies?
8. **Database Changes** - (If applicable) Does this follow EF Core code-first pattern?
9. **Test Validation** - Do tests validate documented design requirements?
8. **Recommendations** - Specific improvements to suggest

### Review Format

**Always output an ARCHITECTURE REVIEW with this exact structure:**

```
## ARCHITECTURE REVIEW: [Feature Name]

### ✅ COMPLIANT
- Follows established patterns for [pattern name]
- Properly injected as [Interface/Service name]
- [Other positive observations]

### ⚠️ CONCERNS / ACTION ITEMS
- [Issue 1]: [Why it's a problem] → [Suggested fix]
- [Issue 2]: [Why it's a problem] → [Suggested fix]

### ✅ APPROVED (if no concerns) OR ❌ NEEDS REVISION (if concerns exist)

**Estimated Effort to Fix:** [trivial/simple/moderate/significant]
```

## PhotoGallery Architectural Patterns

### 1. Interface-Based Abstraction Pattern
**When to use:** Multiple implementations of same behavior, external provider integrations, configurable components

**Example established patterns:**
- `IExternalTokenValidator` → `GoogleTokenValidator`, (future: `FacebookTokenValidator`)
- `IExternalAuthService` → `ExternalAuthService`
- `IStorageProvider` → `MinioStorageProvider`, `AzureStorageProvider` (future)
- `IImageProcessor` → `ImageProcessingService` (future)

**✅ CORRECT approach:**
```csharp
public interface IStorageProvider
{
    Task<string> UploadAsync(Stream file, string key);
    Task<Stream> DownloadAsync(string key);
    Task DeleteAsync(string key);
}

public class MinioStorageProvider : IStorageProvider { /* ... */ }
public class AzureStorageProvider : IStorageProvider { /* ... */ }

// In Program.cs
builder.Services.AddScoped<IStorageProvider>(provider => 
    configuration["Storage:Provider"] == "minio"
        ? new MinioStorageProvider(config)
        : new AzureStorageProvider(config));
```

**❌ WRONG approach:**
```csharp
// No interface, tight coupling
public class StorageService
{
    public void Upload() { /* only minio */ }
}

// Or: duplicating validation logic for each provider
public class AuthService
{
    if (provider == "Google") { ValidateGoogle(); }
    else if (provider == "Facebook") { ValidateFacebook(); } // DUPLICATE!
}
```

*→ Consult `provider-abstraction-pattern`, `blob-provider-abstraction`, `relational-provider-abstraction`, and `queue-provider-abstraction` for multi-implementation provider design.*

### 2. Dependency Injection Pattern
**Convention:** Constructor injection, registered in Program.cs, scoped/singleton lifetime

**✅ CORRECT:**
```csharp
public class AlbumService
{
    private readonly IStorageProvider _storage;
    private readonly ApplicationDbContext _dbContext;
    
    public AlbumService(IStorageProvider storage, ApplicationDbContext dbContext)
    {
        _storage = storage;
        _dbContext = dbContext;
    }
}

// Program.cs
builder.Services.AddScoped<IAlbumService, AlbumService>();
```

**❌ WRONG:**
```csharp
// Creating dependencies inline
public class AlbumService
{
    private readonly IStorageProvider _storage = new MinioStorageProvider(); // Can't test!
}

// Or: static singleton
public static class StorageHelper
{
    public static void Upload() { /* ... */ } // Hard to test, can't inject
}
```

### 3. EF Core Code-First Pattern
**Convention:** Entities → Fluent Config → DbSet → Migration → Auto-run on startup

**✅ CORRECT:**
```csharp
// Entity
public class Album
{
    public int Id { get; set; }
    public string Title { get; set; }
    public ICollection<Photo> Photos { get; set; }
}

// Configuration
public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasMany(x => x.Photos).WithOne(x => x.Album);
    }
}

// DbContext
public DbSet<Album> Albums { get; set; }

// In ApplicationDbContextInitializer
public async Task MigrateAsync()
{
    await _context.Database.MigrateAsync(); // Auto-apply migrations
}
```

**❌ WRONG:**
```csharp
// Manual SQL migrations, stored procedures, or database-first
// Entities without configuration scattered everywhere
// Migrations not auto-applied on startup
```

*→ Consult `mermaid-diagram-curator` + `er-diagram-from-efcore` when diagramming data models and entity relationships.*

### 4. Factory Pattern for Multiple Implementations
**When to use:** Creating instances of multiple related implementations

**✅ CORRECT:**
```csharp
public class TokenValidatorFactory
{
    public static IExternalTokenValidator CreateValidator(string provider)
    {
        return provider?.ToLower() switch
        {
            "google" => new GoogleTokenValidator(),
            "facebook" => new FacebookTokenValidator(),
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }
}

// Usage: No other code changes needed when adding new provider
```

**❌ WRONG:**
```csharp
// Adding new provider scattered across multiple files
public class AuthService
{
    public void Handle(string provider)
    {
        if (provider == "google") ValidateGoogle();
        else if (provider == "facebook") ValidateFacebook(); // Had to modify this
        else if (provider == "microsoft") ValidateMicrosoft(); // Had to modify again
    }
}
```

### 5. Configuration-Driven Behavior
**Convention:** Settings in appsettings.json, injected via IConfiguration, no hardcoded values

**✅ CORRECT:**
```csharp
// appsettings.json
{
  "Storage": {
    "Provider": "minio",
    "Endpoint": "http://localhost:9000"
  },
  "Authentication": {
    "DisableAuth": true
  }
}

// Program.cs
var storageProvider = builder.Configuration["Storage:Provider"];
builder.Services.AddScoped<IStorageProvider>(provider =>
    storageProvider == "minio"
        ? new MinioStorageProvider(config)
        : new AzureStorageProvider(config));
```

**❌ WRONG:**
```csharp
// Hardcoded values
var provider = new MinioStorageProvider("http://localhost:9000");

// Or: environment detection scattered everywhere
public class StorageService
{
    public void Upload()
    {
        if (Environment.GetEnvironmentVariable("ENV") == "dev")
            UseMinio();
        else
            UseAzure();
    }
}
```

### 6. Service Organization: Business Logic in Services, HTTP in Controllers
**Convention:** Controllers handle HTTP (routing, model binding), Services handle business logic

**✅ CORRECT:**
```csharp
// Service: Business logic
public class AlbumService : IAlbumService
{
    public async Task<Album> CreateAsync(string title)
    {
        var album = new Album { Title = title };
        _dbContext.Albums.Add(album);
        await _dbContext.SaveChangesAsync();
        return album;
    }
}

// Controller: HTTP handling only
[ApiController]
[Route("api/albums")]
public class AlbumsController : ControllerBase
{
    private readonly IAlbumService _albumService;
    
    [HttpPost]
    public async Task<IActionResult> CreateAlbum([FromBody] CreateAlbumRequest request)
    {
        var album = await _albumService.CreateAsync(request.Title);
        return Ok(album);
    }
}
```

**❌ WRONG:**
```csharp
// Business logic in controller
[HttpPost]
public async Task<IActionResult> CreateAlbum([FromBody] CreateAlbumRequest request)
{
    var album = new Album { Title = request.Title };
    
    // Database logic here - BAD!
    _dbContext.Albums.Add(album);
    await _dbContext.SaveChangesAsync();
    
    // Validation logic here - BAD!
    if (album.Title.Length > 100)
        return BadRequest();
    
    return Ok(album);
}
```

## SOLID Principles Checklist

### Single Responsibility Principle (SRP)
✅ Each class has ONE reason to change
```csharp
// GOOD: One reason to change (token validation logic)
public class GoogleTokenValidator : IExternalTokenValidator
{
    public async Task<ExternalUserInfo> ValidateTokenAsync(string token) { /* ... */ }
}

// BAD: Multiple reasons to change
public class AuthService
{
    public void ValidateGoogle() { /* ... */ }
    public void ValidateFacebook() { /* ... */ }
    public void GenerateJwt() { /* ... */ }
    public void SendEmail() { /* ... */ }
}
```

### Open/Closed Principle (OCP)
✅ Open for extension, closed for modification
```csharp
// GOOD: Adding Facebook validator doesn't change existing code
public class FacebookTokenValidator : IExternalTokenValidator { /* ... */ }
// Just add to factory, done

// BAD: Adding Facebook requires modifying existing code
public class TokenValidator
{
    public if (provider == "google") ValidateGoogle();
    else if (provider == "facebook") ValidateFacebook(); // Had to modify!
}
```

### Liskov Substitution Principle (LSP)
✅ Subtypes are substitutable for base types
```csharp
// GOOD: Any IExternalTokenValidator can replace another
IExternalTokenValidator validator = 
    provider == "google" ? new GoogleTokenValidator() : new FacebookTokenValidator();
// Either works interchangeably

// BAD: FacebookTokenValidator doesn't match the contract
public class FacebookTokenValidator : IExternalTokenValidator
{
    public async Task<ExternalUserInfo> ValidateTokenAsync(string token)
    {
        // Returns completely different structure, breaks LSP
        return new { error = "Facebook uses different fields" };
    }
}
```

### Interface Segregation Principle (ISP)
✅ Clients don't depend on interfaces they don't use
```csharp
// GOOD: Focused interface
public interface IExternalTokenValidator
{
    Task<ExternalUserInfo> ValidateTokenAsync(string token);
}

// BAD: Bloated interface
public interface IAuthService
{
    Task<ExternalUserInfo> ValidateTokenAsync(string token);
    Task<string> GenerateJwtAsync(Claims claims);
    Task SendResetEmailAsync(string email);
    void LogAuthAttempt(string user);
    // ... bloated with unrelated methods
}
```

### Dependency Inversion Principle (DIP)
✅ Depend on abstractions, not concrete implementations
```csharp
// GOOD: Depends on interface
public class AlbumService
{
    private readonly IStorageProvider _storage;
    public AlbumService(IStorageProvider storage) => _storage = storage;
}

// BAD: Depends on concrete class
public class AlbumService
{
    private readonly MinioStorageProvider _storage = new();
    // Can't test, can't swap implementations
}
```

*→ Consult `solid-dry-principles` for comprehensive SOLID and DRY validation rules.*

## DRY Principle: Pattern Deduplication

### Don't Duplicate Token Validation Logic
**Pattern:** Each provider validates differently - create separate validator classes, don't put all logic in one service

**❌ VIOLATION:**
```csharp
public class AuthService
{
    if (provider == "google")
    {
        var payload = await GoogleJsonWebSignature.ValidateAsync(token);
        // extract email, name, etc.
    }
    else if (provider == "facebook")
    {
        var fbToken = await FacebookApi.ValidateAsync(token);
        // DUPLICATE LOGIC: extract email, name, etc.
    }
}
```

**✅ CORRECT:**
```csharp
// Each provider encapsulates its validation
public class GoogleTokenValidator : IExternalTokenValidator { /* ... */ }
public class FacebookTokenValidator : IExternalTokenValidator { /* ... */ }
```

### Don't Duplicate Role/Permission Checking
**Pattern:** Determine roles once at login, use claims in all controllers

**❌ VIOLATION:**
```csharp
// Duplicated in AlbumController
var isAdmin = _config.GetSection("AdminUsers").GetChildren()
    .Any(x => x.Value == currentUser.Email);

// Duplicated again in PhotoController
var isAdmin = _config.GetSection("AdminUsers").GetChildren()
    .Any(x => x.Value == currentUser.Email);

// And again in AccessCodeController...
```

**✅ CORRECT:**
```csharp
// Determined once in ExternalAuthService during login
if (isAdmin)
    claims.Add(new Claim("role", "Admin"));

// Used everywhere via [Authorize(Roles = "Admin")]
[HttpPost]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> CreateAlbum() { /* ... */ }
```

### Don't Duplicate Configuration Access
**Pattern:** Access configuration consistently, ideally through typed options

**❌ VIOLATION:**
```csharp
// Scattered everywhere
var key = _config["Authentication:Jwt:Key"];
var issuer = _config["Authentication:Jwt:Issuer"];
// ... repeated in 5 different services
```

**✅ CORRECT:**
```csharp
// Typed configuration class
public class JwtOptions
{
    public string Key { get; set; }
    public string Issuer { get; set; }
}

// Configure once in Program.cs
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Authentication:Jwt"));

// Inject and use everywhere
public class JwtTokenService
{
    private readonly JwtOptions _options;
    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;
}
```

## Anti-Patterns to Avoid

### ❌ Static Singletons
```csharp
// Hard to test, can't inject mocks
public static class AuthHelper
{
    public static string GenerateToken() { /* ... */ }
}

// Use this instead: register as scoped service in Program.cs
builder.Services.AddScoped<JwtTokenService>();
```

### ❌ Service Locator Pattern
```csharp
// Anti-pattern: fetching services at runtime
public class PhotoService
{
    public void Upload()
    {
        var storage = ServiceLocator.GetService<IStorageProvider>();
    }
}

// Use this instead: constructor injection
public class PhotoService
{
    private readonly IStorageProvider _storage;
    public PhotoService(IStorageProvider storage) => _storage = storage;
}
```

### ❌ God Objects
```csharp
// One class doing everything
public class ApplicationService
{
    public void HandleAuth() { }
    public void ProcessImages() { }
    public void SendEmails() { }
    public void ManageAlbums() { }
}

// Use this instead: split into focused services
public class AuthService { /* auth only */ }
public class ImageProcessor { /* images only */ }
public class EmailService { /* emails only */ }
public class AlbumService { /* albums only */ }
```

### ❌ Tight Coupling to Concrete Classes
```csharp
// Can't swap implementations
public class PhotoService
{
    public PhotoService()
    {
        _storage = new MinioStorageProvider();
    }
}

// Use this instead: inject interface
public class PhotoService
{
    private readonly IStorageProvider _storage;
    public PhotoService(IStorageProvider storage) => _storage = storage;
}
```

### ❌ Database Logic in Controllers
```csharp
// Wrong place for business logic
[HttpPost]
public async Task CreateAlbum()
{
    var album = new Album { /* ... */ };
    _dbContext.Albums.Add(album);
    await _dbContext.SaveChangesAsync();
}

// Use this instead: delegate to service
[HttpPost]
public async Task CreateAlbum()
{
    var album = await _albumService.CreateAsync(request);
    return Ok(album);
}
```

## Code Review Checklist

**For New Services:**
- [ ] Does it implement an interface?
- [ ] Is there existing code doing similar work (DRY violation)?
- [ ] Is it registered in Program.cs with proper lifetime (Scoped/Singleton)?
- [ ] Does it have a single responsibility?
- [ ] Are dependencies injected, not created inline?
- [ ] Can it be tested with mocked dependencies?

**For New Controllers:**
- [ ] Does it extend `BaseApiController`?
- [ ] Has `[ApiController]` and proper `[Route]`?
- [ ] Is business logic delegated to services (not in controller)?
- [ ] Are appropriate `[Authorize]` attributes set?
- [ ] Does it use DTOs for requests/responses?
- [ ] HTTP-only concerns (routing, binding, formatting)?

**For New Entities:**
- [ ] Is entity in Models/ folder?
- [ ] Is configuration in Data/Configurations/ (Fluent API)?
- [ ] Is DbSet added to ApplicationDbContext?
- [ ] Is migration created with `dotnet ef migrations add`?
- [ ] Are relationships properly defined?
- [ ] Is seeding handled in initializer?

**For New Interfaces/Abstractions:**
- [ ] Follows ISP (focused, not bloated)?
- [ ] All implementations use dependency injection?
- [ ] Factory pattern if multiple implementations?

**For Configuration:**
- [ ] Is it in appsettings.json?
- [ ] Properly namespaced (nested sections)?
- [ ] Safe default for production?
- [ ] Development override in appsettings.Development.json?

*→ Consult `folder-hygiene` for detailed project layout and file organization standards.*

## Questions to Ask During Review

1. **Is there an existing pattern we could reuse?** If implementing feature X, has something similar been done?
2. **Does this violate SRP?** Does the class have more than one reason to change?
3. **Could this be tested?** Can dependencies be injected for testing?
4. **Is this DRY?** Is similar logic implemented elsewhere?
5. **Does this follow established patterns?** Or is this inventing something new?
6. **Can a junior developer understand this?** Is it consistent with the codebase?

## When to Suggest Alternatives

If review reveals issues, suggest specific, actionable alternatives:

```
CONCERN: Authorization logic duplicated in 3 controllers

SUGGESTION:
1. Add [Authorize(Roles = "Admin")] attributes to controllers
2. Roles determined once in ExternalAuthService during login
3. All controllers use claims from JWT token

AFFECTED FILES:
- AlbumsController.cs
- PhotosController.cs
- AccessCodesController.cs
- ExternalAuthService.cs

EFFORT: Simple (30 min)
```

## Remember

- **This skill is your safety net.** Consult early and often.
- **SOLID is not optional.** It's how we avoid technical debt.
- **DRY saves time.** Don't duplicate patterns—reuse them.
- **Patterns are documented here.** Check against them before coding.
- **Future extensibility matters.** Design for Facebook/Microsoft auth, different storage, new roles.

## Cross-cutting plugin skills (always-on)

- `scratch-discipline` — architecture probe / decision drafts in `.copilot/scratch/<task-id>/`.
- `secret-hygiene` — no secrets in design docs, diagrams, or decision records.
- `commit-conventions` — canonical commit-message format.
- `branch-strategy-u-prefix` — `u/<actor>/<type>/<scope>` branches only, **targeting `trial`**. PRs into `main` come only from `trial`. See `Documentation/Architecture/DESIGN_DECISIONS.md` D016.
- `copilot-memory-update` — record durable cross-session architecture decisions.
- `markdown-doc-formatter` — formatting standard for any doc this skill produces.

---

When ready, present your architecture review with the suggested format.
