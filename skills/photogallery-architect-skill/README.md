# PhotoGallery Architect Skill

A specialized skill for architectural review and guidance on the PhotoGallery project.

## What This Skill Does

The Architect Skill reviews code changes for compliance with:
- **SOLID Principles** (Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion)
- **DRY Principle** (Don't Repeat Yourself)
- **PhotoGallery-specific patterns** (established conventions and best practices)

## When to Consult This Skill

**ALWAYS use this skill when:**
- Creating new services or controllers
- Adding database entities or migrations
- Implementing design patterns
- Refactoring existing code
- You're unsure if code follows SOLID/DRY
- You think "this might be similar to existing code"

## What You'll Get

The skill provides an **Architecture Review** that includes:
1. ✅ Compliant patterns identified
2. ⚠️ Concerns and action items (if any)
3. 🔧 Specific suggestions for improvements
4. 📊 Estimated effort to fix issues

## Key Patterns Documented

1. **Interface-Based Abstraction** - For extensible components (storage, auth, image processing)
2. **Dependency Injection** - Constructor injection, registered in Program.cs
3. **EF Core Code-First** - Entities → Fluent Config → Migrations → Auto-run
4. **Factory Pattern** - For multiple implementations of same interface
5. **Configuration-Driven** - Settings in appsettings.json, no hardcoding
6. **Service Separation** - Services = business logic, Controllers = HTTP

## Example Usage

**Scenario:** You're creating an `AlbumService`

```
Ask the architect:
"I'm creating AlbumService for album CRUD operations. Should this 
implement an interface? How should it be registered in Program.cs?"

The architect will review and approve/suggest improvements.
```

## Anti-Patterns the Skill Catches

- Static singletons (can't test)
- Service locator pattern (can't inject)
- God objects (too many responsibilities)
- Tight coupling to concrete classes
- Database logic in controllers
- Duplicated validation/authorization logic

## Files Included

- `SKILL.md` - The actual skill with all patterns and guidelines
- `README.md` - This file, quick reference

## Location in Repository

```
D:\repos\PhotoGallery\PhotoGallery\
└── skills\
    └── yogo-architect-skill\
        ├── SKILL.md
        ├── README.md
        ├── QUICK_REFERENCE.md
        ├── INSTALLATION.md
        └── COMPLETION_CHECKLIST.md
```

## How to Use

1. When working on code, ask: "Should I consult the architect skill?"
2. If unsure about architecture, use this skill
3. Review the recommendations carefully
4. Apply suggestions before submitting PR

Remember: **This skill is your safety net against architectural debt.**
