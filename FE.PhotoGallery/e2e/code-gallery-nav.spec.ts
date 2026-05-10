import { test, expect } from '@playwright/test';
import { LoginPage } from './pages/login.page';

/**
 * E2E probe for PR #45 (PR-D fleet plan):
 *   When an authenticated viewer visits /code/{ACCESS_CODE},
 *   the <app-user-dropdown> avatar is mounted in the gallery
 *   header so the user can navigate back to the dashboard.
 *
 * Gated like coop-gis-probe.spec.ts because this requires a
 * valid live access code, which depends on backend seeding.
 *
 *   $env:RUN_CODE_GALLERY_NAV_PROBE = "1"
 *   $env:CODE_GALLERY_NAV_ACCESS_CODE = "ABCD1234"
 *   npx playwright test e2e/code-gallery-nav.spec.ts
 *
 * Default behavior: the test is skipped, keeping CI green
 * while the conditional-render contract is enforced by the
 * Karma spec (code-gallery.component.spec.ts).
 */
const probeEnabled = process.env['RUN_CODE_GALLERY_NAV_PROBE'] === '1';
const accessCode = process.env['CODE_GALLERY_NAV_ACCESS_CODE'] ?? '';

test.describe('PR-D — code-gallery header nav (probe)', () => {
  test.skip(!probeEnabled, 'Set RUN_CODE_GALLERY_NAV_PROBE=1 + CODE_GALLERY_NAV_ACCESS_CODE to run.');

  test('authenticated viewer sees user-dropdown on /code/{CODE}', async ({ page }) => {
    test.skip(!accessCode, 'CODE_GALLERY_NAV_ACCESS_CODE not provided.');
    test.setTimeout(60_000);

    await new LoginPage(page).loginAsDevAdmin();
    await page.goto(`/code/${accessCode}`);

    await expect(page.locator('app-code-gallery app-user-dropdown')).toBeVisible();
    await expect(page.getByTestId('user-avatar-button')).toBeVisible();
  });

  test('unauthenticated guest sees no user-dropdown on /code/{CODE}', async ({ browser }) => {
    test.skip(!accessCode, 'CODE_GALLERY_NAV_ACCESS_CODE not provided.');
    test.setTimeout(60_000);

    // Fresh context: no auth state.
    const ctx = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    const page = await ctx.newPage();
    await page.goto(`/code/${accessCode}`);

    await expect(page.locator('app-code-gallery')).toBeVisible();
    await expect(page.locator('app-code-gallery app-user-dropdown')).toHaveCount(0);

    await ctx.close();
  });
});
