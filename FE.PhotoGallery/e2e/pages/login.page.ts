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
   *
   * The Angular SPA gates the dashboard on a non-expired AppToken JWT in
   * localStorage. With DISABLE_AUTH=true the backend's `/api/auth/me`
   * authenticates any caller as the seeded test admin and returns a fresh JWT
   * (see PhotoGallery.Middleware.DisableAuthMiddleware). We mint that token
   * via the API context, seed it into localStorage as `AppToken`, and only
   * then navigate to root — at which point AppComponent.ngOnInit's
   * isAuthenticatedSync() check passes and forwards us to /dashboard.
   */
  async loginAsDevAdmin(): Promise<void> {
    const apiBase = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';
    const devToken = process.env['DEV_BEARER_TOKEN'] ?? 'test-token';

    // Mint a real JWT against the DISABLE_AUTH bypass.
    const response = await this.page.request.get(`${apiBase}/api/auth/me`, {
      headers: { Authorization: `Bearer ${devToken}` }
    });
    if (!response.ok()) {
      throw new Error(
        `loginAsDevAdmin: /api/auth/me returned ${response.status()} — is DISABLE_AUTH=true on the backend? Body: ${await response.text()}`
      );
    }
    const body = (await response.json()) as { accessToken: string };
    if (!body.accessToken) {
      throw new Error('loginAsDevAdmin: /api/auth/me did not return accessToken');
    }
    const accessToken = body.accessToken;

    // localStorage is per-origin, so we must be on the SPA origin before we
    // can set it. A trip to /login (the unauthenticated landing) is safe and
    // does not redirect.
    await this.goto('/login');
    await this.page.evaluate(token => {
      window.localStorage.setItem('appToken', token);
    }, accessToken);

    await this.page.goto('/dashboard');
    await this.page.waitForURL(/.*\/dashboard.*/, { timeout: 10_000 });
  }
}
