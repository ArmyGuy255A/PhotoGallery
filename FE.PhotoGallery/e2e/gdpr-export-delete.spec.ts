import { test, expect } from '@playwright/test';
import { adminAuthHeaders } from './fixtures/auth.fixture';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';

/**
 * EPIC-02 C — GDPR data export + erasure (PR #28).
 *
 * API-only coverage for /api/account/me/export and DELETE /api/account/me.
 *
 * Environment caveat: the dev stack runs with DISABLE_AUTH=true, which makes
 * `DisableAuthMiddleware` (Middleware/DisableAuthMiddleware.cs) auto-attach a
 * ClaimsPrincipal for `testadmin@localhost` to EVERY request, regardless of
 * the bearer token (or lack thereof). That makes the "missing-auth -> 401"
 * scenarios (Tests B and C) untestable in this environment, so they're
 * skipped with explanatory messages rather than producing false negatives.
 *
 * The destructive delete scenario (Test D) is also skipped: the dev admin is
 * shared by other specs and must not be removed, and the codebase exposes no
 * test-only seed endpoint to provision a throwaway user (verified by
 * grepping PhotoGallery/Controllers — only AccountGdprController exists on
 * the /api/account/me path, no /api/test/users or similar).
 */
test.describe('EPIC-02 C — GDPR export + delete', () => {
  test('A: GET /api/account/me/export returns a valid JSON envelope', async ({ request }) => {
    const res = await request.get(`${BACKEND_BASE_URL}/api/account/me/export`, {
      headers: adminAuthHeaders()
    });

    expect(res.status(), `export status: ${res.status()} ${await res.text().catch(() => '')}`).toBe(200);

    const headers = res.headers();
    expect(headers['content-type'] ?? '').toContain('application/json');

    const disposition = headers['content-disposition'] ?? '';
    expect(disposition.toLowerCase()).toContain('attachment');
    expect(disposition).toMatch(/filename=.*\.json/i);

    const body = (await res.json()) as {
      exportTimestamp?: string;
      schemaVersion?: string;
      profile?: { email?: string };
      ownedAlbums?: unknown[];
      uploadedPhotos?: unknown[];
      savedAccessCodes?: unknown[];
      downloads?: unknown[];
    };

    // Top-level envelope fields (UserDataExport record in Services/UserDataExport.cs).
    expect(body, 'export body').toBeTruthy();
    expect(body.exportTimestamp, 'exportTimestamp').toBeTruthy();
    expect(body.schemaVersion, 'schemaVersion').toBe('1.0');
    expect(body.profile, 'profile').toBeTruthy();
    expect(Array.isArray(body.ownedAlbums), 'ownedAlbums is array').toBe(true);
    expect(Array.isArray(body.uploadedPhotos), 'uploadedPhotos is array').toBe(true);
    expect(Array.isArray(body.savedAccessCodes), 'savedAccessCodes is array').toBe(true);
    expect(Array.isArray(body.downloads), 'downloads is array').toBe(true);

    // Profile must surface a non-empty email for the caller (testadmin in dev).
    expect(typeof body.profile!.email).toBe('string');
    expect((body.profile!.email ?? '').length, 'profile.email non-empty').toBeGreaterThan(0);
  });

  test('B: GET /api/account/me/export requires auth', async ({ request }) => {
    test.skip(
      true,
      'DISABLE_AUTH=true — DisableAuthMiddleware unconditionally attaches the ' +
        'testadmin principal to every request, so unauthenticated requests are ' +
        'never observed in this environment. Re-enable in a stack where ' +
        'DISABLE_AUTH is unset to assert 401.'
    );

    const res = await request.get(`${BACKEND_BASE_URL}/api/account/me/export`);
    expect(res.status()).toBe(401);
  });

  test('C: DELETE /api/account/me requires auth', async ({ request }) => {
    test.skip(
      true,
      'DISABLE_AUTH=true — same reason as Test B. With auth disabled an ' +
        'unauthenticated DELETE would actually wipe the dev admin, so we ' +
        'guard this scenario behind an explicit skip in the dev stack.'
    );

    const res = await request.delete(`${BACKEND_BASE_URL}/api/account/me`);
    expect(res.status()).toBe(401);
  });

  test('D: DELETE /api/account/me hard-deletes the caller (destructive)', async ({ request: _request }) => {
    test.skip(
      true,
      'No test-seed endpoint exists to provision a throwaway user (verified: ' +
        'no /api/test/users or seed routes in PhotoGallery/Controllers), and ' +
        'DISABLE_AUTH=true means the only authenticated identity is the shared ' +
        'dev admin (testadmin@localhost) which other specs depend on. Running ' +
        'the destructive path here would break the suite. Manual / integration ' +
        'test required for full delete coverage.\n' +
        'TODO: when an admin "create user" or test-seed endpoint lands, replace ' +
        'this skip with: create temp user -> obtain its JWT -> DELETE ' +
        '/api/account/me -> assert 204 -> assert subsequent /api/account/me/export ' +
        'returns 401/404 -> assert audit log row (action="user.deleted").'
    );
  });
});
