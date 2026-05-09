/**
 * COOP / GIS bootstrap regression probe — drives /login, clicks the
 * GIS-rendered Sign-in button, and verifies:
 *
 *   - Cross-Origin-Opener-Policy is "same-origin-allow-popups" everywhere
 *     (otherwise GIS popup → opener postMessage is blocked)
 *   - The GIS SDK script (loaded async/defer from index.html) has actually
 *     evaluated by the time renderButton runs (no `google is not defined`)
 *   - The Google account-chooser popup opens with origin=http://localhost:4300
 *     and the configured clientId
 *
 * Skipped by default because:
 *   - it hits accounts.google.com (live network)
 *   - it requires a running backend with a real Google:ClientId
 *
 * To run locally for COOP/GIS regression testing:
 *   $env:RUN_GIS_PROBE = "1"; npx playwright test e2e/coop-gis-probe.spec.ts --project=chromium
 */
import { test, expect } from '@playwright/test';

const FE = 'http://localhost:4300';
const RUN = process.env['RUN_GIS_PROBE'] === '1';

test.describe('GIS bootstrap probe', () => {
  test.skip(!RUN, 'opt-in: set RUN_GIS_PROBE=1 to enable (hits accounts.google.com)');

  test('COOP set, SDK loaded, popup opens with correct origin+clientId', async ({ page }) => {
    const consoleLogs: { type: string; text: string }[] = [];
    page.on('console', m => consoleLogs.push({ type: m.type(), text: m.text() }));

    await page.goto(`${FE}/login`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000); // Let GIS render the button

    // Document COOP must be relaxed so the popup can postMessage back
    const documentCoop = await page.evaluate(async () => {
      const r = await fetch('/', { method: 'HEAD' });
      return r.headers.get('cross-origin-opener-policy');
    });
    expect(documentCoop).toBe('same-origin-allow-popups');

    // GIS button iframe must have rendered (its mere presence proves the
    // SDK loaded before our setTimeout-deferred renderButton call ran)
    const iframeSrc = await page.evaluate(() => {
      const iframe = document.querySelector('iframe[src*="accounts.google.com"]') as HTMLIFrameElement | null;
      return iframe?.src ?? null;
    });
    expect(iframeSrc).toBeTruthy();
    expect(iframeSrc).toContain('client_id=');

    // No "google is not defined" stack trace
    const gisRefError = consoleLogs.find(l => l.text.includes('google is not defined'));
    expect(gisRefError, 'GIS SDK script-load race must not fire').toBeUndefined();

    // Click the button → popup must open to accounts.google.com with our origin
    const popupPromise = page.waitForEvent('popup', { timeout: 10000 }).catch(() => null);
    await page.locator('#google-signin-container').click({ force: true });
    const popup = await popupPromise;

    expect(popup, 'GIS popup must open').not.toBeNull();
    expect(popup!.url()).toContain('accounts.google.com');
    expect(decodeURIComponent(popup!.url())).toContain('origin=http://localhost:4300');
  });
});
