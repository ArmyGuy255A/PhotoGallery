# PhotoGallery Documentation Index

This folder serves as the **persistent memory** for all design decisions, architectural patterns, and implementation guidance for the PhotoGallery project. All skills consult this documentation before making changes.

**Key Principle**: Documentation = Source of Truth for Design Decisions

---

## 📁 Folder Structure

### 🏗️ **Architecture** - Design Decisions & Patterns
Core architectural decisions, patterns, and system design. This is the source of truth for how things should be built.

- `DESIGN_DECISIONS.md` - All approved design decisions with rationale
- `SYSTEM_ARCHITECTURE.md` - System overview and component relationships
- `DATABASE_SCHEMA.md` - Entity relationships and database design
- `API_DESIGN.md` - REST API patterns and conventions
- `STORAGE_LAYER.md` - Storage provider abstraction (MinIO/Azure)

### 📊 **Phase-Reports** - Phase Completion & Status
Completed phase reports showing what was implemented and why.

- `Phase-1-5-Foundations.md` - Authentication, storage, image processing
- `Phase-6-10-UI-Development.md` - Frontend components and features
- `Phase-11-Refinements.md` - Bug fixes and refinements
- `Phase-12-Photo-Upload.md` - Photo upload infrastructure and TDD
- (Future phases)

### 📚 **Guides** - How-To and Process Documentation
Step-by-step guides for developers and operational procedures.

- `TDD_WORKFLOW.md` - Test-Driven Development workflow
- `TDD_QUICK_REFERENCE.md` - TDD cheat sheet
- `TDD_IMPLEMENTATION_SUMMARY.md` - TDD overview and benefits
- `TDD_READY_TO_USE.md` - Getting started with TDD

### 🚀 **Startup** - Deployment & Operations
How to start, configure, and deploy the application.

- `STARTUP_GUIDE.md` - How to start the application locally
- `STARTUP_SCRIPTS_COMPLETE.md` - Startup script documentation
- `STARTUP_SCRIPTS_VISUAL_GUIDE.md` - Visual walkthrough
- `START_SCRIPTS_README.md` - Script usage

### 📋 **Root Level** - Project Overview
Key reference files in project root.

- `README.md` - Project overview and quick start
- `SKILLS_AND_ARCHITECTURE_REFERENCE.md` - Skill directory

---

## 🔄 How Documentation is Used

### For Architects
1. **Before making any design decision**, check `Architecture/DESIGN_DECISIONS.md`
2. **If new decision needed**, consult with user and get approval
3. **After approval**, update `Architecture/DESIGN_DECISIONS.md`
4. **Update related documents** (`API_DESIGN.md`, `DATABASE_SCHEMA.md`, etc.)

### For Backend Developers
1. **Before implementing**, check `Architecture/DESIGN_DECISIONS.md`
2. **Follow TDD workflow** from `Guides/TDD_WORKFLOW.md`
3. **Write tests** that validate design from documentation
4. **Run all tests** before committing
5. **Consult architect** for SOLID/DRY validation
6. **Commit** with reference to design decision

### For All Skills
1. **Always reference** `Architecture/DESIGN_DECISIONS.md` before proposing changes
2. **Validate against** existing patterns in documentation
3. **Never propose** changes that conflict with approved decisions
4. **Ask user** for design decisions when unclear
5. **Update documentation** when design changes approved

---

## 📝 Updating Documentation

### When Creating New Features
```
1. Consult: Architecture/DESIGN_DECISIONS.md
2. Design: Propose design to user
3. Approve: Get user approval for design
4. Document: Update Architecture/ files
5. Implement: Write tests → code → refactor
6. Test: All unit tests must pass
7. Verify: Check against documented design
8. Commit: Include design decision reference
```

### When Changing Existing Features
```
1. Understand: Why was original decision made? (check documentation)
2. Validate: Is this change justified?
3. Consult: Get user approval for change
4. Update: Modify Architecture/ documentation
5. Implement: Follow TDD workflow
6. Test: All unit tests must pass
7. Commit: Reference changed design decision
```

---

## 🎯 Documentation Governance

### Single Source of Truth
- **Architecture/** folder = Approved design decisions
- If code conflicts with documentation = Code is wrong, fix it
- If design unclear in documentation = Ask user, then document

### Design Decisions
Every significant design decision is recorded with:
- **What**: What was decided
- **Why**: Rationale for the decision
- **When**: When it was approved
- **Who**: Who approved it (always the user)
- **Implications**: How it affects the system

### Versioning
- Major changes → New section in `DESIGN_DECISIONS.md`
- Minor changes → Updated bullet points in related files
- Deprecated patterns → Marked as "Legacy" with migration path

---

## 🚀 Quick Reference

### I Need To...

**Understand how photos are stored**
→ See: `Architecture/STORAGE_LAYER.md`

**Add a new API endpoint**
→ See: `Architecture/API_DESIGN.md` + `Guides/TDD_WORKFLOW.md`

**Change the database schema**
→ See: `Architecture/DATABASE_SCHEMA.md` + `Architecture/DESIGN_DECISIONS.md`

**Implement a new feature**
→ See: `Guides/TDD_WORKFLOW.md` + consult `Architecture/DESIGN_DECISIONS.md`

**Start the application**
→ See: `Startup/STARTUP_GUIDE.md`

**Write unit tests**
→ See: `Guides/TDD_WORKFLOW.md` + `Guides/TDD_QUICK_REFERENCE.md`

**Understand system design**
→ See: `Architecture/SYSTEM_ARCHITECTURE.md`

---

## 📊 Documentation Metrics

| Aspect | Status |
|--------|--------|
| Architecture decisions documented | ✅ Ongoing |
| Phase reports complete | ✅ Phases 1-12 |
| TDD guides available | ✅ 4 guides |
| Startup docs | ✅ 4 guides |
| Mermaid diagrams | ✅ As needed |
| Design decision count | 🔄 Growing |
| Consistency | ✅ Reviewed by architect |

---

## 🔗 Integration with Skills

All PhotoGallery skills reference this documentation:

- **photogallery-architect-skill**: Consults Architecture/ before approving changes
- **backend-developer-skill**: References Architecture/ for design, Guides/ for TDD
- **tdd-unit-testing-skill**: Uses Architecture/ to validate test design
- **photogallery-documentation-skill**: Manages and updates all documentation
- Other skills: Consult before proposing changes

---

## ✅ Checklist for Every Implementation

Before committing any code:

- [ ] Checked `Architecture/DESIGN_DECISIONS.md` for related decisions
- [ ] Followed TDD workflow from `Guides/TDD_WORKFLOW.md`
- [ ] All unit tests passing (`dotnet test PhotoGallery.Tests`)
- [ ] Design validated against `Architecture/` documentation
- [ ] Architect skill approved the design
- [ ] Updated documentation if design changed
- [ ] Commit message references design decision

---

## 🎓 Learning Path

### New Developer Onboarding
1. Read: `README.md` (project overview)
2. Read: `Startup/STARTUP_GUIDE.md` (how to start)
3. Read: `Architecture/SYSTEM_ARCHITECTURE.md` (system design)
4. Read: `Guides/TDD_WORKFLOW.md` (development process)
5. Check: `Architecture/DESIGN_DECISIONS.md` (design patterns)
6. Start: First feature using TDD

### Technical Lead Onboarding
1. Read: `Architecture/SYSTEM_ARCHITECTURE.md`
2. Read: `Architecture/DESIGN_DECISIONS.md`
3. Review: Phase reports to understand progression
4. Understand: How architect skill validates designs
5. Review: TDD integration

---

## 📞 Questions?

**If you can't find something**, ask:
- "What design principles guide storage implementation?" → Check `Architecture/STORAGE_LAYER.md`
- "How should I structure my API endpoint?" → Check `Architecture/API_DESIGN.md`
- "What's the approved way to handle errors?" → Check `Architecture/DESIGN_DECISIONS.md`
- "How do I implement this feature?" → Use TDD from `Guides/TDD_WORKFLOW.md` + consult architect

---

## 🏆 Philosophy

> **Documentation is not a burden. It's your competitive advantage.**
> 
> When design decisions are documented:
> - New developers onboard 10x faster
> - Mistakes are prevented (no rediscovering bad patterns)
> - Refactoring is safe (documentation proves original intent)
> - Quality is consistent (everyone follows same patterns)
> - Decisions are traceable (why was X chosen? Check documentation)

**This documentation folder is your project's brain.** 🧠

---

**Last Updated**: 2026-05-03
**Status**: Operational (6 folders, 20+ documents)
**Maintained By**: Architect Skill + Development Team
