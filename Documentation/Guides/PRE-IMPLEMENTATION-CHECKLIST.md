# Pre-Implementation Checklist

**MANDATORY: Complete this checklist before any implementation or commit.**

This checklist ensures all work aligns with PhotoGallery's documentation-driven development system. Every developer and AI agent must follow this.

---

## Phase 1: Consultation (BEFORE Writing Any Code)

- [ ] **Read Design Decisions First**
  - Check: `Documentation/Architecture/DESIGN_DECISIONS.md`
  - Question: Is a similar problem already solved?
  - If YES → Reference that decision in your implementation
  - If NO → Proceed to Phase 2

- [ ] **Ask Architect If Design Is Unclear**
  - Consult: `photogallery-architect-skill`
  - Question: Does this design follow SOLID/DRY principles?
  - Question: Is this consistent with existing patterns?
  - Decision: Approved or needs revision?

- [ ] **Check File Placement**
  - Consult: `photogallery-documentation-skill`
  - Question: Where should this file/document belong?
  - Question: Does this follow our folder structure?
  - Never create files in project root unless explicitly approved

- [ ] **Plan Test Strategy**
  - Consult: `photogallery-tdd-unit-testing-skill`
  - Question: What test scenarios cover this feature?
  - Question: What edge cases should we test?
  - Decision: Test cases documented before coding?

---

## Phase 2: Implementation (TDD Workflow)

- [ ] **RED Phase: Write Failing Tests**
  - File: `PhotoGallery.Tests/`
  - Command: `dotnet test PhotoGallery.Tests`
  - Result: Tests FAIL (expected)
  - Commit: `git commit -m "test: Add failing tests for [feature]"`

- [ ] **GREEN Phase: Implement Minimal Code**
  - File: `PhotoGallery/` or `FE.PhotoGallery/`
  - Implement: Minimal code to pass RED tests
  - Command: `dotnet test PhotoGallery.Tests`
  - Result: Tests PASS ✓
  - Commit: `git commit -m "feat: Implement [feature]"`

- [ ] **BLUE Phase: Refactor While Tests Stay Green**
  - Improve: Code quality, clarity, performance
  - Command: After each change: `dotnet test PhotoGallery.Tests`
  - Result: Tests still PASS ✓
  - Commit: `git commit -m "refactor: [description]"`

---

## Phase 3: Documentation & Review

- [ ] **Document Your Design Decision (If New)**
  - File: `Documentation/Architecture/DESIGN_DECISIONS.md`
  - Add: New decision section or reference existing one
  - Include: Context, Decision, Rationale, Implications
  - Format: Follow existing template exactly
  - Link: Cross-reference related decisions

- [ ] **Architect Validates Your Work**
  - Consult: `photogallery-architect-skill`
  - Check: SOLID principles (Single Responsibility, Open/Closed, etc.)
  - Check: DRY principle (no duplication)
  - Check: Design matches documentation
  - Approval: Sign off before merging

- [ ] **Update Navigation Links**
  - Files: Touch any related documentation?
  - Update: Top and bottom navigation links
  - Check: `Documentation/INDEX.md` still accurate
  - Verify: No broken links

---

## Phase 4: Final Verification (Before Commit)

- [ ] **All Tests Pass**
  - Command: `dotnet test PhotoGallery.Tests`
  - Result: `Passed: N, Failed: 0, Errors: 0`
  - Nothing less is acceptable

- [ ] **No Compilation Errors**
  - Backend: `dotnet build PhotoGallery.sln`
  - Result: Build succeeds with 0 errors
  - Warnings are OK, errors are not

- [ ] **Code Follows PhotoGallery Style**
  - Naming: PascalCase for classes, camelCase for variables
  - Pattern: Follows existing code in same area
  - DI: Uses dependency injection (no `new` for services)
  - Testing: Mocks used for external dependencies

- [ ] **Commit Message References Design**
  - Format: `[type]: [description]`
  - Include: Reference to design decision
  - Example: `feat: Add image processing queue (D003)`
  - Footer: Always include Co-authored-by trailer

---

## Phase 5: Before Marking Task Complete

- [ ] **All Tests Still Passing**
  - Final run: `dotnet test PhotoGallery.Tests`
  - Verify: 0 failures, 0 errors

- [ ] **No Files Left in Project Root**
  - Check: `ls -la` (project root only)
  - All temp/debug files in `temp/`
  - All docs in `Documentation/`

- [ ] **Documentation Is Current**
  - Read: `Documentation/Architecture/DESIGN_DECISIONS.md`
  - Verify: Your design is documented
  - Verify: All links work

- [ ] **Ready for Code Review**
  - All checkboxes above: CHECKED ✓
  - Ready for merge
  - Ready for next developer to build on

---

## AI Agent Specific: Mandatory Skill Consultation

Every agent (human or AI) must follow this **non-negotiable** sequence:

1. **Before any work**: Read most recent checkpoint in `checkpoints/`
2. **Before writing code**: Consult `photogallery-architect-skill`
3. **Before writing tests**: Consult `photogallery-tdd-unit-testing-skill`
4. **Before creating files**: Consult `photogalloy-documentation-skill`
5. **Before committing**: Verify checklist above is complete
6. **Before marking done**: Verify all tests pass + no new files in project root

---

## Quick Reference: Skills to Consult

| Skill | When to Consult | File Location |
|-------|-----------------|----------------|
| **Architect** | Any design question, architecture concern, SOLID/DRY validation | `skills/photogallery-architect-skill/SKILL.md` |
| **TDD** | Test planning, test strategy, edge case coverage | `skills/tdd-unit-testing-skill/SKILL.md` |
| **Documentation** | File placement, documentation standards, Mermaid diagrams | `skills/photogallery-documentation-skill/SKILL.md` |
| **Backend Developer** | Implementation pattern, code style, dependency injection | `skills/backend-developer-skill/SKILL.md` |
| **Frontend Developer** | Angular/CoreUI patterns, component structure | `skills/photogallery-frontend-developer-skill/SKILL.md` |

---

## Documentation Navigation

- **Start here**: `Documentation/INDEX.md` (central hub)
- **Understand design**: `Documentation/Architecture/DESIGN_DECISIONS.md`
- **See system**: `Documentation/Architecture/SYSTEM_ARCHITECTURE.md`
- **Learn workflow**: `Documentation/Phase-Reports/DOCUMENTATION_SYSTEM_COMPLETE.md`
- **Get help**: `Documentation/Guides/` (TDD, CI/CD, Docker, etc.)

---

## Example: Following This Checklist

**Scenario: Implement "Add Album Feature"**

```
PHASE 1: CONSULTATION
✓ Read DESIGN_DECISIONS.md → Find D001, D005
✓ Ask architect → "This design fits both decisions ✓"
✓ Check file placement → "Put tests in PhotoGallery.Tests ✓"
✓ Plan tests → "Test: Create album, Get album, Update album, Delete album"

PHASE 2: IMPLEMENTATION (TDD)
✓ RED: Write 4 failing tests
✓ GREEN: Implement Album entity + Repository
✓ BLUE: Refactor to improve code clarity

PHASE 3: DOCUMENTATION
✓ Reference D001 and D005 in commit message
✓ Ask architect → "SOLID/DRY approved ✓"
✓ Verify links in documentation still work

PHASE 4: VERIFICATION
✓ dotnet test → All 17 tests passing ✓
✓ dotnet build → 0 errors ✓
✓ Code review checklist complete ✓

PHASE 5: MARK COMPLETE
✓ All tests passing
✓ No files in project root
✓ Documentation current
✓ Ready for merge
```

---

**Last Updated**: 2026-05-03  
**Status**: Active - All developers must follow  
**Related**: See `Documentation/INDEX.md` for full navigation

[← Back to INDEX](../INDEX.md)
