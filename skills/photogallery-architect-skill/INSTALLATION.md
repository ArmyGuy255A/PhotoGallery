# Installing the PhotoGallery Architect Skill

## Location
The Architect Skill is located at:
```
D:\repos\PhotoGallery\PhotoGallery\skills\yogo-architect-skill\
```

## Files
- **SKILL.md** - The main skill definition (required)
- **README.md** - Quick reference guide
- **INSTALLATION.md** - This file
- **QUICK_REFERENCE.md** - One-page reference card
- **COMPLETION_CHECKLIST.md** - Quality verification

## How to Use

### Method 1: Direct Reference (Recommended)
When you need an architecture review, explicitly ask for it:
> "Use the yogo-architect skill to review this code: [code]"

### Method 2: CI/CD Integration
The skill can be automatically referenced in GitHub Actions workflows:
```yaml
- name: Architecture Review
  run: |
    # Reference the skill guide
    cat ${{ github.workspace }}/skills/yogo-architect-skill/SKILL.md
```

## Skill Details

**Name:** yogo-architect
**Type:** Code Review & Architecture Validation
**Primary Use:** PhotoGallery project architecture compliance
**Triggers:** SOLID/DRY violations, pattern misalignment, design decisions

## What the Skill Provides

When invoked, the Architect Skill will:
1. Review code against SOLID principles
2. Check for DRY violations
3. Validate PhotoGallery patterns
4. Catch anti-patterns
5. Provide specific recommendations
6. Estimate effort to fix issues

## Key Patterns It Validates

✅ Interface-based abstractions
✅ Dependency injection (constructor)
✅ EF Core code-first migrations
✅ Factory pattern for multiple implementations
✅ Configuration-driven behavior
✅ Service/Controller separation
✅ JWT token-based authentication
✅ Role-based authorization claims

## Anti-Patterns It Catches

❌ Static singletons
❌ Service locator pattern
❌ God objects (too many responsibilities)
❌ Tight coupling to concrete classes
❌ Database logic in controllers
❌ Duplicate authorization/validation logic
❌ Hardcoded configuration values

## Review Output Format

When you use this skill, you'll receive:

```
## ARCHITECTURE REVIEW: [Feature Name]

### ✅ COMPLIANT
- Pattern 1: OK
- Pattern 2: OK

### ⚠️ CONCERNS / ACTION ITEMS
- Issue 1: [Why] → [Fix]
- Issue 2: [Why] → [Fix]

### ✅ APPROVED OR ❌ NEEDS REVISION

**Estimated Effort to Fix:** simple/moderate/significant
```

## Next Steps

1. **Use this skill** - When creating services, controllers, or entities, ask for an architecture review
2. **Create Authentication Skill** - Next guided skill for auth/authz patterns
3. **Create CoreUI Angular Skill** - UI component patterns
4. **Create Playwright Testing Skill** - E2E testing patterns
5. **Begin Implementation** - Start Phase 2 with database models

## Support

For detailed patterns and examples, see:
- **SKILL.md** - Complete skill with all guidelines
- **README.md** - Quick reference
- **QUICK_REFERENCE.md** - One-page developer reference card
