# Backend Developer Skill

Expert guide for building PhotoGallery backend using ASP.NET 9.0, Entity Framework Core, and clean architecture principles.

## Quick Links

- **SKILL.md** - Complete backend implementation guide covering all phases
- **QUICK_REFERENCE.md** - One-page cheat sheet for common tasks
- **COMPLETION_CHECKLIST.md** - Quality verification before committing code

## When to Use This Skill

Use this skill when:

- **Creating Domain Entities** - Need to design Album, Photo, AccessCode, UserAccessLog entities with proper business logic
- **Building Repositories** - Implementing Repository pattern with EF Core
- **Writing Services** - Creating business logic layer that orchestrates repositories
- **Building API Endpoints** - Creating REST endpoints for albums, photos, access codes
- **Implementing Authentication** - Setting up OAuth callbacks, JWT token generation, role-based access
- **Integrating Storage** - Building Minio/Azure storage provider implementation
- **Processing Images** - Creating image compression and versioning service
- **Database Design** - Configuring EF Core entities, relationships, migrations
- **Quality Assurance** - Verifying SOLID/DRY compliance with architect skill

## Key Phases Covered

1. **Phase 2: Database & Core Models** - Entity design, repositories, services, migrations
2. **Phase 3: Authentication & Authorization** - OAuth, JWT, role-based access control
3. **Phase 4: Storage Abstraction** - IStorageProvider, Minio, Azure implementations
4. **Phase 5: Image Processing** - Compression profiles, background processing, versioning
5. **Phase 6: API Endpoints** - RESTful endpoints for all PhotoGallery features

## Architecture Pattern

PhotoGallery backend follows **three-layer architecture**:

```
Presentation Layer (API Controllers)
    ↓
Business Logic Layer (Services)
    ↓
Data Access Layer (Repositories)
    ↓
Domain Layer (Entities)
```

Dependencies flow **inward only** - domain never depends on infrastructure or presentation.

## Key Patterns

### Entity Design
- Private constructors, public factory methods
- Business logic in entities (validation, state transitions)
- No service dependencies in entities
- Navigation properties for relationships

### Repository Pattern
- Generic `IRepository<T>` for CRUD operations
- Specific repository interfaces for domain-specific queries
- `ISpecification<T>` for reusable query logic
- Dependency-injected into services

### Service Layer
- Validates input, enforces business rules
- Uses repositories for data access
- Throws domain exceptions on validation errors
- Returns DTOs (not entities) to controllers

### API Controllers
- Minimal logic - delegates to services
- Extracts user claims from JWT
- Returns appropriate HTTP status codes
- Handles service exceptions

### Storage Abstraction
- `IStorageProvider` interface abstracts Minio/Azure
- Factory pattern selects provider based on configuration
- Enables testing with mock provider

## Related Skills

- **clean-architecture-guide** - Understand the three-layer pattern and dependency rules
- **photogallery-architect-skill** - Validate SOLID/DRY compliance during code review
- **photogallery-auth-skill** - Understand OAuth, JWT, and role patterns
- **unit-testing-skill** - Write unit tests for entities and services

## Before You Start

1. Read `clean-architecture-guide` to understand layering
2. Read `photogallery-architect-skill` to know SOLID principles
3. Read `photogallery-auth-skill` for authentication patterns
4. Review existing PhotoGallery code structure
5. Consult architect skill before committing major changes

## Example Workflow

1. **Design Phase** - Sketch entity relationships, identify business logic
2. **Domain Layer** - Write entity classes with factory methods and business logic
3. **Infrastructure Layer** - Create EF configurations, repositories, services
4. **Presentation Layer** - Build API controllers that use services
5. **Authentication** - Integrate OAuth, JWT tokens, role-based access
6. **Storage** - Implement IStorageProvider if handling file uploads
7. **Image Processing** - Queue and process images if needed
8. **Testing** - Write unit tests for entities and services
9. **Code Review** - Consult architect skill for SOLID/DRY compliance
10. **Migration** - Create EF migration and verify database schema

## Support

For questions about:
- **Architecture decisions** → Consult `clean-architecture-guide`
- **SOLID/DRY violations** → Consult `photogallery-architect-skill`
- **Authentication patterns** → Consult `photogallery-auth-skill`
- **Unit testing** → Consult `unit-testing-skill`
- **Frontend integration** → Consult `frontend-developer-skill`

---

**Dispatch this agent for Phase 2-6 backend implementation work.**
