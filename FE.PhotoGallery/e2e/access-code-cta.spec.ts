import { test, expect } from '@playwright/test';

/**
 * B1 — Access Code CTA on the login page (PR #20).
 *
 * The /login page exposes an "Enter Access Code" CTA for unauthenticated
 * guests so they can reach a shared album via /code/{CODE} without needing
 * to sign in with Google. These tests exercise the CTA's expand/collapse
 * behaviour, client-side validation, and uppercase-normalised redirect.
 *
 * All tests run with a fresh, unauthenticated browser context — the CTA
 * is a guest-only flow.
 */
test.use({ storageState: { cookies: [], origins: [] } });

/**
 * In DISABLE_AUTH=true mode the backend's /api/auth/me endpoint returns a
 * test-admin user + token, which the AuthService stores and then route guards
 * use to bounce /login → /dashboard. For these guest-only specs we abort that
 * bootstrap call so the LoginComponent stays put.
 */
async function blockAutoAuth(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/auth/me', route => route.abort());
}

test.describe('B1 — Access Code CTA', () => {
  test.beforeEach(async ({ page }) => {
    await blockAutoAuth(page);
  });

  test('CTA is visible on /login when unauthenticated', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByTestId('enter-access-code-btn')).toBeVisible();
  });

  test('clicking the CTA reveals the access-code form', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('enter-access-code-btn').click();
    await expect(page.getByTestId('access-code-input')).toBeVisible();
  });

  test('submit button is disabled when input is empty', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('enter-access-code-btn').click();
    await expect(page.getByTestId('access-code-input')).toBeVisible();
    await expect(page.getByTestId('access-code-submit')).toBeDisabled();
  });

  test('3-character code is rejected with a validation error', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('enter-access-code-btn').click();
    await page.getByTestId('access-code-input').fill('abc');
    await page.getByTestId('access-code-submit').click();
    await expect(page.getByTestId('access-code-error')).toContainText(/4-32/i);
    expect(page.url()).toMatch(/\/login$/);
  });

  test('special-character code is rejected with a validation error', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('enter-access-code-btn').click();
    await page.getByTestId('access-code-input').fill('abc#$%');
    await page.getByTestId('access-code-submit').click();
    await expect(page.getByTestId('access-code-error')).toBeVisible();
    expect(page.url()).toMatch(/\/login$/);
  });

  test('valid lowercase code redirects to /code/{CODE} uppercase-normalised', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('enter-access-code-btn').click();
    await page.getByTestId('access-code-input').fill('test123abc');
    await page.getByTestId('access-code-submit').click();
    await expect(page).toHaveURL(/\/code\/TEST123ABC$/);
  });

  test('Cancel button collapses the form and shows the CTA again', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('enter-access-code-btn').click();
    await expect(page.getByTestId('access-code-input')).toBeVisible();

    await page.getByRole('button', { name: /cancel/i }).click();

    await expect(page.getByTestId('access-code-input')).toBeHidden();
    await expect(page.getByTestId('enter-access-code-btn')).toBeVisible();
  });

  test('Google login button is still visible (smoke)', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByRole('button', { name: /sign in with google/i })).toBeVisible();
  });
});
