---
name: qa-quality-control
description: |
  Expert quality control and E2E testing guide for PhotoGallery using Playwright. Use this skill to validate PhotoGallery features end-to-end, ensure workflows work correctly, test accessibility compliance, and verify multi-browser compatibility. This skill orchestrates comprehensive testing using the playwright-testing-skill and validates that all features (auth, albums, photos, access codes) work correctly across browsers and devices. Covers test planning, test case creation, E2E workflow validation, accessibility testing, multi-browser testing, and CI/CD integration. Includes step-by-step guidance for testing: authentication flows, album CRUD operations, photo uploads, access code generation, visitor gallery access, and responsive design across mobile/tablet/desktop.
  
  This skill delegates to copilot-dev-team plugin meta-skills: `playwright-bootstrap` (Playwright install/config), `playwright-test-recipe` (canonical e2e + page-object + auth-fixture pattern), `pr-review-checklist` (PR-review gate), `release-notes` (release format), and `project-board-sync` (GitHub Project v2 column sync). Auto-trigger these when their conditions match. Plugin meta-skills are canonical — prefer them on conflict.
  
  **Dispatch this agent for:**
  - Phase 9: Testing infrastructure setup
  - Feature validation after backend/frontend implementation
  - Pre-release quality assurance
  - CI/CD pipeline validation
  - Accessibility compliance testing
  
  **Related skills this uses:**
  - **playwright-testing-skill** - Executes E2E tests and test patterns
  - **clean-architecture-guide** - Understands service layer for test mocking
  - **backend-developer-skill** - Collaborates on backend validation
  - **frontend-developer-skill** - Collaborates on frontend validation

  This skill delegates to copilot-dev-team plugin meta-skills: `playwright-bootstrap` (Playwright install/config), `playwright-test-recipe` (canonical e2e + page-object + auth-fixture pattern), `pr-review-checklist` (PR-review gate), `release-notes` (release format), and `project-board-sync` (GitHub Project v2 column sync). Auto-trigger these when their conditions match. Plugin meta-skills are canonical — prefer them on conflict.
---

# QA Quality Control Skill: PhotoGallery E2E Testing

## Plugin Meta-Skills

QA work crosses several disciplines (e2e authoring, review hygiene, release management); each is owned by a focused `copilot-dev-team` plugin meta-skill. This skill stays PhotoGallery-specific (which user flows are critical, what counts as a release-blocker); it defers to the plugin meta-skills for the underlying procedures.

| Phase / situation | MUST consult | Consider |
| --- | --- | --- |
| Authoring an e2e test | `playwright-test-recipe` | — |
| First-time Playwright setup | — | `playwright-bootstrap` |
| Final PR-review / quality gate | `pr-review-checklist` | — |
| Drafting release notes for a sprint/version | — | `release-notes` |
| Moving issues across the GitHub Project board | — | `project-board-sync` |

**Workflow callouts:**

- *→ E2E test authoring — consult `playwright-test-recipe` (and `playwright-bootstrap` for first-time setup).*
- *→ PR / quality-gate sections — consult `pr-review-checklist`.*
- *→ Release-tagging / sprint-close sections — consult `release-notes`.*
- *→ Project-board management sections — consult `project-board-sync`.*

## Your Role

You are the quality control expert ensuring PhotoGallery works correctly end-to-end. Your responsibilities:

1. **Test Planning** - Identify all workflows and edge cases
2. **E2E Test Development** - Write comprehensive Playwright tests
3. **Accessibility Testing** - Verify WCAG 2.1 AA compliance
4. **Multi-Browser Testing** - Validate Chrome, Firefox, Safari, mobile
5. **Performance Testing** - Ensure acceptable load times
6. **Regression Testing** - Verify previous features still work
7. **CI/CD Integration** - Automate tests in GitHub Actions
8. **Compliance** - Reference playwright skill for test patterns

**Before writing tests**, read the related skills:
- **playwright-testing-skill** - Understand test structure, Page Objects, fixtures
- **clean-architecture-guide** - Understand auth flow and service layer
- **backend-developer-skill** - Know backend API contracts
- **frontend-developer-skill** - Know UI component selectors

## Phase 9: Testing Infrastructure

### Step 1: Project Setup

→ **Consult `playwright-bootstrap`** for first-time Playwright project setup, dependency installation, and config scaffolding.

```bash
npm install -D @playwright/test
npx playwright install

# Create test structure
mkdir -p tests/e2e/{pages,fixtures,helpers}
```

### Step 2: Playwright Configuration

```typescript
// playwright.config.ts
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
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
    {
      name: 'Mobile Chrome',
      use: { ...devices['Pixel 5'] },
    },
    {
      name: 'Mobile Safari',
      use: { ...devices['iPhone 12'] },
    },
  ],

  webServer: {
    command: 'ng serve',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env.CI,
  },
});
```

### Step 3: Login Fixture

→ **Consult `playwright-test-recipe`** for canonical auth fixture pattern, best practices for login state reuse, and test isolation.

```typescript
// tests/e2e/fixtures/auth.fixture.ts
import { test as base } from '@playwright/test';
import { LoginPage } from '../pages/login.page';

type AuthFixtures = {
  authenticatedPage: Page;
  adminToken: string;
};

export const test = base.extend<AuthFixtures>({
  authenticatedPage: async ({ page }, use) => {
    const loginPage = new LoginPage(page);
    
    // Bypass login if DISABLE_AUTH is set in backend
    if (process.env.DISABLE_AUTH === 'true') {
      // Set token in localStorage for test user
      await page.addInitScript(() => {
        localStorage.setItem('auth_token', 'test-token-for-admin');
      });
    } else {
      // Normal login flow
      await loginPage.goto();
      await loginPage.clickTestUserLogin();
      await page.waitForURL('/admin/dashboard');
    }
    
    await use(page);
  },
  
  adminToken: async ({ page }, use) => {
    let token = 'test-admin-token';
    
    if (process.env.DISABLE_AUTH !== 'true') {
      // Get token from localStorage after login
      token = await page.evaluate(() => localStorage.getItem('auth_token') || '');
    }
    
    await use(token);
  },
});

export { expect } from '@playwright/test';
```

### Step 4: Page Objects

```typescript
// tests/e2e/pages/login.page.ts
import { Page } from '@playwright/test';

export class LoginPage {
  constructor(private page: Page) {}
  
  async goto() {
    await this.page.goto('/login');
  }
  
  async clickTestUserLogin() {
    await this.page.click('button:has-text("Test Admin Login")');
  }
  
  async clickGoogleLogin() {
    await this.page.click('button:has-text("Sign in with Google")');
  }
  
  async isVisible() {
    return this.page.isVisible('h1:has-text("PhotoGallery")');
  }
}

// tests/e2e/pages/albums.page.ts
import { Page } from '@playwright/test';

export class AlbumsPage {
  constructor(private page: Page) {}
  
  async goto() {
    await this.page.goto('/admin/albums');
  }
  
  async clickCreateAlbum() {
    await this.page.click('button:has-text("Create Album")');
  }
  
  async fillAlbumForm(title: string, description: string) {
    await this.page.fill('input[formControlName="title"]', title);
    await this.page.fill('textarea[formControlName="description"]', description);
  }
  
  async clickCreate() {
    await this.page.click('button:has-text("Create")');
  }
  
  async getAlbumCount() {
    const rows = await this.page.locator('tbody tr').count();
    return rows;
  }
  
  async verifyAlbumExists(title: string) {
    return this.page.isVisible(`text=${title}`);
  }
  
  async deleteAlbum(title: string) {
    const row = this.page.locator(`tr:has(td:has-text("${title}"))`);
    await row.locator('button:has-text("Delete")').click();
    await this.page.click('button:has-text("OK")');
  }
}

// tests/e2e/pages/photo-upload.page.ts
import { Page } from '@playwright/test';
import * as path from 'path';

export class PhotoUploadPage {
  constructor(private page: Page) {}
  
  async goto(albumId: string) {
    await this.page.goto(`/admin/albums/${albumId}/upload`);
  }
  
  async uploadPhoto(filePath: string) {
    const inputFile = await this.page.locator('input[type="file"]');
    await inputFile.setInputFiles(filePath);
  }
  
  async getUploadedPhotoCount() {
    const items = await this.page.locator('.list-group-item').count();
    return items;
  }
  
  async waitForUploadComplete() {
    await this.page.waitForSelector('text=Uploaded Photos');
  }
}

// tests/e2e/pages/access-code.page.ts
import { Page } from '@playwright/test';

export class AccessCodePage {
  constructor(private page: Page) {}
  
  async goto(albumId: string) {
    await this.page.goto(`/admin/albums/${albumId}/access-code`);
  }
  
  async selectTemporary() {
    await this.page.selectOption('select[formControlName="expirationType"]', 'temporary');
  }
  
  async setExpirationDays(days: number) {
    await this.page.fill('input[formControlName="expirationDays"]', days.toString());
  }
  
  async clickCreate() {
    await this.page.click('button:has-text("Create Code")');
  }
  
  async getGeneratedCode() {
    const input = await this.page.locator('input[readonly]');
    return input.inputValue();
  }
  
  async copyCode() {
    await this.page.click('button:has-text("Copy Code")');
  }
}

// tests/e2e/pages/photo-gallery.page.ts
import { Page } from '@playwright/test';

export class PhotoGalleryPage {
  constructor(private page: Page) {}
  
  async gotoWithCode(code: string) {
    await this.page.goto(`/gallery/${code}`);
  }
  
  async getPhotoCount() {
    return this.page.locator('[class*="photo"]').count();
  }
  
  async openFirstPhoto() {
    await this.page.locator('[class*="photo"]').first().click();
  }
  
  async selectQuality(quality: 'high' | 'medium' | 'low') {
    await this.page.selectOption('select', quality);
  }
  
  async downloadPhoto() {
    const downloadPromise = this.page.waitForEvent('download');
    await this.page.click('button:has-text("Download")');
    return downloadPromise;
  }
  
  async verifyAlbumTitle(title: string) {
    return this.page.isVisible(`h1:has-text("${title}")`);
  }
}
```

## Test Cases

→ **Consult `playwright-test-recipe`** for e2e test patterns, page-object design, and fixture best practices. All spec files below follow this pattern.

### Authentication Tests

```typescript
// tests/e2e/auth.spec.ts
import { test, expect } from './fixtures/auth.fixture';
import { LoginPage } from './pages/login.page';

test.describe('Authentication', () => {
  test('should display login page', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    expect(await loginPage.isVisible()).toBeTruthy();
  });
  
  test('should login as test user', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.clickTestUserLogin();
    await page.waitForURL('/admin/dashboard');
    expect(page.url()).toContain('dashboard');
  });
  
  test('should redirect to login when not authenticated', async ({ page }) => {
    await page.goto('/admin/albums');
    await page.waitForURL('/login');
    expect(page.url()).toContain('login');
  });
  
  test('authenticated page should have auth token', async ({ authenticatedPage, adminToken }) => {
    const token = await authenticatedPage.evaluate(() => localStorage.getItem('auth_token'));
    expect(token).toBeTruthy();
  });
});
```

### Album Management Tests

```typescript
// tests/e2e/albums.spec.ts
import { test, expect } from './fixtures/auth.fixture';
import { AlbumsPage } from './pages/albums.page';

test.describe('Album Management', () => {
  test('should list albums', async ({ authenticatedPage }) => {
    const albumsPage = new AlbumsPage(authenticatedPage);
    await albumsPage.goto();
    expect(await albumsPage.getAlbumCount()).toBeGreaterThanOrEqual(0);
  });
  
  test('should create new album', async ({ authenticatedPage }) => {
    const albumsPage = new AlbumsPage(authenticatedPage);
    await albumsPage.goto();
    
    const initialCount = await albumsPage.getAlbumCount();
    
    await albumsPage.clickCreateAlbum();
    await authenticatedPage.waitForURL('**/albums/create');
    
    await albumsPage.fillAlbumForm('Test Album', 'This is a test album');
    await albumsPage.clickCreate();
    
    await authenticatedPage.waitForURL('**/albums');
    
    const newCount = await albumsPage.getAlbumCount();
    expect(newCount).toBe(initialCount + 1);
  });
  
  test('should verify album exists', async ({ authenticatedPage }) => {
    const albumsPage = new AlbumsPage(authenticatedPage);
    await albumsPage.goto();
    
    const exists = await albumsPage.verifyAlbumExists('Test Album');
    expect(exists).toBeTruthy();
  });
  
  test('should delete album', async ({ authenticatedPage }) => {
    const albumsPage = new AlbumsPage(authenticatedPage);
    await albumsPage.goto();
    
    const initialCount = await albumsPage.getAlbumCount();
    
    await albumsPage.deleteAlbum('Test Album');
    
    const newCount = await albumsPage.getAlbumCount();
    expect(newCount).toBe(initialCount - 1);
  });
});
```

### Photo Upload Tests

```typescript
// tests/e2e/photo-upload.spec.ts
import { test, expect } from './fixtures/auth.fixture';
import { AlbumsPage } from './pages/albums.page';
import { PhotoUploadPage } from './pages/photo-upload.page';
import * as path from 'path';

test.describe('Photo Upload', () => {
  let albumId: string;
  
  test.beforeEach(async ({ authenticatedPage }) => {
    // Create test album
    const albumsPage = new AlbumsPage(authenticatedPage);
    await albumsPage.goto();
    await albumsPage.clickCreateAlbum();
    await authenticatedPage.waitForURL('**/albums/create');
    
    await albumsPage.fillAlbumForm('Upload Test Album', 'For testing uploads');
    await albumsPage.clickCreate();
    
    // Get album ID from URL
    const url = authenticatedPage.url();
    albumId = url.split('/').pop() || '';
  });
  
  test('should upload single photo', async ({ authenticatedPage }) => {
    const uploadPage = new PhotoUploadPage(authenticatedPage);
    await uploadPage.goto(albumId);
    
    const testImagePath = path.join(__dirname, '../fixtures/test-photo.jpg');
    await uploadPage.uploadPhoto(testImagePath);
    
    await uploadPage.waitForUploadComplete();
    
    const count = await uploadPage.getUploadedPhotoCount();
    expect(count).toBeGreaterThan(0);
  });
});
```

### Visitor Access Tests

```typescript
// tests/e2e/visitor-access.spec.ts
import { test, expect } from '@playwright/test';
import { AccessCodePage } from './pages/access-code.page';
import { PhotoGalleryPage } from './pages/photo-gallery.page';

test.describe('Visitor Access via Code', () => {
  let accessCode: string;
  
  test.beforeEach(async ({ page }) => {
    // Login, create album, get access code
    // ... (setup logic)
  });
  
  test('should access album with valid code', async ({ page }) => {
    const galleryPage = new PhotoGalleryPage(page);
    await galleryPage.gotoWithCode(accessCode);
    
    expect(await galleryPage.verifyAlbumTitle('Test Album')).toBeTruthy();
  });
  
  test('should list photos in album', async ({ page }) => {
    const galleryPage = new PhotoGalleryPage(page);
    await galleryPage.gotoWithCode(accessCode);
    
    const count = await galleryPage.getPhotoCount();
    expect(count).toBeGreaterThan(0);
  });
  
  test('should download photo with quality selection', async ({ page }) => {
    const galleryPage = new PhotoGalleryPage(page);
    await galleryPage.gotoWithCode(accessCode);
    
    await galleryPage.openFirstPhoto();
    await galleryPage.selectQuality('high');
    
    const download = await galleryPage.downloadPhoto();
    expect(download.suggestedFilename()).toMatch(/\.jpg$/);
  });
});
```

### Accessibility Tests

```typescript
// tests/e2e/accessibility.spec.ts
import { test, expect } from './fixtures/auth.fixture';
import { injectAxe, checkA11y } from 'axe-playwright';

test.describe('Accessibility (WCAG 2.1 AA)', () => {
  test('login page should have no accessibility issues', async ({ page }) => {
    await page.goto('/login');
    await injectAxe(page);
    await checkA11y(page, null, {
      detailedReport: true,
      detailedReportOptions: {
        html: true
      }
    });
  });
  
  test('dashboard should have no accessibility issues', async ({ authenticatedPage }) => {
    await authenticatedPage.goto('/admin/dashboard');
    await injectAxe(authenticatedPage);
    await checkA11y(authenticatedPage);
  });
  
  test('album page should have proper labels', async ({ authenticatedPage }) => {
    await authenticatedPage.goto('/admin/albums');
    
    const labels = await authenticatedPage.locator('label').count();
    expect(labels).toBeGreaterThan(0);
  });
  
  test('forms should have ARIA descriptions', async ({ authenticatedPage }) => {
    await authenticatedPage.goto('/admin/albums/create');
    
    const titleInput = await authenticatedPage.locator('input[formControlName="title"]');
    const ariaLabel = await titleInput.getAttribute('aria-label') || 
                       await titleInput.getAttribute('aria-labelledby');
    expect(ariaLabel).toBeTruthy();
  });
});
```

### Responsive Design Tests

```typescript
// tests/e2e/responsive.spec.ts
import { test, expect, devices } from '@playwright/test';

test.describe('Responsive Design', () => {
  test('should be responsive on mobile (Pixel 5)', async ({ browser }) => {
    const context = await browser.newContext({
      ...devices['Pixel 5']
    });
    const page = await context.newPage();
    
    await page.goto('/admin/dashboard');
    
    // Verify no horizontal scrollbar
    const viewportSize = page.viewportSize();
    const bodyWidth = await page.evaluate(() => document.body.scrollWidth);
    expect(bodyWidth).toBeLessThanOrEqual(viewportSize!.width + 1);
    
    await context.close();
  });
  
  test('should be responsive on tablet (iPad)', async ({ browser }) => {
    const context = await browser.newContext({
      ...devices['iPad']
    });
    const page = await context.newPage();
    
    await page.goto('/admin/albums');
    
    // Verify table is readable
    const table = await page.isVisible('table');
    expect(table).toBeTruthy();
    
    await context.close();
  });
});
```

## CI/CD Integration

```yaml
# .github/workflows/e2e-tests.yml
name: E2E Tests

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest
    
    services:
      backend:
        image: photogallery-backend:latest
        ports:
          - 8443:8443
        env:
          DISABLE_AUTH: true
          
      minio:
        image: minio/minio
        ports:
          - 9000:9000
        env:
          MINIO_ROOT_USER: minioadmin
          MINIO_ROOT_PASSWORD: minioadmin
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup Node
        uses: actions/setup-node@v3
        with:
          node-version: '20'
      
      - name: Install dependencies
        run: npm ci
      
      - name: Install Playwright
        run: npx playwright install --with-deps
      
      - name: Build frontend
        run: npm run build
      
      - name: Run E2E tests
        run: npm run test:e2e
      
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: playwright-report
          path: playwright-report/
```

## Quality Checklist

→ **Consult `pr-review-checklist`** before merging to ensure comprehensive PR-review gate compliance and QA sign-off.

Before release, verify:

- [ ] All E2E tests pass (Chrome, Firefox, Safari, mobile)
- [ ] Accessibility tests pass (WCAG 2.1 AA)
- [ ] Responsive design tests pass (mobile/tablet/desktop)
- [ ] Performance acceptable (< 3 second page load)
- [ ] Error handling works (shows user-friendly messages)
- [ ] Authentication flow complete (Google + test user)
- [ ] Album CRUD works (create/read/update/delete)
- [ ] Photo upload works
- [ ] Access codes work (generation, validation, expiration)
- [ ] Visitor gallery accessible via code
- [ ] Photo download with quality selection works
- [ ] No console errors or warnings
- [ ] No security issues (no sensitive data in localStorage)

## Test Coverage Goals

- **Authentication:** 100% (critical path)
- **Album Management:** 100% (core feature)
- **Photo Upload:** 95% (handle edge cases)
- **Access Codes:** 100% (security feature)
- **Visitor Access:** 100% (key workflow)
- **Responsive Design:** 95% (cross-browser)
- **Accessibility:** 100% (compliance)

## Support

For questions about:
- **Test patterns** → Consult `playwright-testing-skill`
- **Test authoring & e2e recipes** → Consult `playwright-test-recipe`
- **First-time Playwright setup** → Consult `playwright-bootstrap`
- **PR review gate** → Consult `pr-review-checklist`
- **Release notes & sprint closure** → Consult `release-notes`
- **GitHub Project board sync** → Consult `project-board-sync`
- **Backend integration** → Consult `backend-developer-skill`
- **Frontend integration** → Consult `frontend-developer-skill`
- **Architecture** → Consult `photogallery-architect-skill`

## References

- **Playwright:** https://playwright.dev/docs/intro
- **Accessibility Testing:** https://www.w3.org/WAI/test-evaluate/
- **axe DevTools:** https://www.deque.com/axe/devtools/
- **Related Skills:** playwright-testing-skill, backend-developer-skill, frontend-developer-skill


## Cross-cutting plugin skills (always-on)

These copilot-dev-team meta-skills apply regardless of phase:

- `scratch-discipline` — QA probes / repro scripts in .copilot/scratch/<task-id>/.
- `secret-hygiene` — never hardcode test passwords / tokens in committed e2e specs.
- `commit-conventions` — canonical commit-message format.
- `branch-strategy-u-prefix` — `u/<actor>/<type>/<scope>` branches only.
- `copilot-memory-update` — record durable QA-policy decisions (browser matrix, release criteria).
