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

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  workers: process.env['CI'] ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:4200',
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

  webServer: {
    command: 'ng serve',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env['CI']
  }
});
