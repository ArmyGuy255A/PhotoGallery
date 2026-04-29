# Clean Architecture Guide

A comprehensive guide to implementing Clean Architecture (also known as Hexagonal, Onion, or Ports & Adapters Architecture) for PhotoGallery.

## Quick Overview

Clean Architecture organizes code into layers where dependencies **point inward toward the domain**:

```
Presentation Layer (Controllers, DTOs)
    ↓ depends on
Infrastructure Layer (Repositories, Services, DbContext)
    ↓ depends on
Domain Layer (Entities, Interfaces, Business Logic)
```

**Key Rule:** Domain never depends on anything else. It's the innermost, protected circle.

## The Three Layers

### 1. **Domain** (Core Business Logic)
- No dependencies on external libraries or frameworks
- Contains: Entities, Aggregates, Value Objects, Interfaces, Domain Events
- Testable without database or HTTP
- Example: `Album` entity with business rules

### 2. **Infrastructure** (Data & External Services)
- Implements domain interfaces
- Depends on Domain but not the other way around
- Contains: EF Core DbContext, Repositories, Storage Providers, Email Services
- Example: `AlbumRepository` implements `IRepository<Album>`

### 3. **Presentation** (API/UI)
- HTTP endpoints, Controllers, Request/Response DTOs
- Delegates business logic to services
- Depends on Infrastructure & Domain
- Example: `AlbumsController` calls `IRepository<Album>`

## Why This Matters for PhotoGallery

✅ **Testable** - Test business logic without database
✅ **Flexible** - Swap Minio ↔ Azure, Google ↔ Facebook without changing domain
✅ **Maintainable** - Clear separation, easy to understand
✅ **Scalable** - Independent features, team scaling
✅ **Framework Independent** - Core logic doesn't depend on ASP.NET/EF

## For More Details

See **SKILL.md** for:
- Complete layer definitions with code examples
- Repository and Specification patterns
- Domain Events and DDD patterns
- Detailed organization structures
- Testing implications
- Anti-patterns to avoid
- Decision points specific to PhotoGallery

## Quick Reference

| Layer | Purpose | Example | Can Depend On |
|-------|---------|---------|---------------|
| **Domain** | Business logic | Album.cs | Nothing (self only) |
| **Infrastructure** | Implementations | AlbumRepository.cs | Domain |
| **Presentation** | API/UI | AlbumsController.cs | Infrastructure, Domain |

## Anti-Pattern Alert

❌ **NEVER** let Domain depend on Infrastructure:
```csharp
// BAD - Domain shouldn't know about EF Core
public class Album
{
    public void Save(DbContext context) { }
}
```

✅ **CORRECT** - Infrastructure depends on Domain:
```csharp
// GOOD - Repository implements domain interface
public class AlbumRepository : IRepository<Album>
{
    public async Task AddAsync(Album album) { }
}
```

## Related Skills

- **PhotoGallery Architect** - Validates code for SOLID/DRY compliance (uses this guide)
- **PhotoGallery Unit Testing** - Creates tests using Clean Architecture patterns
- **PhotoGallery Authentication** - Designs auth services using Clean Architecture patterns

## Next Steps

1. Read SKILL.md for comprehensive guide
2. Use when designing PhotoGallery's Phase 2+ structure
3. Reference when reviewing code for layer compliance
4. Ensure all services follow the dependency flow

---

**Remember:** Clean Architecture is about making your core domain independent, testable, and flexible. Everything else serves that goal.
