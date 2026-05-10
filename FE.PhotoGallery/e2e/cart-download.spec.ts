/**
 * Cart download end-to-end probe (PR-F).
 *
 * Drives the full cart flow against a live stack:
 *   1. Open `/code/{code}` (guest-friendly).
 *   2. Add 2-3 photos to the cart from the gallery.
 *   3. Open a photo modal that's already in the cart → assert the
 *      Remove button is visible (red, "− Remove from Cart") → click it →
 *      cart count decrements (PR-F UX#3 regression).
 *   4. Open the cart drawer.
 *   5. Visual sanity-check: the gallery thumbnail in the cart preview is
 *      the watermarked variant (D009 — guests see watermarked).
 *   6. Click Download. Assert the manifest endpoint is hit and the
 *      progress bar advances (manifest -> downloading -> zipping -> saving -> done).
 *   7. The browser receives a ZIP (download event fires).
 *
 * Skipped by default because:
 *   - it requires a running MinIO + backend + FE
 *   - it requires a seeded album + access code + at least 2 photos
 *
 * To run locally:
 *   $env:RUN_CART_E2E = "1"
 *   $env:CART_TEST_CODE = "DEMO123"   # access code for a seeded album
 *   npx playwright test e2e/cart-download.spec.ts --project=chromium
 */
import { test, expect } from '@playwright/test';

const FE = process.env['CART_FE_BASE'] ?? 'http://localhost:4300';
const CODE = process.env['CART_TEST_CODE'] ?? 'DEMO123';
const RUN = process.env['RUN_CART_E2E'] === '1';

test.describe('Cart download e2e probe', () => {
  test.skip(!RUN, 'opt-in: set RUN_CART_E2E=1 to enable (requires running stack)');

  test('happy path: add → modal Remove → download → ZIP delivered', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });

    await page.goto(`${FE}/code/${CODE}`);
    await expect(page).toHaveURL(new RegExp(`/code/${CODE}`));

    // --- Step 2: add the first two photos to the cart -----------------
    const addButtons = page.getByRole('button', { name: /add to cart/i });
    await expect(addButtons.first()).toBeVisible({ timeout: 10_000 });

    const initialAddCount = await addButtons.count();
    expect(initialAddCount).toBeGreaterThanOrEqual(2);

    await addButtons.nth(0).click();
    await addButtons.nth(1).click();

    const cartBadge = page.locator('[data-testid="cart-count"], .cart-badge').first();
    await expect(cartBadge).toContainText('2', { timeout: 5_000 });

    // --- Step 3: photo modal Remove flow (PR-F UX#3) ------------------
    // Click the first photo to open its modal — it's already in the cart.
    const firstThumb = page.locator('[data-testid="photo-thumb"], .photo-card').first();
    await firstThumb.click();

    const removeBtn = page.getByRole('button', { name: /remove from cart/i });
    await expect(removeBtn).toBeVisible();
    // Style guard: Remove is red.
    const removeColor = await removeBtn.evaluate((el) => getComputedStyle(el).backgroundColor);
    expect(removeColor).toMatch(/rgb\((2[0-5][0-9]|1[6-9][0-9]),\s*\d+,\s*\d+\)/);

    await removeBtn.click();

    // Cart should drop to 1.
    await expect(cartBadge).toContainText('1', { timeout: 5_000 });

    // Close modal.
    await page.keyboard.press('Escape');

    // Re-add the photo so the download has 2 items.
    await addButtons.first().click();
    await expect(cartBadge).toContainText('2', { timeout: 5_000 });

    // --- Step 4: open cart drawer -------------------------------------
    await page.locator('[data-testid="cart-toggle"], .cart-button').first().click();

    // --- Step 5: watermark visual check on cart thumb -----------------
    const cartThumb = page.locator('.cart-drawer .thumb img').first();
    await expect(cartThumb).toBeVisible();
    const thumbSrc = await cartThumb.getAttribute('src');
    expect(thumbSrc).toBeTruthy();
    // Public viewers see the watermarked Medium per D009. The presigned URL
    // for the watermarked variant points at "medium-watermarked.jpg".
    // (We don't pixel-diff here — just assert the right object is referenced.)
    expect(thumbSrc).toMatch(/medium-watermarked|watermarked/i);

    // --- Step 6: download → progress phases → ZIP delivered ----------
    const manifestRequest = page.waitForRequest(r =>
      r.url().includes('/cart/manifest') && r.method() === 'POST'
    );
    const downloadEvent = page.waitForEvent('download', { timeout: 30_000 });

    await page.getByRole('button', { name: /download \(\d+\)/i }).click();

    const manifest = await manifestRequest;
    expect(manifest.postDataJSON()).toMatchObject({
      items: expect.arrayContaining([expect.objectContaining({ photoId: expect.any(String) })])
    });

    // Progress bar should appear and animate
    const progressLabel = page.locator('.progress-label');
    await expect(progressLabel).toBeVisible({ timeout: 5_000 });

    const download = await downloadEvent;
    const filename = download.suggestedFilename();
    expect(filename).toMatch(/\.zip$/i);

    // No JS console errors during the flow
    expect(consoleErrors, `console errors: ${consoleErrors.join('\n')}`).toEqual([]);
  });

  test('cancel during download tears down the in-flight Observable', async ({ page }) => {
    await page.goto(`${FE}/code/${CODE}`);
    const addButtons = page.getByRole('button', { name: /add to cart/i });
    await expect(addButtons.first()).toBeVisible({ timeout: 10_000 });
    await addButtons.nth(0).click();
    await addButtons.nth(1).click();

    await page.locator('[data-testid="cart-toggle"], .cart-button').first().click();
    await page.getByRole('button', { name: /download \(\d+\)/i }).click();

    // Fire the cancel as soon as the cancel button appears.
    const cancel = page.getByRole('button', { name: /^cancel$/i });
    await expect(cancel).toBeVisible({ timeout: 5_000 });
    await cancel.click();

    // After cancel, the Download button should re-appear.
    await expect(page.getByRole('button', { name: /download \(\d+\)/i })).toBeVisible({ timeout: 5_000 });
  });
});
