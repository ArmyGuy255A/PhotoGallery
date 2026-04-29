# QA Quality Control Skill

Expert guide for end-to-end testing and quality assurance of PhotoGallery using Playwright.

## Quick Links

- **SKILL.md** - Complete QA testing guide
- **QUICK_REFERENCE.md** - One-page cheat sheet for test patterns
- **COMPLETION_CHECKLIST.md** - Quality verification before release

## When to Use This Skill

Use this skill when:

- **Planning Test Coverage** - Identify all workflows to test
- **Writing E2E Tests** - Create Playwright test cases
- **Testing Authentication** - Verify login flows and guards
- **Testing Features** - Validate albums, photos, access codes
- **Accessibility Testing** - Verify WCAG 2.1 AA compliance
- **Multi-Browser Testing** - Test Chrome, Firefox, Safari, mobile
- **Regression Testing** - Verify previous features still work
- **CI/CD Setup** - Configure GitHub Actions for automated testing

## Key Phases Covered

- **Phase 9:** Testing infrastructure setup, test fixtures, Page Objects
- **Feature Validation:** E2E tests for all PhotoGallery workflows
- **Pre-Release:** Quality assurance checklist

## Test Structure

```
tests/e2e/
├── pages/              # Page Objects encapsulate selectors/actions
│   ├── login.page.ts
│   ├── albums.page.ts
│   ├── photo-upload.page.ts
│   ├── access-code.page.ts
│   └── photo-gallery.page.ts
├── fixtures/           # Setup/teardown, auth fixture
│   └── auth.fixture.ts
├── helpers/            # Utility functions
├── auth.spec.ts        # Authentication tests
├── albums.spec.ts      # Album CRUD tests
├── photo-upload.spec.ts# Photo upload tests
├── visitor-access.spec.ts # Access code + gallery tests
├── accessibility.spec.ts # WCAG 2.1 AA tests
└── responsive.spec.ts  # Mobile/tablet/desktop tests
```

## Key Patterns

### Page Objects
- Encapsulate selectors and actions
- One class per page/component
- Methods represent user actions (clickCreate, fillForm, etc.)
- Improve test maintainability

### Fixtures
- Setup/teardown logic (authentication, test data)
- Reused across multiple tests
- Provide cleaned state for each test

### Multi-Browser Testing
- Test on Chrome, Firefox, Safari, mobile variants
- Playwright runs all browsers in parallel
- Configuration in playwright.config.ts

### Accessibility Testing
- Use axe-playwright for automated checks
- Test semantic HTML and ARIA labels
- Verify WCAG 2.1 AA compliance

## Typical Workflow

1. **Setup Phase** - Install Playwright, create fixtures
2. **Page Objects** - Define pages and their interactions
3. **Auth Tests** - Verify login, guards, redirects
4. **Feature Tests** - Test albums, photos, access codes
5. **Accessibility** - Run accessibility checks
6. **Responsive** - Test mobile/tablet/desktop
7. **Performance** - Verify load times
8. **CI/CD** - Automate tests in GitHub Actions

## Development vs CI

### Development
```bash
# Watch mode - re-run tests as you code
npm run test:e2e -- --watch

# Single browser for faster feedback
npm run test:e2e -- --project=chromium

# Debug specific test
npm run test:e2e -- --debug tests/e2e/auth.spec.ts
```

### CI
```bash
# All browsers in parallel
npm run test:e2e

# Generate HTML report
npm run test:e2e -- --reporter=html
```

## Related Skills

- **playwright-testing-skill** - Core testing patterns and techniques
- **backend-developer-skill** - Understand API contracts for testing
- **frontend-developer-skill** - Understand component selectors and flow
- **photogallery-auth-skill** - Understand authentication mechanism

## Before You Start

1. Read `playwright-testing-skill` for test patterns
2. Read `backend-developer-skill` to understand APIs
3. Read `frontend-developer-skill` to understand components
4. Install Playwright: `npm install -D @playwright/test`
5. Create test directory structure

## Example Test

```typescript
import { test, expect } from './fixtures/auth.fixture';
import { AlbumsPage } from './pages/albums.page';

test('should create album', async ({ authenticatedPage }) => {
  const albumsPage = new AlbumsPage(authenticatedPage);
  
  await albumsPage.goto();
  await albumsPage.clickCreateAlbum();
  await albumsPage.fillAlbumForm('My Album', 'Test description');
  await albumsPage.clickCreate();
  
  expect(await albumsPage.verifyAlbumExists('My Album')).toBeTruthy();
});
```

## Key Test Scenarios

- Login/logout workflows
- Album creation/editing/deletion
- Photo upload with progress
- Access code generation (temporary, custom, permanent)
- Visitor gallery access with code
- Photo download with quality selection
- Authorization (admin vs user)
- Mobile responsiveness
- Accessibility compliance

## Support

For questions about:
- **Test patterns** → Consult `playwright-testing-skill`
- **Backend endpoints** → Consult `backend-developer-skill`
- **Frontend selectors** → Consult `frontend-developer-skill`
- **Architecture** → Consult `photogallery-architect-skill`

---

**Dispatch this agent for Phase 9+ testing work and quality validation.**
