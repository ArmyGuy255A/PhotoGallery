# Playwright E2E Testing Expert Skill

A comprehensive guide to end-to-end testing PhotoGallery using Playwright with Page Objects, fixtures, authentication testing, and CI/CD integration.

## What is Playwright?

Playwright is a modern E2E testing framework that:
- Tests real user interactions (clicks, typing, navigation)
- Runs on Chrome, Firefox, Safari simultaneously
- Handles async operations (animations, API calls)
- Captures screenshots, videos, traces for debugging
- Fast, reliable, and easy to maintain
- Integrates with CI/CD pipelines

**Perfect for PhotoGallery testing:**
- Admin workflows (create albums, upload photos)
- Visitor flows (access with codes, view/download photos)
- Authentication (login, logout, token handling)
- Form validation and error states
- Responsive design across devices

## Quick Start

### Install & Setup

```bash
npm install --save-dev @playwright/test
npx playwright install          # Install browsers
npx playwright codegen http://localhost:4200  # Record tests
```

### Project Structure

```
e2e/
├── pages/          # Page Objects
├── fixtures/       # Setup/teardown
└── *.spec.ts       # Test files
```

### First Test

```typescript
// e2e/auth.spec.ts
import { test, expect } from '@playwright/test';

test('user can login', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('text=PhotoGallery')).toBeVisible();
});
```

### Run Tests

```bash
npx playwright test                    # Run all
npx playwright test --ui               # Interactive mode
npx playwright test --project=chromium # Single browser
npx playwright show-report             # View results
```

## Core Concepts

### Test Structure: Arrange-Act-Assert

```typescript
test('admin can create album', async ({ page }) => {
  // 1. Arrange (setup)
  await page.goto('/albums');

  // 2. Act (user interaction)
  await page.click('[data-testid="create-button"]');
  await page.fill('[data-testid="title"]', 'My Album');
  await page.click('[data-testid="save-button"]');

  // 3. Assert (verify result)
  await expect(page.locator('text=Album created')).toBeVisible();
});
```

### Locators (Finding Elements)

```typescript
// Preferred: data-testid (most reliable)
page.locator('[data-testid="create-button"]');

// By role (accessible)
page.locator('button:has-text("Create")');

// By text
page.locator('text=Create Album');

// By CSS selector
page.locator('.btn-primary');

// Combined
page.locator('[data-testid="modal"] >> button[type="submit"]');
```

**Best Practice:** Add `data-testid` to your HTML:
```html
<button data-testid="create-button">Create</button>
<input data-testid="album-title" type="text">
```

### Page Objects (Organization Pattern)

Page Objects encapsulate UI interactions:

```typescript
// pages/albums.page.ts
export class AlbumsPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto('/albums');
  }

  async clickCreate() {
    await this.page.locator('[data-testid="create-button"]').click();
  }

  async verifyAlbumExists(title: string) {
    await expect(this.page.locator(`text=${title}`)).toBeVisible();
  }
}

// In test:
const albumsPage = new AlbumsPage(page);
await albumsPage.goto();
await albumsPage.clickCreate();
```

### Assertions

```typescript
// Visibility
await expect(page.locator('.success')).toBeVisible();
await expect(page.locator('.error')).toBeHidden();

// Text
await expect(page.locator('h1')).toHaveText('Albums');

// Value
await expect(page.locator('input')).toHaveValue('test@gmail.com');

// State
await expect(page.locator('button')).toBeEnabled();
await expect(page.locator('checkbox')).toBeChecked();

// Count
await expect(page.locator('.album')).toHaveCount(3);

// URL
await expect(page).toHaveURL('/albums');

// Custom
expect(value).toBe(expected);
expect(array).toContain(item);
```

## Common PhotoGallery Test Scenarios

### Authentication

```typescript
test('admin auto-login with DISABLE_AUTH', async ({ page }) => {
  // DISABLE_AUTH=true in development auto-logs in testadmin@localhost
  await page.goto('/');
  await expect(page.locator('[data-testid="user-menu"]')).toBeVisible();
  await expect(page.locator('[data-testid="create-album-button"]')).toBeVisible();
});

test('user can logout', async ({ page }) => {
  await page.locator('[data-testid="logout-button"]').click();
  await expect(page.locator('[data-testid="login-button"]')).toBeVisible();
});
```

### Album Management

```typescript
test('admin can create album', async ({ page }) => {
  await page.goto('/albums');
  await page.click('[data-testid="create-button"]');
  await page.fill('[data-testid="title"]', 'Beach Photos');
  await page.click('[data-testid="save-button"]');
  await expect(page.locator('text=Album created')).toBeVisible();
});

test('admin can delete album', async ({ page }) => {
  await page.goto('/albums');
  await page.click('[data-testid="delete-button"]');
  await page.click('[data-testid="confirm-delete"]');
  await expect(page.locator('text=Album deleted')).toBeVisible();
});
```

### Photo Upload

```typescript
test('admin can upload photos', async ({ page }) => {
  await page.goto('/albums/1/upload');
  await page.setInputFiles('[data-testid="file-input"]', 'photo.jpg');
  await expect(page.locator('text=Upload complete')).toBeVisible();
});
```

### Visitor Access with Codes

```typescript
test('visitor can access album with code', async ({ browser }) => {
  const visitorPage = await browser.newPage();
  await visitorPage.goto('/code/SAMPLE-CODE-123/photos');
  await expect(visitorPage.locator('[data-testid="photo"]')).toHaveCount(5);
  await visitorPage.close();
});

test('visitor cannot upload photos', async ({ browser }) => {
  const visitorPage = await browser.newPage();
  await visitorPage.goto('/code/SAMPLE-CODE-123/photos');
  await expect(visitorPage.locator('[data-testid="upload-button"]')).toBeHidden();
  await visitorPage.close();
});
```

## Fixtures (Reusable Setup)

Fixtures provide setup/teardown for tests:

```typescript
// fixtures/auth.fixture.ts
import { test as base } from '@playwright/test';

export const test = base.extend({
  authenticatedPage: async ({ page }, use) => {
    // Setup: ensure authenticated
    await page.goto('/albums'); // Auto-logs in with DISABLE_AUTH
    
    // Use page in test
    await use(page);
    
    // Teardown: optional cleanup
    await page.close();
  },
});

export { expect } from '@playwright/test';

// Use in test:
import { test, expect } from './fixtures/auth.fixture';

test('authenticated test', async ({ authenticatedPage }) => {
  await expect(authenticatedPage.locator('[data-testid="create-button"]')).toBeVisible();
});
```

## Waiting for Elements

```typescript
// Wait for element to appear
await page.locator('[data-testid="success"]').waitFor({ state: 'visible' });

// Wait for navigation
await page.waitForNavigation();

// Wait for network idle
await page.waitForLoadState('networkidle');

// Wait for URL change
await page.waitForURL('/albums');

// Implicit wait (built-in to Playwright)
// Most interactions wait automatically
```

## CLI & Configuration

### Commands

```bash
npx playwright test                 # Run all tests
npx playwright test --ui            # Interactive mode
npx playwright test --debug         # Debugger
npx playwright test --grep "create" # Match pattern
npx playwright test --project=chromium
npx playwright show-report          # View HTML report
```

### Config (playwright.config.ts)

```typescript
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: true,
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: devices['Desktop Chrome'] },
    { name: 'firefox', use: devices['Desktop Firefox'] },
    { name: 'webkit', use: devices['Desktop Safari'] },
  ],
});
```

## CI/CD Integration (GitHub Actions)

```yaml
# .github/workflows/e2e-tests.yml
name: E2E Tests
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
      - run: cd FE.PhotoGallery && npm ci
      - run: cd FE.PhotoGallery && npx playwright install --with-deps
      - run: cd FE.PhotoGallery && npm run dev &
        env:
          DISABLE_AUTH: true
      - run: npx wait-on http://localhost:4200
      - run: cd FE.PhotoGallery && npm run test:e2e
      - uses: actions/upload-artifact@v3
        if: always()
        with:
          name: playwright-report
          path: FE.PhotoGallery/playwright-report/
```

## Related Skills

- **CoreUI Angular** - Components being tested
- **PhotoGallery Architect** - Code structure for tests
- **Clean Architecture** - Test organization patterns

## When to Use This Skill

**Writing tests for:**
- Admin workflows (album/photo management)
- Visitor access (code-based access)
- Authentication flows
- Form submissions and validation
- Responsive design
- Error conditions
- User interactions

**Setting up:**
- Test project structure
- Page Objects
- Fixtures
- CI/CD automation
- Test reporting

**Debugging:**
- Flaky tests
- Async issues
- Element locators
- Waiting strategies

## Key Files

**Configuration:**
- `playwright.config.ts` - Global config
- `e2e/` - Test files
- `e2e/pages/` - Page Objects
- `e2e/fixtures/` - Fixtures

**Scripts in package.json:**
```json
{
  "test:e2e": "playwright test",
  "test:e2e:ui": "playwright test --ui",
  "test:e2e:debug": "playwright test --debug"
}
```

## Next Steps

1. Read SKILL.md for complete patterns and examples
2. Install Playwright dependencies
3. Create page objects for PhotoGallery pages
4. Write tests for admin workflows
5. Write tests for visitor access
6. Add CI/CD integration
7. Run tests in GitHub Actions

---

For detailed code examples and advanced patterns, see **SKILL.md**.
