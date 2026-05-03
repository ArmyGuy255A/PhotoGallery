# PhotoGallery Skills & Architecture Reference Guide

**Status:** ✅ ALL COMPLETE - Ready for Phase 2 Implementation  
**Last Updated:** April 2024  
**Documentation:** 8 Skills, 5 Architecture Diagrams, 200+ Code Examples, ~367 KB Total

---

## Quick Navigation

### Skills (8 Total)

**Foundation:**
- [clean-architecture-guide](D:\repos\PhotoGallery\PhotoGallery\skills\clean-architecture-guide\) - Three-layer architecture patterns
- [photogallery-architect-skill](D:\repos\PhotoGallery\PhotoGallery\skills\photogallery-architect-skill\) - SOLID/DRY validation

**Specialized:**
- [photogallery-auth-skill](D:\repos\PhotoGallery\PhotoGallery\skills\photogallery-auth-skill\) - OAuth, JWT, role-based access
- [coreui-expert-skill](D:\repos\PhotoGallery\PhotoGallery\skills\coreui-expert-skill\) - UI components, responsive design
- [playwright-testing-skill](D:\repos\PhotoGallery\PhotoGallery\skills\playwright-testing-skill\) - E2E testing patterns

**Agents (NEW):**
- [backend-developer-skill](D:\repos\PhotoGallery\PhotoGallery\skills\backend-developer-skill\) - End-to-end backend implementation
- [frontend-developer-skill](D:\repos\PhotoGallery\PhotoGallery\skills\frontend-developer-skill\) - End-to-end frontend implementation
- [qa-quality-control-skill](D:\repos\PhotoGallery\PhotoGallery\skills\qa-quality-control-skill\) - E2E testing & QA

### Architecture Diagrams

All in `D:\repos\PhotoGallery\PhotoGallery\Architecture\`:

1. **01-Entity-Relationship-Diagram.mmd** - Database schema
2. **02-Authentication-and-Photo-Workflow.mmd** - 60-step sequence diagram
3. **03-Backend-Architecture-Layers.mmd** - Three-layer system design
4. **04-Frontend-Component-Structure.mmd** - Angular component hierarchy
5. **05-JWT-Token-and-Access-Control-Flow.mmd** - Authentication flow

---

## Implementation Roadmap

### Phase 2: Database & Core Models (Backend Agent)
- Create domain entities (Album, Photo, PhotoVersion, AccessCode, User)
- Configure EF Core relationships with Fluent API
- Implement repositories with generic IRepository<T> pattern
- Create services for business logic
- Generate EF migrations
- **Reference:** clean-architecture-guide, photogallery-architect-skill

### Phase 3: Authentication & Authorization (Backend Agent)
- Implement OAuth callback with Google
- Create JWT token service
- Implement role-based access control
- Create access code validation
- Implement DISABLE_AUTH bypass for development
- **Reference:** photogallery-auth-skill, photogallery-architect-skill

### Phase 4: Storage Abstraction (Backend Agent)
- Create IStorageProvider interface
- Implement MinioStorageProvider
- Implement AzureStorageProvider
- Create StorageProviderFactory
- Configure provider switching via appsettings
- **Reference:** photogallery-architect-skill (factory pattern)

### Phase 5: Image Processing (Backend Agent)
- Create IImageProcessor interface
- Implement compression profiles (High/Medium/Low/Raw)
- Create background processing service
- Implement photo version generation
- Queue image processing
- **Reference:** photogallery-architect-skill

### Phase 6: API Endpoints (Backend Agent)
- Albums CRUD endpoints (authenticated)
- Photo upload endpoints
- Access code endpoints
- Visitor access code routes
- Photo download with quality selection
- **Reference:** photogallery-auth-skill, photogallery-architect-skill

### Phase 7: Frontend Architecture (Frontend Agent)
- Install CoreUI packages
- Configure app.config.ts for CoreUI
- Create dashboard layout
- Setup HTTP interceptor for JWT
- Create auth guards (authenticated, admin)
- **Reference:** coreui-expert-skill, clean-architecture-guide

### Phase 8: Angular Components (Frontend Agent)
- Login page (Google OAuth)
- Admin dashboard
- Albums list and form
- Photo upload
- Access code generator
- Photo gallery viewer (visitor)
- **Reference:** coreui-expert-skill, photogallery-architect-skill

### Phase 9: Testing Infrastructure (QA Agent)
- Setup Playwright configuration
- Create login fixture for authenticated tests
- Create Page Objects for all pages
- Write E2E test suites (auth, albums, photos, access codes)
- Implement accessibility testing
- Setup multi-browser testing
- **Reference:** playwright-testing-skill

### Phases 10-11: DevOps & Polish (Backend Agent)
- Docker Compose setup
- GitHub Actions CI/CD
- Performance optimization
- Security review

---

## Skills Cross-Reference Matrix

| Phase | Primary Agent | References | Purpose |
|-------|---------------|-----------|---------|
| 2-6 | Backend Developer | architect, clean-arch, auth | Implement backend layers |
| 7-8 | Frontend Developer | architect, clean-arch, coreui, auth | Build UI with CoreUI |
| 9+ | QA Quality Control | playwright, backend, frontend, auth | E2E testing |

---

## Key Architecture Decisions

### Database Layer
- **ORM:** Entity Framework Core (code-first)
- **Migration:** Auto-migration on app startup
- **Pattern:** Generic Repository with Specifications
- **Database:** SQLite (dev), PostgreSQL (prod)

### Service Layer
- **Pattern:** Three-layer architecture (Domain/Infrastructure/Presentation)
- **DI:** Dependency injection for all services
- **Factory:** Factory pattern for providers (storage, OAuth)
- **Specifications:** Reusable query objects

### API Layer
- **Authentication:** JWT tokens (issued after OAuth)
- **Authorization:** Role-based access control (Admin/User/Visitor)
- **Access Codes:** Time-limited codes for visitor access
- **Routes:** `/api/*` for authenticated, `/code/*` for visitors

### Frontend Layer
- **Framework:** Angular 19.2 with standalone components
- **UI:** CoreUI Pro components throughout
- **Forms:** Reactive forms with validation
- **Authentication:** JWT interceptor on all requests
- **Guards:** Route guards for authentication/authorization

### Storage
- **Abstraction:** IStorageProvider interface
- **Development:** Minio (local S3-compatible)
- **Production:** Azure Blob Storage
- **Configuration:** appsettings-based switching

### Image Processing
- **Queue:** Background processing (Hangfire/Azure Service Bus)
- **Versions:** 4 compression levels (High/Medium/Low/Raw)
- **Library:** ImageSharp for compression
- **Storage:** Versioned files in storage provider

---

## Development Workflow

### Setup
1. Clone repository
2. Review plan.md and architecture diagrams
3. Reference appropriate skill based on current phase
4. Use architect skill for code review

### During Development
1. Read relevant skill SKILL.md
2. Follow code examples and patterns
3. Consult architect skill for SOLID/DRY validation
4. Reference related skills for integration points

### After Features
1. Run unit tests
2. Run E2E tests with QA agent
3. Get architect skill review
4. Merge to main

---

## Common Questions

### "Should I create a new service class?"
→ Consult **photogallery-architect-skill** for single-responsibility principle

### "How should I organize this Angular component?"
→ Check **frontend-developer-skill** for component patterns, **coreui-expert-skill** for UI

### "What's the best way to implement this authentication feature?"
→ See **photogallery-auth-skill** for OAuth/JWT patterns

### "How do I write E2E tests for this feature?"
→ Use **playwright-testing-skill** and **qa-quality-control-skill** patterns

### "Is this following clean architecture?"
→ Validate with **clean-architecture-guide** layers

### "Should my database query be in the repository?"
→ Check **clean-architecture-guide** for data access patterns

---

## Files Organization

```
PhotoGallery/
├── skills/
│   ├── clean-architecture-guide/
│   ├── photogallery-architect-skill/
│   ├── photogallery-auth-skill/
│   ├── coreui-expert-skill/
│   ├── playwright-testing-skill/
│   ├── backend-developer-skill/          ← NEW
│   ├── frontend-developer-skill/         ← NEW
│   └── qa-quality-control-skill/         ← NEW
│
├── Architecture/                         ← NEW
│   ├── 01-Entity-Relationship-Diagram.mmd
│   ├── 02-Authentication-and-Photo-Workflow.mmd
│   ├── 03-Backend-Architecture-Layers.mmd
│   ├── 04-Frontend-Component-Structure.mmd
│   └── 05-JWT-Token-and-Access-Control-Flow.mmd
│
├── PhotoGallery/
│   ├── Models/
│   │   ├── Album.cs
│   │   ├── Photo.cs
│   │   ├── PhotoVersion.cs
│   │   ├── AccessCode.cs
│   │   └── ApplicationUser.cs
│   │
│   ├── Data/
│   │   ├── Configurations/
│   │   ├── ApplicationDbContext.cs
│   │   └── Migrations/
│   │
│   ├── Services/
│   │   ├── AlbumService.cs
│   │   ├── PhotoService.cs
│   │   ├── AuthService.cs
│   │   └── ImageProcessingService.cs
│   │
│   ├── Controllers/
│   │   ├── AlbumsController.cs
│   │   ├── PhotosController.cs
│   │   └── AccessCodesController.cs
│   │
│   └── Infrastructure/
│       ├── Repositories/
│       └── Storage/
│
└── FE.PhotoGallery/
    ├── src/app/
    │   ├── pages/
    │   │   ├── login/
    │   │   ├── albums/
    │   │   ├── photo-upload/
    │   │   ├── access-code-form/
    │   │   └── photo-gallery/
    │   │
    │   ├── components/
    │   │   ├── album-card/
    │   │   ├── photo-grid/
    │   │   └── forms/
    │   │
    │   ├── services/
    │   │   ├── auth.service.ts
    │   │   ├── album.service.ts
    │   │   ├── photo.service.ts
    │   │   ├── http-token.interceptor.ts
    │   │   └── storage.service.ts
    │   │
    │   └── guards/
    │       ├── auth.guard.ts
    │       └── admin.guard.ts
    │
    └── tests/e2e/
        ├── pages/
        ├── fixtures/
        └── specs/
```

---

## Quality Assurance

All skills include:
- ✅ SKILL.md (comprehensive reference)
- ✅ README.md (quick overview & when to use)
- ✅ QUICK_REFERENCE.md (one-page cheat sheet)
- ✅ COMPLETION_CHECKLIST.md (quality verification)
- ✅ 200+ real code examples
- ✅ PhotoGallery-specific patterns
- ✅ Best practices and anti-patterns
- ✅ Cross-skill references

---

## Performance Targets

- **Page Load:** < 3 seconds
- **API Response:** < 500ms (p95)
- **Photo Download:** Streaming capable
- **Image Processing:** Asynchronous (background queue)
- **Database Queries:** Optimized with indexes
- **E2E Test Suite:** Complete in < 10 minutes

---

## Security Considerations

✅ JWT tokens with expiration
✅ Role-based access control
✅ HTTPS enforcement
✅ SQL injection prevention (EF Core parameterization)
✅ XSS prevention (Angular sanitization)
✅ CSRF protection (cookie-based tokens)
✅ Access code expiration
✅ No sensitive data in localStorage (only JWT)

---

## Accessibility Standards

✅ WCAG 2.1 AA compliance
✅ Semantic HTML
✅ ARIA labels and descriptions
✅ Keyboard navigation
✅ Color contrast requirements
✅ Alt text for images
✅ Focus indicators

---

## Next Steps

1. **Read** plan.md for Phase 2 details
2. **Review** architecture diagrams to visualize system design
3. **Dispatch** Backend Developer Agent for Phase 2 work
4. **Reference** skills during development
5. **Use** architect skill for code review
6. **Progress** through phases 2-11 sequentially

---

## Support & References

For specific questions:

- **Architecture:** photogallery-architect-skill, clean-architecture-guide
- **Backend:** backend-developer-skill, photogallery-auth-skill
- **Frontend:** frontend-developer-skill, coreui-expert-skill
- **Testing:** qa-quality-control-skill, playwright-testing-skill
- **Authentication:** photogallery-auth-skill

---

**Created:** April 2024  
**Last Updated:** April 2024  
**Status:** ✅ Ready for Development  
**Next Phase:** Backend Database & Models (Phase 2)

---
