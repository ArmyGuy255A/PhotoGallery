# Playwright E2E Testing - Quick Reference

One-page cheat sheet for PhotoGallery E2E testing.

## Installation & Setup

```bash
npm install --save-dev @playwright/test
npx playwright install              # Install browser binaries
npx playwright codegen http://localhost:4200  # Record tests
```

## Test Structure

```typescript
import { test, expect } from '@playwright/test';

test('user can create album', async ({ page }) => {
  // 1. Setup/Arrange
  await page.goto('/albums');

  // 2. Act/Action
  await page.click('[data-testid="create-button"]');
  await page.fill('[data-testid="title"]', 'My Album');
  await page.click('[data-testid="save"]');

  // 3. Assert/Verify
  await expect(page.locator('text=Album created')).toBeVisible();
});
```

## Locators (Finding Elements)

| Method | Example | Use Case |
|--------|---------|----------|
| data-testid | `[data-testid="create"]` | Best - explicit and stable |
| Text | `text=Create Album` | Simple text elements |
| Role | `button:has-text("Save")` | Accessible selectors |
| CSS | `.btn-primary` | CSS classes |
| XPath | `//button[@type="submit"]` | Complex selectors |

**Add to HTML:**
```html
<button data-testid="create-button">Create</button>
<input data-testid="album-title" type="text">
```

## Assertions (Expectations)

```typescript
// Visibility
await expect(page.locator('.message')).toBeVisible();
await expect(page.locator('.error')).toBeHidden();

// Text content
await expect(page.locator('h1')).toHaveText('Albums');
await expect(page.locator('p')).toContainText('success');

// Input values
await expect(page.locator('input')).toHaveValue('user@gmail.com');
await expect(page.locator('select')).toHaveValue('admin');

// Element state
await expect(page.locator('button')).toBeEnabled();
await expect(page.locator('button')).toBeDisabled();
await expect(page.locator('checkbox')).toBeChecked();

// Count
await expect(page.locator('.album')).toHaveCount(3);
await expect(page.locator('.album')).toHaveCount(0);

// URL
await expect(page).toHaveURL('/albums');
await expect(page).toHaveURL(/albums\/\d+/);

// Attribute
await expect(page.locator('img')).toHaveAttribute('alt', 'Album');

// Class
await expect(page.locator('button')).toHaveClass(/active/);
```

## Common Actions

```typescript
// Navigation
await page.goto('/albums');
await page.goto('/', { waitUntil: 'networkidle' });

// Clicking
await page.click('[data-testid="button"]');
await page.locator('[data-testid="button"]').click();
await page.click('text=Click me');

// Typing
await page.fill('[data-testid="input"]', 'text');
await page.type('[data-testid="input"]', 'text', { delay: 100 });

// Selecting
await page.selectOption('[data-testid="select"]', 'option-value');

// File upload
await page.setInputFiles('[data-testid="file-input"]', 'photo.jpg');
await page.setInputFiles('[data-testid="files"]', ['photo1.jpg', 'photo2.jpg']);

// Checking/unchecking
await page.check('[data-testid="checkbox"]');
await page.uncheck('[data-testid="checkbox"]');

// Hover
await page.hover('[data-testid="element"]');

// Scroll
await page.locator('[data-testid="element"]').scrollIntoViewIfNeeded();

// Press keys
await page.press('[data-testid="input"]', 'Enter');
await page.press('[data-testid="input"]', 'Control+A');
```

## Waiting & Async

```typescript
// Wait for element
await page.locator('[data-testid="modal"]').waitFor({ state: 'visible' });

// Wait for navigation
await page.waitForNavigation();
await page.waitForURL('/albums/123');

// Wait for network
await page.waitForLoadState('networkidle');

// Wait with timeout
await page.locator('[data-testid="msg"]').waitFor({ timeout: 5000 });

// All animations done
await page.waitForLoadState('load');

// Combined wait and action
const [response] = await Promise.all([
  page.waitForNavigation(),
  page.click('[data-testid="save"]')
]);
```

## Page Objects (Best Practice)

```typescript
// pages/albums.page.ts
import { Page } from '@playwright/test';

export class AlbumsPage {
  constructor(readonly page: Page) {}

  async goto() {
    await this.page.goto('/albums');
  }

  async clickCreate() {
    await this.page.click('[data-testid="create-button"]');
  }

  async fillTitle(text: string) {
    await this.page.fill('[data-testid="title"]', text);
  }

  async clickSave() {
    await this.page.click('[data-testid="save-button"]');
  }

  async getAlbumCount() {
    return this.page.locator('[data-testid="album-card"]').count();
  }

  async verifyAlbumExists(title: string) {
    await expect(this.page.locator(`text=${title}`)).toBeVisible();
  }
}

// Use in test:
import { AlbumsPage } from './pages/albums.page';

test('create album', async ({ page }) => {
  const albumsPage = new AlbumsPage(page);
  
  await albumsPage.goto();
  await albumsPage.clickCreate();
  await albumsPage.fillTitle('My Album');
  await albumsPage.clickSave();
  await albumsPage.verifyAlbumExists('My Album');
});
```

## CLI Commands

```bash
npx playwright test                      # Run all tests
npx playwright test --ui                 # Interactive mode
npx playwright test --debug              # Debugger
npx playwright test e2e/auth.spec.ts    # Single file
npx playwright test --grep "create"      # Match pattern
npx playwright test --project=chromium   # Single browser
npx playwright test --project=firefox
npx playwright test --project=webkit
npx playwright test --project="Mobile Chrome"

npx playwright show-report               # View HTML report
npx playwright test --update-snapshots   # Update screenshots
```

## package.json Scripts

```json
{
  "scripts": {
    "test:e2e": "playwright test",
    "test:e2e:ui": "playwright test --ui",
    "test:e2e:debug": "playwright test --debug",
    "test:e2e:report": "playwright show-report"
  }
}
```

## PhotoGallery Test Examples

### Authentication

```typescript
test('admin auto-login with DISABLE_AUTH', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('[data-testid="user-menu"]')).toBeVisible();
});
```

### Album Management

```typescript
test('create album', async ({ page }) => {
  await page.goto('/albums');
  await page.click('[data-testid="create-button"]');
  await page.fill('[data-testid="title"]', 'Beach');
  await page.click('[data-testid="save"]');
  await expect(page.locator('text=Album created')).toBeVisible();
});

test('delete album', async ({ page }) => {
  await page.goto('/albums');
  await page.click('[data-testid="delete-button"]');
  await page.click('[data-testid="confirm"]');
  await expect(page.locator('text=Deleted')).toBeVisible();
});
```

### Photo Upload

```typescript
test('upload photo', async ({ page }) => {
  await page.goto('/albums/1/upload');
  await page.setInputFiles('[data-testid="file-input"]', 'photo.jpg');
  await expect(page.locator('text=Upload complete')).toBeVisible();
});
```

### Visitor Access

```typescript
test('visitor can access album with code', async ({ browser }) => {
  const visitorPage = await browser.newPage();
  await visitorPage.goto('/code/SAMPLE-123/photos');
  await expect(visitorPage.locator('[data-testid="photo"]')).toHaveCount(5);
  await visitorPage.close();
});

test('visitor cannot delete photos', async ({ browser }) => {
  const visitorPage = await browser.newPage();
  await visitorPage.goto('/code/SAMPLE-123/photos');
  await expect(visitorPage.locator('[data-testid="delete-button"]')).toBeHidden();
  await visitorPage.close();
});
```

## Fixtures (Setup/Teardown)

```typescript
// fixtures/auth.fixture.ts
import { test as base } from '@playwright/test';

export const test = base.extend({
  authenticatedPage: async ({ page }, use) => {
    // Setup
    await page.goto('/albums');
    
    // Use in test
    await use(page);
    
    // Teardown (optional)
  },
});

export { expect } from '@playwright/test';

// Use:
import { test } from './fixtures/auth.fixture';

test('admin can create album', async ({ authenticatedPage }) => {
  // authenticatedPage is already logged in
});
```

## Configuration (playwright.config.ts)

```typescript
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: true,
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: devices['Desktop Chrome'] },
    { name: 'firefox', use: devices['Desktop Firefox'] },
    { name: 'webkit', use: devices['Desktop Safari'] },
    { name: 'Mobile Chrome', use: devices['Pixel 5'] },
  ],
});
```

## CI/CD (GitHub Actions)

```yaml
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

## Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| Test fails on CI but passes locally | Check DISABLE_AUTH, verify baseURL, environment differences |
| Element not found | Check data-testid exists, wait for element, scroll into view |
| Flaky tests | Use proper waits instead of sleep, avoid race conditions |
| Timeout errors | Increase timeout, check element exists, verify selector correct |
| File upload fails | Verify file path, check input type="file", use absolute path |
| Screenshot differences | Update snapshots with --update-snapshots |

## Debugging

```bash
# Debug mode (stops at each step)
npx playwright test --debug

# Interactive UI mode (best for debugging)
npx playwright test --ui

# Verbose logging
PWDEBUG=1 npx playwright test

# Show traces (full execution recording)
npx playwright test --trace on
npx playwright show-trace trace.zip
```

---

For complete patterns and advanced features, see **SKILL.md**. Official docs: https://playwright.dev/docs/intro
