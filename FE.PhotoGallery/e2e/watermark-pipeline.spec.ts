import { test, expect, APIRequestContext } from '@playwright/test';
import * as path from 'path';
import { LoginPage } from './pages/login.page';
import { AlbumDetailPage } from './pages/album-detail.page';
import { PhotoUploadPage } from './pages/photo-upload.page';
import { createAlbumViaApi, adminAuthHeaders } from './fixtures/auth.fixture';
import { getSamplePhotos } from './fixtures/data.fixture';
import { waitForPhotoProcessing } from './helpers/wait-for-processing';
import { assertImageLoads } from './helpers/assert-image-loads';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';

/**
 * B3 / PR #21 — Watermark Pipeline end-to-end coverage.
 *
 * Confirms the D009 watermark architecture as observed by real browser users:
 *
 *  1. An authenticated admin viewing the photo modal on /albums/{id} sees the
 *     UNwatermarked Medium variant (admin tooling stays clean).
 *  2. An anonymous visitor at /code/{code} sees the watermarked Medium variant
 *     in the modal — the watermark is the deterrent for casual screen-grab and
 *     AI-removal abuse, while the unwatermarked Medium stays gated behind cart
 *     checkout.
 *  3. The public photo-list endpoint (`GET /api/code/{code}/photos`) returns
 *     `mediumUrl` values that point at MinIO (port 9000) and contain the
 *     `medium-watermarked` path segment — i.e. the public URL contract is
 *     direct-from-storage AND watermarked.
 *
 * Setup is shared via `test.beforeAll`/`test.describe.serial` because creating
 * an album, uploading a photo, and waiting for the four-quality processing
 * pipeline takes ~30s and would dominate runtime if repeated per test.
 */

interface AdminPhoto {
  id: string;
  fileName: string;
  thumbnailUrl?: string;
  mediumUrl?: string;
}

interface PublicPhoto {
  photoId: string;
  fileName: string;
  thumbnailUrl?: string;
  mediumUrl?: string;
}

async function createAccessCode(
  request: APIRequestContext,
  albumId: string
): Promise<string> {
  const response = await request.post(
    `${BACKEND_BASE_URL}/api/albums/${albumId}/access-codes`,
    {
      headers: { 'Content-Type': 'application/json', ...adminAuthHeaders() },
      data: { expiresForever: true }
    }
  );
  expect(
    response.ok(),
    `Failed to create access code: ${response.status()} ${await response.text()}`
  ).toBeTruthy();
  const body = (await response.json()) as { code: string };
  expect(body.code, 'Access code response missing `code` field').toBeTruthy();
  return body.code;
}

test.describe.serial('B3 — Watermark Pipeline', () => {
  // Shared state populated by Test 1's setup phase and consumed by Tests 2 and 3.
  // This avoids the cross-context auth dance of doing setup in `test.beforeAll`
  // (admin needs a real browser session for the upload UI).
  let albumId: string;
  let photoId: string;
  let accessCode: string;

  test('1. Admin photo modal shows the unwatermarked Medium variant', async ({ page, request }) => {
    test.setTimeout(180_000);

    const loginPage = new LoginPage(page);
    const albumPage = new AlbumDetailPage(page);
    const uploadPage = new PhotoUploadPage(page);

    await loginPage.loginAsDevAdmin();

    const album = await createAlbumViaApi(request, `E2E B3 Watermark ${Date.now()}`);
    albumId = album.id;

    // App.component.ngOnInit hijacks any deep-link page.goto() and redirects to
    // /dashboard (it always Router.navigate(['/dashboard']) after /me returns).
    // To reach /albums/{id}, do an SPA-internal pushState + popstate so Angular
    // Router routes us there without re-running app init.
    await page.evaluate((url) => {
      window.history.pushState({}, '', url);
      window.dispatchEvent(new PopStateEvent('popstate'));
    }, `/albums/${albumId}`);
    await page.waitForURL(new RegExp(`/albums/${albumId}$`), { timeout: 10_000 });
    await expect(albumPage.byTestId('album-detail')).toBeVisible({ timeout: 10_000 });

    const samplePhotos = getSamplePhotos(1);
    const photoFileName = path.basename(samplePhotos[0]);
    await uploadPage.chooseFiles(samplePhotos);
    // The upload-item's data-upload-status terminal value is `complete` (or
    // `error`) — not `success` as the legacy helper expects. Wait for it
    // explicitly so we don't depend on the page object's outdated regex.
    await expect(uploadPage.uploadItemByFileName(photoFileName)).toHaveAttribute(
      'data-upload-status',
      /complete|error/,
      { timeout: 60_000 }
    );

    const listResponse = await request.get(
      `${BACKEND_BASE_URL}/api/albums/${albumId}/photos`,
      { headers: adminAuthHeaders() }
    );
    expect(listResponse.ok(), `List photos failed: ${listResponse.status()}`).toBeTruthy();
    const listJson = (await listResponse.json()) as { photos: AdminPhoto[] };
    const uploaded = (listJson.photos ?? []).find(p => p.fileName === photoFileName);
    expect(uploaded, `Uploaded photo "${photoFileName}" not found in album listing`).toBeDefined();
    photoId = uploaded!.id;

    await waitForPhotoProcessing(request, photoId, { timeoutMs: 90_000 });

    // Pre-create the access code now while we still have admin auth on hand;
    // Tests 2 and 3 re-use it without needing to re-authenticate as admin.
    accessCode = await createAccessCode(request, albumId);

    // Reload via SPA navigation (full page.reload() would re-trigger app init
    // which always redirects to /dashboard). pushState + popstate keeps us on
    // /albums/{id}; album-detail's polling has already picked up mediumUrl as
    // soon as processing completed.
    await expect(albumPage.byTestId('album-detail')).toBeVisible({ timeout: 10_000 });

    // Sanity-check the API that drives the page reflects processing complete.
    const refreshedResponse = await request.get(
      `${BACKEND_BASE_URL}/api/albums/${albumId}/photos`,
      { headers: adminAuthHeaders() }
    );
    expect(refreshedResponse.ok()).toBeTruthy();
    const refreshedJson = (await refreshedResponse.json()) as { photos: AdminPhoto[] };
    const photo = (refreshedJson.photos ?? []).find(p => p.id === photoId);
    expect(photo, 'Photo missing from admin listing').toBeDefined();
    expect(photo!.mediumUrl, 'Admin mediumUrl missing').toBeTruthy();
    expect(
      photo!.mediumUrl!,
      `Admin should receive UNwatermarked mediumUrl, got: ${photo!.mediumUrl}`
    ).not.toContain('medium-watermarked');

    // Open the modal by clicking the photo card.
    await albumPage.cardByPhotoId(photoId).click();

    const modalImg = page.locator('app-photo-modal img.modal-image');
    await expect(modalImg).toBeVisible({ timeout: 10_000 });

    // Assert the modal's currentSrc resolved to the unwatermarked Medium.
    await expect
      .poll(async () => modalImg.evaluate((el: HTMLImageElement) => el.currentSrc || el.src), {
        timeout: 10_000
      })
      .toMatch(/.+/);

    const modalSrc = await modalImg.evaluate((el: HTMLImageElement) => el.currentSrc || el.src);
    expect(
      modalSrc,
      `Admin modal must NOT load the watermarked Medium, got: ${modalSrc}`
    ).not.toContain('medium-watermarked');

    // And the image actually rendered (naturalWidth > 0).
    await assertImageLoads(modalImg);
  });

  test('2. Guest at /code/{code} sees the watermarked Medium in the modal', async ({ page }) => {
    test.setTimeout(60_000);

    // Guest = no auth. Wipe any cookies/storage from the dev-admin auto-login
    // path so we hit the /code/{code} route as an anonymous visitor.
    await page.context().clearCookies();

    // App.component.ngOnInit unconditionally calls `/api/auth/me` and then
    // Router.navigate(['/dashboard']) on success or '/login' on failure — that
    // hijacks any deep-linked goto, including /code/{code}. Stub the /me call
    // so neither the success nor error branch runs and the SPA stays on the
    // route we navigate to.
    await page.route('**/api/auth/me', () => {
      // intentionally never resolve — keeps the Observable in-flight
    });

    await page.goto(`/code/${accessCode}`);

    // Code-gallery renders inline photo cards once the photos load.
    const photoCard = page.locator('.code-gallery .photo-card').first();
    await expect(photoCard).toBeVisible({ timeout: 15_000 });

    // Open the modal — the click handler is on .photo-thumb inside the card.
    await photoCard.locator('.photo-thumb').click();

    const modalImg = page.locator('app-photo-modal img.modal-image');
    await expect(modalImg).toBeVisible({ timeout: 10_000 });

    // Wait until the browser has resolved a real currentSrc for the modal image
    // (Angular binds [src] asynchronously and currentSrc is empty until fetch).
    await expect
      .poll(async () => modalImg.evaluate((el: HTMLImageElement) => el.currentSrc || el.src), {
        timeout: 10_000
      })
      .toContain('medium-watermarked.jpg');

    const modalSrc = await modalImg.evaluate((el: HTMLImageElement) => el.currentSrc || el.src);
    expect(
      modalSrc,
      `Guest modal must load medium-watermarked.jpg, got: ${modalSrc}`
    ).toContain('medium-watermarked.jpg');

    // And the watermarked file actually exists in storage and decoded.
    await assertImageLoads(modalImg);
  });

  test('3. Public /api/code/{code}/photos returns watermarked mediumUrl pointing at MinIO', async ({ request }) => {
    const response = await request.get(`${BACKEND_BASE_URL}/api/code/${accessCode}/photos`);
    expect(
      response.ok(),
      `Public photos endpoint failed: ${response.status()} ${await response.text()}`
    ).toBeTruthy();

    const body = (await response.json()) as { photos: PublicPhoto[] };
    expect(body.photos, 'Public photos response missing `photos` array').toBeDefined();
    expect(body.photos.length, 'Public album should expose at least one photo').toBeGreaterThan(0);

    const first = body.photos[0];
    expect(first.mediumUrl, 'Public mediumUrl missing on photo[0]').toBeTruthy();

    // CONTRACT 1: The public Medium variant is the watermarked version.
    expect(
      first.mediumUrl!,
      `Public mediumUrl must contain "medium-watermarked", got: ${first.mediumUrl}`
    ).toContain('medium-watermarked');

    // CONTRACT 2: It points directly at MinIO (port 9000 in dev), NOT proxied
    //             through the web server (port 5105). This guards against
    //             accidentally re-routing public traffic through the API host.
    const webServerOrigin = new URL(BACKEND_BASE_URL).origin;
    expect(
      first.mediumUrl!.startsWith(webServerOrigin),
      `Public mediumUrl=${first.mediumUrl} points at the web server (${webServerOrigin}); ` +
        `it must point directly at MinIO origin instead.`
    ).toBeFalsy();

    const mediumOrigin = new URL(first.mediumUrl!).origin;
    expect(
      mediumOrigin,
      `Public mediumUrl origin should be the MinIO host on port 9000, got origin: ${mediumOrigin}`
    ).toContain(':9000');
  });
});
