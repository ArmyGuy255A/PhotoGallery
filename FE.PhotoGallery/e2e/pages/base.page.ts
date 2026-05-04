import { Page, Locator } from '@playwright/test';

/**
 * Base page object that other page objects extend.
 *
 * Encapsulates the common pieces every page interaction needs (the underlying
 * Playwright `Page`, a stable `data-testid` selector helper, and a navigation
 * helper) so individual page objects only have to declare locators and
 * domain-specific actions.
 *
 * Reference: D006 (Frontend Testing Strategy — Playwright-First).
 */
export abstract class BasePage {
  constructor(protected readonly page: Page) {}

  /**
   * Locate an element by its `data-testid` attribute. All E2E selectors should
   * go through this helper so we never rely on CSS classes / text content
   * (both of which churn during normal UI work).
   *
   * Public so specs can use it ad-hoc for selectors that don't justify their
   * own page-object property.
   */
  byTestId(testId: string): Locator {
    return this.page.locator(`[data-testid="${testId}"]`);
  }

  /** Navigate to a path relative to the configured `baseURL`. */
  async goto(path: string): Promise<void> {
    await this.page.goto(path);
  }
}
