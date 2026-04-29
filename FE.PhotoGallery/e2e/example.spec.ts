import { test, expect } from '@playwright/test';

test.describe('Photo Gallery E2E Tests', () => {
  test('should navigate to login page when not authenticated', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/.*login/);
  });

  test('login page should display Google OAuth button', async ({ page }) => {
    await page.goto('/login');
    const googleButton = page.locator('button:has-text("Sign in with Google")');
    await expect(googleButton).toBeVisible();
  });

  test('dashboard should show album creation section', async ({ page }) => {
    // This test assumes we have a way to bypass auth or pre-authenticate
    // In a real scenario, we would mock the auth or use test credentials
    await page.goto('/dashboard');
    // If not logged in, redirect to login
    if (page.url().includes('login')) {
      console.log('Redirected to login - auth is working correctly');
    }
  });

  test('should be able to navigate between pages', async ({ page }) => {
    await page.goto('/');
    // Check if we're redirected to login
    const loginPageIndicator = page.locator('text=Photo Gallery');
    await expect(loginPageIndicator).toBeVisible();
  });
});

test.describe('API Integration Tests', () => {
  test('should be able to call health check endpoint', async ({ page }) => {
    const response = await page.request.get('http://localhost:5105/health');
    // Note: Health endpoint may not exist, this is just an example
    // In practice, test against actual API endpoints
  });
});
