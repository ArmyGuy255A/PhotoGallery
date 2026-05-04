import { Page } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Login page object.
 *
 * In the local dev environment the backend runs with `DISABLE_AUTH=true`, which
 * means hitting the root URL auto-authenticates and redirects to /dashboard.
 * This page object exists so future tests can switch to a real login flow
 * without having to touch every spec.
 */
export class LoginPage extends BasePage {
  constructor(page: Page) {
    super(page);
  }

  /**
   * Trigger the auto-auth flow for DISABLE_AUTH=true environments and wait
   * for the dashboard to render.
   */
  async loginAsDevAdmin(): Promise<void> {
    await this.goto('/');
    await this.page.waitForURL(/.*\/dashboard.*/, { timeout: 10_000 });
  }
}
