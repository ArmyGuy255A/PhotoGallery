---
name: photogallery-playwright
description: |
  End-to-end testing expertise for PhotoGallery using Playwright. This skill covers E2E test structure, page objects, fixtures, user flows, assertions, visual regression testing, authentication testing, CI/CD integration, and reporting. Use this whenever writing E2E tests for PhotoGallery, testing UI components, automating user workflows, verifying authentication flows, testing photo uploads, validating album management, or setting up test automation in GitHub Actions. Explains how to test across browsers (Chrome, Firefox, Safari), handle asynchronous operations, test responsive design, and generate test reports.

  This skill delegates to copilot-dev-team plugin meta-skills: `playwright-bootstrap` (first-time install / config / runner), `playwright-test-recipe` (canonical test + page-object pattern + auth fixture), and `app-jwt-claims` (JWT shape used by the auth fixture). Auto-trigger these when their conditions match. Plugin meta-skills are canonical — prefer them on conflict.
---

# Playwright E2E Testing Guide for PhotoGallery

## Plugin Meta-Skills

The `copilot-dev-team` plugin's `playwright-bootstrap` and `playwright-test-recipe` are the canonical references for installing Playwright and authoring tests. This skill focuses on PhotoGallery-specific flows (login → gallery, admin → upload, access-code → public view); it defers to the plugin meta-skills for the underlying patterns.

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| First-time Playwright setup in the repo | `playwright-bootstrap` | — |
| Authoring a new e2e test or page-object | `playwright-test-recipe` | — |
| Triaging a flaky e2e test | `playwright-test-recipe` | — |
| Building the sign-in / auth fixture | `playwright-test-recipe` | `app-jwt-claims`, `identity-and-jwt` |
| Reading runtime env (API URL) in tests | — | `runtime-env-config` |

**Workflow callouts:**

- *→ Setup / install / config sections — consult `playwright-bootstrap`.*
- *→ Test authoring / page-object sections — consult `playwright-test-recipe`.*
- *→ Auth fixture / sign-in helper sections — consult `playwright-test-recipe` + `app-jwt-claims`.*

## What is Playwright?

Playwright is a modern end-to-end testing framework that:
- ✅ Tests real user interactions (clicks, typing, navigation)
- ✅ Runs on Chrome, Firefox, Safari simultaneously
- ✅ Handles async operations (waiting for elements, animations)
- ✅ Captures screenshots and videos for debugging
- ✅ Generates detailed reports and traces
- ✅ Integrates with CI/CD (GitHub Actions)
- ✅ Tests responsive design (mobile, tablet, desktop)
- ✅ Runs fast and reliable tests

**PhotoGallery uses Playwright to:**
- Test admin workflows (create album, upload photos, generate access codes)
- Test visitor flows (access album with code, download photos)
- Verify authentication (login with OAuth, token handling)
- Validate form submissions
- Test responsive design

## Installation & Setup

→ **Consult `playwright-bootstrap`** for first-time Playwright setup in a fresh repo: workspace structure, `package.json`, `playwright.config.ts`, browser installation, CI scaffolding.

### Install Playwright

```bash
cd FE.PhotoGallery
npm install --save-dev @playwright/test
npx playwright install  # Install browser binaries
```

### Initialize Playwright Project

```bash
npx playwright codegen http://localhost:4200
# Opens browser for you to record interactions
```

### Project Structure

```
FE.PhotoGallery/
├── e2e/
│   ├── auth.spec.ts              # Authentication tests
│   ├── album-management.spec.ts   # Album CRUD tests
│   ├── photo-upload.spec.ts       # Photo upload tests
│   ├── visitor-access.spec.ts     # Access code tests
│   ├── fixtures/
│   │   ├── auth.fixture.ts        # Auth setup/teardown
│   │   └── data.fixture.ts        # Test data
│   └── pages/
│       ├── base.page.ts           # Page object base class
│       ├── login.page.ts          # Login page object
│       ├── albums.page.ts         # Albums list page
│       ├── album-detail.page.ts   # Album detail page
│       └── upload.page.ts         # Photo upload page
├── playwright.config.ts           # Playwright configuration
└── package.json
```

### Configuration File

```typescript
// playwright.config.ts
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] },
    },
    // Mobile browsers
    {
      name: 'Mobile Chrome',
      use: { ...devices['Pixel 5'] },
    },
    {
      name: 'Mobile Safari',
      use: { ...devices['iPhone 12'] },
    },
  ],

  // Web server configuration
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env.CI,
  },
});
```

## Core Concepts

### Test Structure

Every Playwright test has this structure:

```typescript
import { test, expect } from '@playwright/test';

test('user can create an album', async ({ page }) => {
  // 1. Setup - go to page
  await page.goto('/');

  // 2. Action - interact with page
  await page.fill('[data-testid="album-title"]', 'My Album');
  await page.click('[data-testid="create-button"]');

  // 3. Assert - verify result
  await expect(page.locator('text=My Album')).toBeVisible();
});
```

**Three Parts:**
1. **Setup** - Navigate to page, prepare data
2. **Action** - User interactions (click, type, navigate)
3. **Assert** - Verify expected results

### Locators (Finding Elements)

```typescript
// By test ID (preferred)
page.locator('[data-testid="album-title"]');

// By role (accessible)
page.locator('button:has-text("Create")');

// By CSS selector
page.locator('.album-card');

// By XPath
page.locator('//button[contains(text(), "Create")]');

// By text
page.locator('text=Create Album');

// By placeholder
page.locator('[placeholder="Album title"]');

// By label (for form inputs)
page.locator('label:has-text("Title") >> .. >> input');

// Combined selectors
page.locator('[data-testid="modal"] >> button:has-text("Save")');
```

**Best Practice:** Use `data-testid` attribute in your HTML:
```html
<input data-testid="album-title" type="text">
<button data-testid="create-button">Create Album</button>
```

### Assertions

```typescript
// Visibility
await expect(page.locator('.success-message')).toBeVisible();
await expect(page.locator('.error-message')).toBeHidden();

// Text content
await expect(page.locator('h1')).toHaveText('Albums');
await expect(page.locator('h1')).toContainText('Album');

// Values
await expect(page.locator('input#email')).toHaveValue('user@gmail.com');
await expect(page.locator('select')).toHaveValue('admin');

// State
await expect(page.locator('button')).toBeEnabled();
await expect(page.locator('button')).toBeDisabled();
await expect(page.locator('checkbox')).toBeChecked();

// Count
await expect(page.locator('.album-card')).toHaveCount(3);
await expect(page.locator('.album-card')).toHaveCount(0);

// Attribute
await expect(page.locator('img')).toHaveAttribute('alt', 'Album preview');

// CSS class
await expect(page.locator('button')).toHaveClass(/active/);

// URL
await expect(page).toHaveURL('/albums/123');
await expect(page).toHaveURL(/albums\/\d+/);

// Custom assertion
await expect(async () => {
  const count = await page.locator('.photo').count();
  expect(count).toBeGreaterThan(0);
}).toPass();
```

### Page Objects (Recommended Pattern)

→ **Consult `playwright-test-recipe`** for the canonical Page Object Model, locator strategy (semantic first: `getByRole`, `getByLabel`, then `getByTestId`), and page-object class structure. This section shows PhotoGallery-specific page objects; the plugin recipe covers the underlying patterns.

**Base Page Object:**
```typescript
// pages/base.page.ts
import { Page } from '@playwright/test';

export class BasePage {
  readonly page: Page;

  constructor(page: Page) {
    this.page = page;
  }

  async goto(path: string) {
    await this.page.goto(path);
  }

  async waitForNavigation(action: () => Promise<void>) {
    await Promise.all([
      this.page.waitForNavigation(),
      action()
    ]);
  }

  async isVisible(locator: string) {
    return this.page.locator(locator).isVisible();
  }
}
```

**Albums Page Object:**
```typescript
// pages/albums.page.ts
import { BasePage } from './base.page';
import { expect } from '@playwright/test';

export class AlbumsPage extends BasePage {
  // Locators
  readonly createButton = this.page.locator('[data-testid="create-album-button"]');
  readonly albumCards = this.page.locator('[data-testid="album-card"]');
  readonly noAlbumsMessage = this.page.locator('text=No albums found');

  async goto() {
    await super.goto('/albums');
  }

  async clickCreateAlbum() {
    await this.createButton.click();
  }

  async getAlbumCount() {
    return this.albumCards.count();
  }

  async getFirstAlbumTitle() {
    return this.albumCards.first().locator('[data-testid="album-title"]').textContent();
  }

  async clickEditAlbum(index: number) {
    const albumCard = this.albumCards.nth(index);
    await albumCard.locator('[data-testid="edit-button"]').click();
  }

  async clickDeleteAlbum(index: number) {
    const albumCard = this.albumCards.nth(index);
    await albumCard.locator('[data-testid="delete-button"]').click();
  }

  async verifyAlbumExists(title: string) {
    await expect(this.page.locator(`text=${title}`)).toBeVisible();
  }

  async verifyNoAlbums() {
    await expect(this.noAlbumsMessage).toBeVisible();
  }
}
```

**Using Page Objects in Tests:**
```typescript
import { test, expect } from '@playwright/test';
import { AlbumsPage } from './pages/albums.page';
import { CreateAlbumModal } from './pages/create-album.modal';

test('user can create an album', async ({ page }) => {
  const albumsPage = new AlbumsPage(page);
  const createModal = new CreateAlbumModal(page);

  await albumsPage.goto();
  await albumsPage.clickCreateAlbum();
  
  await createModal.fillTitle('Beach Wedding');
  await createModal.fillDescription('Wedding photos from the beach');
  await createModal.clickSave();

  await albumsPage.verifyAlbumExists('Beach Wedding');
});
```

## Testing Common PhotoGallery Workflows

### 1. Authentication Testing

→ **Consult `playwright-test-recipe` + `app-jwt-claims`** for auth fixture patterns, test-token endpoint minting (backend-minted JWT), storage state, and role-based fixtures. PhotoGallery uses `DISABLE_AUTH=true` in dev; production tests rely on the backend test-token endpoint.

```typescript
import { test, expect } from '@playwright/test';

test.describe('Authentication', () => {
  test('admin can login with development bypass', async ({ page }) => {
    // Development mode: DISABLE_AUTH=true auto-logs in testadmin@localhost
    await page.goto('/');
    
    // Should be logged in automatically
    await expect(page.locator('[data-testid="user-menu"]')).toBeVisible();
    
    // Should show admin controls
    await expect(page.locator('[data-testid="create-album-button"]')).toBeVisible();
  });

  test('logout removes authentication', async ({ page }) => {
    await page.goto('/');
    
    // Verify logged in
    await expect(page.locator('[data-testid="user-menu"]')).toBeVisible();
    
    // Click logout
    await page.locator('[data-testid="logout-button"]').click();
    
    // Should redirect to login or show login button
    await page.waitForURL('/');
    await expect(page.locator('[data-testid="login-button"]')).toBeVisible();
  });

  test('unauthenticated user cannot access admin pages', async ({ page }) => {
    await page.goto('/admin/albums', { waitUntil: 'networkidle' });
    
    // Should redirect to login
    await expect(page).toHaveURL('/login');
  });
});
```

### 2. Album Management Testing

```typescript
test.describe('Album Management', () => {
  test('admin can create album', async ({ page }) => {
    const albumsPage = new AlbumsPage(page);
    const createModal = new CreateAlbumModal(page);

    await albumsPage.goto();
    await albumsPage.clickCreateAlbum();
    
    await createModal.fillTitle('Summer Vacation');
    await createModal.fillDescription('2024 Summer trip to Italy');
    await createModal.clickSave();

    await expect(page.locator('text=Album created successfully')).toBeVisible();
    await albumsPage.verifyAlbumExists('Summer Vacation');
  });

  test('admin can edit album', async ({ page }) => {
    const albumsPage = new AlbumsPage(page);
    const editModal = new EditAlbumModal(page);

    await albumsPage.goto();
    await albumsPage.clickEditAlbum(0);
    
    await editModal.fillTitle('Updated Title');
    await editModal.clickSave();

    await expect(page.locator('text=Album updated')).toBeVisible();
    await albumsPage.verifyAlbumExists('Updated Title');
  });

  test('admin can delete album', async ({ page, context }) => {
    const albumsPage = new AlbumsPage(page);

    await albumsPage.goto();
    const initialCount = await albumsPage.getAlbumCount();
    
    await albumsPage.clickDeleteAlbum(0);
    
    // Confirm deletion in dialog
    await page.locator('[data-testid="confirm-delete"]').click();

    await expect(page.locator('text=Album deleted')).toBeVisible();
    const finalCount = await albumsPage.getAlbumCount();
    expect(finalCount).toBe(initialCount - 1);
  });

  test('album list shows correct photo counts', async ({ page }) => {
    const albumsPage = new AlbumsPage(page);

    await albumsPage.goto();
    
    const photoCount = await page.locator('[data-testid="photo-count"]').first().textContent();
    expect(photoCount).toMatch(/^\d+$/);
  });
});
```

### 3. Photo Upload Testing

```typescript
test.describe('Photo Upload', () => {
  test('admin can upload single photo', async ({ page }) => {
    const uploadPage = new UploadPage(page);

    await uploadPage.goto('/albums/1/upload');
    
    // Upload a file
    await uploadPage.uploadFile('./test-assets/sample-photo.jpg');
    
    // Verify upload progress
    await expect(page.locator('[data-testid="upload-progress"]')).toBeVisible();
    
    // Wait for completion
    await expect(page.locator('text=Upload complete')).toBeVisible();
    
    // Verify photo appears in gallery
    await expect(page.locator('[data-testid="photo-thumbnail"]')).toHaveCount(1);
  });

  test('admin can upload multiple photos', async ({ page }) => {
    const uploadPage = new UploadPage(page);

    await uploadPage.goto('/albums/1/upload');
    
    // Upload multiple files
    const filePath = ['photo1.jpg', 'photo2.jpg', 'photo3.jpg'];
    await uploadPage.uploadFiles(filePath);
    
    // Wait for all uploads
    await expect(page.locator('text=3 photos uploaded')).toBeVisible();
    
    // Verify all photos appear
    await expect(page.locator('[data-testid="photo-thumbnail"]')).toHaveCount(3);
  });

  test('upload form rejects invalid file types', async ({ page }) => {
    const uploadPage = new UploadPage(page);

    await uploadPage.goto('/albums/1/upload');
    
    // Try to upload non-image file
    await uploadPage.uploadFile('./test-assets/document.pdf');
    
    // Should show error
    await expect(page.locator('text=Only image files allowed')).toBeVisible();
  });
});
```

### 4. Access Code Testing

```typescript
test.describe('Access Codes (Visitor Access)', () => {
  test('admin can generate access code for album', async ({ page }) => {
    const albumDetailPage = new AlbumDetailPage(page);
    const accessCodeModal = new AccessCodeModal(page);

    await albumDetailPage.goto('/albums/1');
    await albumDetailPage.clickGenerateAccessCode();
    
    // Fill in access code details
    await accessCodeModal.setExpirationDays(7);
    await accessCodeModal.clickGenerate();

    // Verify code is displayed
    const code = await accessCodeModal.getGeneratedCode();
    expect(code).toBeTruthy();
    expect(code).toMatch(/^[A-Z0-9-]+$/);
  });

  test('visitor can access album with valid code', async ({ page, context }) => {
    // Create new browser context (simulate visitor in incognito)
    const visitorPage = await context.newPage();

    // Access album with code
    await visitorPage.goto('/code/SAMPLE-CODE-123/photos');

    // Should see photos without authentication
    await expect(visitorPage.locator('[data-testid="photo-grid"]')).toBeVisible();
    await expect(visitorPage.locator('[data-testid="photo-thumbnail"]')).toHaveCount(5);

    // Should NOT see admin controls
    await expect(visitorPage.locator('[data-testid="upload-button"]')).toBeHidden();
    await expect(visitorPage.locator('[data-testid="delete-button"]')).toBeHidden();

    await visitorPage.close();
  });

  test('visitor cannot access album with invalid code', async ({ page }) => {
    await page.goto('/code/INVALID-CODE-XYZ/photos');

    // Should show error
    await expect(page.locator('text=Invalid or expired access code')).toBeVisible();
    
    // Should redirect or show error page
    await expect(page.locator('[data-testid="photo-grid"]')).toBeHidden();
  });

  test('expired access code is rejected', async ({ page }) => {
    // Create an access code that expired
    await page.goto('/code/EXPIRED-CODE-123/photos');

    // Should show expiration error
    await expect(page.locator('text=This access code has expired')).toBeVisible();
  });
});
```

## Fixtures (Setup & Teardown)

Fixtures provide reusable test setup:

```typescript
// fixtures/auth.fixture.ts
import { test as base } from '@playwright/test';
import { LoginPage } from '../pages/login.page';

type AuthFixture = {
  authenticatedPage: Page;
};

export const test = base.extend<AuthFixture>({
  authenticatedPage: async ({ page }, use) => {
    // Setup: login before test
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    
    // DISABLE_AUTH=true auto-logs in, or manually login
    await page.goto('/albums'); // Will redirect to login if needed
    
    // Login happens automatically with DISABLE_AUTH
    await page.waitForURL('/albums');

    // Use authenticated page in test
    await use(page);

    // Teardown: cleanup after test
    // (Optional: logout, clear data, etc.)
  },
});

export { expect } from '@playwright/test';
```

**Using Fixtures:**
```typescript
import { test, expect } from './fixtures/auth.fixture';

test('authenticated user can see albums', async ({ authenticatedPage }) => {
  await expect(authenticatedPage.locator('[data-testid="album-card"]')).toHaveCount(3);
});
```

## Running Tests

### CLI Commands

```bash
# Run all tests
npx playwright test

# Run specific test file
npx playwright test e2e/album-management.spec.ts

# Run tests matching pattern
npx playwright test --grep "can create"

# Run specific browser
npx playwright test --project=chromium

# Run with UI mode (interactive)
npx playwright test --ui

# Run with debug mode
npx playwright test --debug

# Update snapshots
npx playwright test --update-snapshots

# Show test report
npx playwright show-report
```

### package.json Scripts

```json
{
  "scripts": {
    "test:e2e": "playwright test",
    "test:e2e:ui": "playwright test --ui",
    "test:e2e:debug": "playwright test --debug",
    "test:e2e:chromium": "playwright test --project=chromium",
    "test:e2e:firefox": "playwright test --project=firefox",
    "test:e2e:webkit": "playwright test --project=webkit",
    "test:e2e:mobile": "playwright test --project='Mobile Chrome'",
    "test:e2e:report": "playwright show-report"
  }
}
```

## Advanced Features

### Visual Regression Testing

```typescript
test('album card looks correct', async ({ page }) => {
  const albumsPage = new AlbumsPage(page);
  
  await albumsPage.goto();
  
  // Take screenshot and compare with baseline
  await expect(page.locator('[data-testid="album-card"]').first())
    .toHaveScreenshot('album-card.png');
});
```

### Waiting for Elements

```typescript
// Wait for element to appear
await page.locator('[data-testid="success-message"]').waitFor({ state: 'visible' });

// Wait for navigation
await page.waitForNavigation();

// Wait for network requests
await page.waitForLoadState('networkidle');

// Wait for specific URL
await page.waitForURL('/albums/123');

// Wait with timeout
await page.locator('[data-testid="modal"]').waitFor({ timeout: 5000 });
```

### Handling Async Operations

```typescript
test('async photo upload completes', async ({ page }) => {
  const uploadPage = new UploadPage(page);

  await uploadPage.goto('/albums/1/upload');
  
  // Start upload and wait for completion in parallel
  const uploadPromise = page.waitForNavigation(); // Album detail page
  await uploadPage.uploadFile('photo.jpg');
  await uploadPage.clickConfirm();
  
  // Wait for both
  await uploadPromise;
});
```

### Testing Error States

```typescript
test('form shows validation errors', async ({ page }) => {
  const createModal = new CreateAlbumModal(page);

  // Try to submit empty form
  await createModal.clickSave();

  // Verify error messages
  await expect(page.locator('[data-testid="error-title"]')).toBeVisible();
  await expect(page.locator('[data-testid="error-title"]'))
    .toHaveText('Title is required');
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
      
      - name: Setup Node.js
        uses: actions/setup-node@v3
        with:
          node-version: 18
          cache: 'npm'
          cache-dependency-path: 'FE.PhotoGallery/package-lock.json'
      
      - name: Install dependencies
        run: cd FE.PhotoGallery && npm ci
      
      - name: Install Playwright browsers
        run: cd FE.PhotoGallery && npx playwright install --with-deps
      
      - name: Start development server
        run: cd FE.PhotoGallery && npm run dev &
        env:
          DISABLE_AUTH: true
      
      - name: Wait for server
        run: npx wait-on http://localhost:4200
      
      - name: Run E2E tests
        run: cd FE.PhotoGallery && npm run test:e2e
      
      - name: Upload report
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: playwright-report
          path: FE.PhotoGallery/playwright-report/
          retention-days: 30
```

## Test Organization Best Practices

```typescript
// ✅ Good: Descriptive test names
test('admin can create album with title and description', async ({ page }) => {
  // ...
});

// ❌ Bad: Vague test names
test('test album creation', async ({ page }) => {
  // ...
});

// ✅ Good: Arrange-Act-Assert pattern
test('album deletion shows confirmation dialog', async ({ page }) => {
  // Arrange
  const albumsPage = new AlbumsPage(page);
  await albumsPage.goto();

  // Act
  await albumsPage.clickDeleteAlbum(0);

  // Assert
  await expect(page.locator('[data-testid="confirm-dialog"]')).toBeVisible();
});

// ✅ Good: Test one thing
test('delete button removes album', async ({ page }) => {
  // Only tests deletion
});

// ❌ Bad: Test multiple things
test('user can create, edit, and delete album', async ({ page }) => {
  // Tests too many behaviors
});

// ✅ Good: Use data-testid for reliability
await page.locator('[data-testid="create-button"]').click();

// ❌ Bad: Fragile selectors
await page.locator('.btn-primary').click();
```

## Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| Test flakes (intermittent failures) | Use proper waits instead of sleeps, use waitFor(), proper assertions |
| Timeouts | Increase timeout, check locator is correct, verify element exists |
| Element not clickable | Wait for element to be visible/enabled, scroll into view if needed |
| State issues between tests | Use proper fixtures and test isolation, don't rely on test order |
| CI tests fail but local pass | Check DISABLE_AUTH is set, verify baseURL, check for environment differences |

---

**Key Takeaway:** Playwright makes it easy to test real user workflows. Use Page Objects for maintainability, data-testid for reliability, fixtures for setup, and run tests in CI/CD for confidence that features work.


## Cross-cutting plugin skills (always-on)

These copilot-dev-team meta-skills apply regardless of phase:

- `scratch-discipline` — exploratory Playwright probes in .copilot/scratch/<task-id>/.
- `secret-hygiene` — never hardcode test passwords / tokens; use env or fixture-issued tokens.
- `commit-conventions` — canonical commit-message format.
- `branch-strategy-u-prefix` — `u/<actor>/<type>/<scope>` branches only, **targeting `trial`**. PRs into `main` come only from `trial`. See `Documentation/Architecture/DESIGN_DECISIONS.md` D016.
- `copilot-memory-update` — record durable e2e policy decisions (browser matrix, retries).


## PR-Validation Workflow (executor view)

The `pg-qa-quality-control` agent owns the end-to-end PR-validation flow (see `qa-quality-control-skill` for the orchestrator view). This skill / `pg-playwright-tester` agent is the **executor** for Steps 4 (author missing specs) and 5 (run the suite).

> *→ Authoring patterns — consult `playwright-test-recipe` (plugin canonical).*
> *→ First-time setup — consult `playwright-bootstrap`.*

### Step 4: Author missing specs

After the QA orchestrator hands you a list of user-visible changes (from the PR diff), for each change:

1. **Locate the matching feature** — e.g., a new `AlbumShareDialog` component in FE, a new `POST /api/albums/{id}/share` endpoint in BE.
2. **Check existing coverage** — search `tests/e2e/` for tests that exercise the affected route or component:
   ```pwsh
   Select-String -Path tests/e2e/tests/**/*.spec.ts -Pattern '(AlbumShare|albums/.*/share)'
   ```
3. **Author a spec** following the project's page-object conventions (one class per page under `tests/e2e/tests/pages/`, semantic locators preferred, storage-state auth fixture). See `playwright-test-recipe` for the canonical pattern.
4. **Use `data-testid` only when no semantic locator exists.** If you need one, request the FE change from `pg-angular-coreui-dev` rather than adding it yourself.
5. **One spec per behavior.** A `share-dialog opens and emits the correct payload` is one test. A `share dialog handles invalid emails` is another test, not a branch in the first.

### Step 5: Run the suite

```pwsh
Push-Location tests/e2e
# First run on a fresh checkout / new branch only:
npx playwright install --with-deps
# Scoped run for fast feedback:
npx playwright test --grep "<feature-name>" --reporter=list
# Full suite before declaring qa-passed:
npx playwright test --reporter=list,html
Pop-Location
```

Capture artifacts:
- `tests/e2e/playwright-report/index.html` — for the PR comment link.
- `tests/e2e/test-results/` — traces and screenshots on failure.

### What you hand back to the orchestrator

A structured summary:

```
{
  "specsAdded": ["tests/e2e/tests/album-share.spec.ts"],
  "results": { "passed": 42, "failed": 1, "skipped": 0 },
  "failures": [
    { "test": "share dialog > handles invalid emails", "reason": "expected error message not visible", "repro": "npx playwright test album-share --grep 'invalid emails'" }
  ],
  "reportPath": "tests/e2e/playwright-report/index.html"
}
```

The orchestrator then composes the PR comment (Step 6 in `qa-quality-control-skill`).

### What you don't do

- Don't post the PR comment yourself — that's the orchestrator's job (so the comment is single-source).
- Don't fix the production code that caused the failure — file the failure summary and hand back.
- Don't invent stack-state fixtures for new auth flows without coordinating with `pg-aspnet-backend-dev`.
