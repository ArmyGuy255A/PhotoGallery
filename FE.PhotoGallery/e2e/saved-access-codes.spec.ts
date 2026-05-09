import { test, expect, Page } from '@playwright/test';
import { adminAuthHeaders, createAlbumViaApi } from './fixtures/auth.fixture';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';
const FRONTEND_BASE_URL = process.env['FRONTEND_BASE_URL'] ?? 'http://localhost:4300';

/**
 * The Angular AppComponent's ngOnInit unconditionally calls
 * `router.navigate(['/dashboard'])` whenever it succeeds at loading the
 * current user from the backend. That fires on every full page load,
 * which means `page.goto('/code/{code}')` would race the auto-redirect
 * and almost always lose. Instead, we drive routing via History API +
 * popstate after a single bootstrap goto to `/`. Angular's router
 * subscribes to popstate via PathLocationStrategy and re-resolves the
 * route, but AppComponent.ngOnInit doesn't run again — so we land where
 * we asked.
 */
async function appNavigate(page: Page, path: string): Promise<void> {
  await page.evaluate((p) => {
    window.history.pushState({}, '', p);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

/**
 * EPIC-02 Slice B — Saved Access Codes flow.
 *
 * Covers the round trip: visit /code/:code as an authenticated user → save
 * the code via the "Save to my account" button → see it appear on
 * /shared-albums → re-open it via "View Album" → remove it (which only
 * unlinks the user's saved-link, the underlying AccessCode keeps working).
 *
 * Tests share state and must run serially.
 */
test.describe.configure({ mode: 'serial' });

test.describe('B — Saved Access Codes', () => {
  let page: Page;
  let albumTitle: string;
  let accessCode: string;

  test.beforeAll(async ({ browser, request }) => {
    page = await browser.newPage();

    // Bootstrap the SPA once — this triggers AppComponent.ngOnInit, which
    // auto-redirects to /dashboard. After this, in-app navigation via
    // appNavigate() bypasses the auto-redirect.
    await page.goto(`${FRONTEND_BASE_URL}/`);
    await page.waitForURL(/\/dashboard/, { timeout: 15_000 });

    // Setup: create album + access code via API.
    albumTitle = `Saved Codes E2E ${Date.now()}`;
    const album = await createAlbumViaApi(request, albumTitle);

    const codeResp = await request.post(
      `${BACKEND_BASE_URL}/api/albums/${album.id}/access-codes`,
      {
        headers: { 'Content-Type': 'application/json', ...adminAuthHeaders() },
        data: { expiresForever: true }
      }
    );
    if (!codeResp.ok()) {
      throw new Error(`Failed to create access code: ${codeResp.status()} ${await codeResp.text()}`);
    }
    const codeBody = await codeResp.json();
    accessCode = codeBody.code;
    expect(accessCode, 'access code should be returned by API').toBeTruthy();
  });

  test.beforeEach(() => {
    test.setTimeout(60_000);
    // Auto-accept any confirm dialogs that may appear on Remove.
    page.removeAllListeners('dialog');
    page.on('dialog', (d) => d.accept().catch(() => {}));
  });

  test.afterAll(async () => {
    await page.close();
  });

  test('1. Save button visible on /code/{code} when authenticated', async () => {
    await appNavigate(page, `/code/${accessCode}`);

    // Wait for the gallery to render (album title is the h1).
    await expect(page.locator('.code-gallery h1')).toHaveText(albumTitle, { timeout: 15_000 });

    const saveBtn = page.getByRole('button', { name: /save to my account/i });
    await expect(saveBtn).toBeVisible({ timeout: 5_000 });
  });

  test('2. Click Save → ✓ Saved feedback shown', async () => {
    const saveBtn = page.getByRole('button', { name: /save to my account/i });
    await saveBtn.click();

    // The button text becomes "✓ Saved" briefly (~2 seconds).
    await expect(page.locator('.save-button')).toContainText(/saved/i, { timeout: 3_000 });
  });

  test('3. Idempotent save (clicking again is OK, no error)', async () => {
    const consoleErrors: string[] = [];
    const onErr = (msg: any) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text());
    };
    page.on('console', onErr);

    let dialogShown = false;
    const onDialog = (d: any) => { dialogShown = true; d.accept().catch(() => {}); };
    page.on('dialog', onDialog);

    // Re-route to a different page and back to reset the per-component
    // `saved` flag so the button is enabled again. Avoid a full reload
    // (which would trigger the AppComponent auto-redirect to /dashboard).
    await appNavigate(page, '/dashboard');
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 5_000 });
    await appNavigate(page, `/code/${accessCode}`);
    await expect(page.locator('.code-gallery h1')).toHaveText(albumTitle, { timeout: 15_000 });

    const saveBtn = page.getByRole('button', { name: /save to my account/i });
    await expect(saveBtn).toBeVisible();
    await saveBtn.click();

    // Should still report saved feedback (server is idempotent).
    await expect(page.locator('.save-button')).toContainText(/saved/i, { timeout: 3_000 });

    // No alert/dialog and no save-related console errors.
    expect(dialogShown, 'no error dialog should appear on duplicate save').toBe(false);
    expect(
      consoleErrors.filter((e) => /access code|save/i.test(e)),
      'no save-related console errors'
    ).toEqual([]);

    page.off('console', onErr);
    page.off('dialog', onDialog);
  });

  test('4. /shared-albums lists the saved code', async () => {
    await appNavigate(page, '/shared-albums');
    await expect(page.locator('h1', { hasText: 'Shared Albums' })).toBeVisible({ timeout: 10_000 });

    const card = page.locator('.card', { hasText: albumTitle });
    await expect(card).toBeVisible({ timeout: 10_000 });
    // Card shows saved date (the "Saved <date>" meta line).
    await expect(card.locator('.meta', { hasText: /saved/i })).toBeVisible();
  });

  test('5. "View Album" button on a card returns to /code/{code}', async () => {
    const card = page.locator('.card', { hasText: albumTitle });
    await card.getByRole('button', { name: /view album/i }).click();

    await page.waitForURL(`**/code/${accessCode}`, { timeout: 10_000 });
    await expect(page.locator('.code-gallery h1')).toHaveText(albumTitle, { timeout: 10_000 });
  });

  test('6. Remove unlinks the saved code', async () => {
    await appNavigate(page, '/shared-albums');

    const card = page.locator('.card', { hasText: albumTitle });
    await expect(card).toBeVisible({ timeout: 10_000 });

    await card.getByRole('button', { name: /^remove$/i }).click();

    // Card should disappear after removal.
    await expect(card).toHaveCount(0, { timeout: 10_000 });

    // Empty state shown (assuming no other saved codes for this user).
    await expect(page.locator('.empty', { hasText: /haven't saved any albums/i })).toBeVisible({
      timeout: 5_000
    });
  });

  test('7. Original access code still works after Remove', async () => {
    await appNavigate(page, `/code/${accessCode}`);

    // The AccessCode itself was not deleted — the gallery should still render.
    await expect(page.locator('.code-gallery h1')).toHaveText(albumTitle, { timeout: 15_000 });
    // No "Unable to access this album" error block.
    await expect(page.locator('.error h2', { hasText: /unable to access/i })).toHaveCount(0);
  });
});
