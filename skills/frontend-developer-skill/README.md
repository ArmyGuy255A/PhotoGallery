# Frontend Developer Skill

Expert guide for building PhotoGallery Angular 19.2 frontend using CoreUI components and responsive design.

## Quick Links

- **SKILL.md** - Complete frontend implementation guide
- **QUICK_REFERENCE.md** - One-page cheat sheet for common tasks
- **COMPLETION_CHECKLIST.md** - Quality verification before committing code

## When to Use This Skill

Use this skill when:

- **Creating Angular Components** - Building reusable, CoreUI-based components
- **Building Forms** - Reactive forms with validation and error handling
- **Connecting to APIs** - HTTP services with JWT authentication
- **Building Responsive Layouts** - Mobile-first design using Bootstrap 5
- **Accessibility** - WCAG 2.1 AA compliance
- **State Management** - Angular services and RxJS patterns
- **Routing** - Setting up authentication guards and lazy loading
- **Testing Integration** - Working with QA agent for E2E tests

## Key Phases Covered

1. **Phase 7: Frontend Architecture** - CoreUI setup, layout, guards, interceptors
2. **Phase 8: Angular Components** - Login, dashboard, albums, forms, gallery
3. **UI Feature Implementation** - Any frontend features across phases

## Architecture Pattern

PhotoGallery frontend follows **clean component structure**:

```
Components (Presentation)
    ↓
Services (Business Logic)
    ↓
HTTP Interceptors (API Integration)
    ↓
Guards (Authentication)
```

**Key Patterns:**
- Standalone components (Angular 19.2+)
- Reactive forms for all input
- RxJS observables for async
- HTTP interceptor for JWT attachment
- Auth guards for protected routes
- CoreUI components for consistent UI

## Component Types

### Page Components
- Top-level route components (Login, Dashboard, Albums, etc.)
- Handle routing and page-level logic
- Communicate with services

### Feature Components
- Reusable within pages (Album card, Photo uploader, etc.)
- Accept @Input() and emit @Output() events
- Pure presentation logic

### Layout Components
- Wrap page content (Admin layout, Client layout)
- Header, sidebar, footer structure

## Services

### Auth Service
- Token management (localStorage)
- Role checking
- Login/logout

### Album Service
- CRUD operations for albums
- HTTP requests to backend

### Photo Service
- Photo upload
- Photo download with quality selection

### Access Code Service
- Code validation
- Album access via code

## Key Technologies

- **Angular 19.2** - Frontend framework
- **CoreUI Angular Pro** - UI component library
- **Bootstrap 5** - Grid system and utilities
- **Reactive Forms** - Form handling and validation
- **RxJS** - Asynchronous programming
- **TypeScript** - Strict typing

## Related Skills

- **coreui-expert-skill** - CoreUI components and responsive design
- **clean-architecture-guide** - Service layer, guards, interceptors structure
- **photogallery-auth-skill** - JWT token flow and role-based access
- **photogallery-architect-skill** - Component structure validation
- **playwright-testing-skill** - E2E test patterns

## Before You Start

1. Read `coreui-expert-skill` to understand components
2. Read `clean-architecture-guide` for service patterns
3. Read `photogallery-auth-skill` for authentication flow
4. Review existing PhotoGallery Angular structure
5. Consult architect skill for component design validation

## Example Workflow

1. **Setup Phase** - Install CoreUI, configure app.config.ts, create layout
2. **Service Layer** - Build auth, album, photo services
3. **Auth Flow** - Login page, guards, JWT interceptor
4. **Admin Pages** - Dashboard, albums list, album create/edit
5. **File Upload** - Photo uploader component with progress
6. **Access Codes** - Code generator, visitor gallery
7. **Responsive Design** - Test mobile/tablet/desktop
8. **Accessibility** - Add labels, ARIA, semantic HTML
9. **Testing** - Work with QA for E2E tests
10. **Code Review** - Consult architect skill for structure validation

## Support

For questions about:
- **CoreUI components** → Consult `coreui-expert-skill`
- **Service patterns** → Consult `clean-architecture-guide`
- **Authentication** → Consult `photogallery-auth-skill`
- **Component structure** → Consult `photogallery-architect-skill`
- **Testing** → Consult `playwright-testing-skill`

---

**Dispatch this agent for Phase 7-8 frontend implementation work.**
