/**
 * Playwright tests require the backend to run with ASPNETCORE_ENVIRONMENT=Test
 * so that DISABLE_AUTH=true (via appsettings.Test.json). The dev environment
 * will eventually run real Google auth and tests would otherwise need a real
 * Google account.
 *
 * Local: $env:ASPNETCORE_ENVIRONMENT="Test"; dotnet watch run --project PhotoGallery
 * CI:    set in workflow YAML before invoking 'npm run e2e'
 */
import { defineConfig, devices } from '@playwright/test';

/**
 * BASE_URL lets a single spec point at different deployment shapes:
 *   - http://localhost:4300                          (raw ng serve, default)
 *   - https://localhost:8000/photogallery            (local docker stack, S6)
 *   - https://appeid.app/photogallery                (Trial, post-deploy)
 *
 * IGNORE_HTTPS_ERRORS is auto-enabled for any https:// target so the
 * self-signed local docker cert + an Atypical Trial cert during rollout
 * don't block specs. Override with PLAYWRIGHT_STRICT_HTTPS=1 to opt back in.
 */
const BASE_URL = process.env['BASE_URL'] ?? 'http://localhost:4300';
const STRICT_HTTPS = process.env['PLAYWRIGHT_STRICT_HTTPS'] === '1';
const IGNORE_HTTPS_ERRORS = !STRICT_HTTPS && BASE_URL.startsWith('https://');

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  workers: process.env['CI'] ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: BASE_URL,
    ignoreHTTPSErrors: IGNORE_HTTPS_ERRORS,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure'
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] }
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] }
    }
  ],

  webServer: BASE_URL === 'http://localhost:4300'
    ? {
        command: 'ng serve',
        url: 'http://localhost:4300',
        reuseExistingServer: !process.env['CI']
      }
    : undefined
});
