# Architect Skill - Quick Reference Card

## When to Use This Skill

✅ Creating new services
✅ Creating new controllers  
✅ Adding database entities
✅ Implementing design patterns
✅ Refactoring existing code
✅ Unsure if code follows SOLID/DRY
✅ Think "this might duplicate existing code"

## Core Patterns

### 1. Interfaces for Multiple Implementations
```csharp
// GOOD: Interface + multiple implementations
public interface IStorageProvider { }
public class MinioStorageProvider : IStorageProvider { }
public class AzureStorageProvider : IStorageProvider { }

// BAD: No interface, can't swap implementations
public class StorageService { }
```

### 2. Dependency Injection
```csharp
// GOOD: Inject via constructor
public AlbumService(IStorageProvider storage) 
    => _storage = storage;

// BAD: Create inline
public AlbumService() 
    => _storage = new MinioStorageProvider();
```

### 3. EF Core Code-First
```csharp
// Entity + Configuration + DbSet + Migration + Auto-run
public class Album { }
public class AlbumConfig : IEntityTypeConfiguration<Album> { }
public DbSet<Album> Albums { get; set; }
// dotnet ef migrations add AddAlbum
// Migration auto-runs on startup
```

### 4. Factory Pattern
```csharp
// GOOD: Centralized creation
public class TokenValidatorFactory
{
    public static IExternalTokenValidator CreateValidator(string provider)
        => provider switch { "google" => new GoogleTokenValidator() };
}

// BAD: Creation scattered in multiple files
```

### 5. Configuration-Driven
```csharp
// appsettings.json
{ "Storage": { "Provider": "minio" } }

// Access in Program.cs
var provider = config["Storage:Provider"];
```

### 6. Service = Logic, Controller = HTTP
```csharp
// GOOD: Logic in service
public class AlbumService { public Task<Album> CreateAsync() { } }

// GOOD: HTTP in controller
[HttpPost] public Task CreateAlbum() => _service.CreateAsync();

// BAD: Logic in controller
[HttpPost] public Task CreateAlbum() { _db.Albums.Add(...); }
```

## SOLID Principles

| Principle | Rule | Example |
|-----------|------|---------|
| **S**RP | One reason to change | GoogleValidator only changes if Google API changes |
| **O**CP | Open/extend, closed/modify | Add FacebookValidator without changing existing code |
| **L**SP | Subtypes substitute for base | Any ITokenValidator works interchangeably |
| **I**SP | Don't force unused methods | ITokenValidator has only ValidateAsync() |
| **D**IP | Depend on abstractions | AlbumService(IStorage) not AlbumService(MinioStorage) |

## DRY - Don't Duplicate

### ❌ Duplicate Validation Logic
```csharp
// Repeated in multiple services
if (provider == "google") ValidateGoogle();
else if (provider == "facebook") ValidateFacebook();
```
**Fix:** Separate validator classes per provider

### ❌ Duplicate Role Checking
```csharp
// Check in AlbumController, PhotoController, AccessCodeController
var isAdmin = config["AdminUsers"].Contains(user.Email);
```
**Fix:** Check once at login, use [Authorize(Roles="Admin")] everywhere

### ❌ Duplicate Configuration Access
```csharp
// In 5 different services
var key = config["Auth:Jwt:Key"];
var issuer = config["Auth:Jwt:Issuer"];
```
**Fix:** Typed configuration class, inject once

## Anti-Patterns to Avoid

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| Static singletons | Can't test, can't inject | Register as scoped service |
| Service locator | Hard to test | Constructor injection |
| God objects | Too many responsibilities | Split into focused services |
| Tight coupling | Can't swap implementations | Use interfaces |
| DB logic in controllers | Testability, reusability | Move to services |

## Code Review Checklist

### Services
- [ ] Has interface?
- [ ] Is there duplicate logic elsewhere?
- [ ] Registered in Program.cs?
- [ ] Single responsibility?
- [ ] Dependencies injected?

### Controllers
- [ ] Extends BaseApiController?
- [ ] Logic delegated to services?
- [ ] Proper [Authorize] attributes?
- [ ] Uses DTOs?

### Entities
- [ ] In Models/ folder?
- [ ] Configuration in Data/Configurations/?
- [ ] DbSet added?
- [ ] Migration created?
- [ ] Relationships defined?

### Configuration
- [ ] In appsettings.json?
- [ ] Properly namespaced?
- [ ] Safe production default?
- [ ] Dev override in appsettings.Development.json?

## Review Template

Request a review like:
> "Review this code for SOLID/DRY compliance: [code here]"

You'll get:
```
## ARCHITECTURE REVIEW: [Feature]

### ✅ COMPLIANT
- Follows [pattern]
- Properly [injected/configured/structured]

### ⚠️ CONCERNS
- [Issue]: [Why] → [Fix]

### ✅ APPROVED OR ❌ NEEDS REVISION

Estimated Effort: [trivial/simple/moderate/significant]
```

## Key Questions

1. **Is there an existing pattern I could reuse?**
2. **Does this violate SRP?** (Multiple reasons to change?)
3. **Can this be tested?** (Dependencies injectable?)
4. **Is this DRY?** (Logic repeated elsewhere?)
5. **Does this follow established patterns?**
6. **Would a junior understand this?** (Consistent with codebase?)

## Patterns by Feature

### Adding OAuth Provider (Facebook/Microsoft)
1. Create `FacebookTokenValidator : IExternalTokenValidator`
2. Add to `TokenValidatorFactory` switch
3. Done! No other changes needed

### Adding Storage Provider (Azure)
1. Create `AzureStorageProvider : IStorageProvider`
2. Update factory in Program.cs
3. Configure in appsettings.json
4. Done!

### Adding New Role
1. Define role in seeding
2. Add claim in `ExternalAuthService`
3. Use `[Authorize(Roles="NewRole")]` on controllers
4. Done!

### Adding New Entity
1. Create entity class
2. Create configuration class (Fluent API)
3. Add DbSet
4. Create migration: `dotnet ef migrations add [Name]`
5. Seed in initializer if needed
6. Done! Migration auto-runs

## Remember

✨ **This skill prevents technical debt**
✨ **Ask early, ask often**
✨ **SOLID + DRY = maintainable code**
✨ **Patterns are here for a reason**
✨ **Future extensibility matters**
