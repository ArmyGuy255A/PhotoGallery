import { test, expect } from '@playwright/test';
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
 * Phase 13E integration test — verifies the pre-signed URL caching architecture
 * actually offloads thumbnail downloads to MinIO instead of proxying through
 * the web server.
 *
 * What this guards against:
 *  - A regression that reverts thumbnailUrl back to a `/api/photos/.../download`
 *    proxy URL (would re-introduce the web-server bandwidth bottleneck).
 *  - A regression that drops the thumbnailUrl field from the API response
 *    entirely (would silently fall back to a placeholder).
 *  - A regression that points pre-signed URLs at the wrong host (HTTPS scheme
 *    issue we hit earlier, captured in the MinIO presign-protocol fix).
 *
 * The test correlates browser network activity with the API contract:
 *  1. Upload a photo and wait for processing
 *  2. GET /api/albums/{id}/photos and confirm `thumbnailUrl` is set and points
 *     at MinIO (not at the web server)
 *  3. Reload the album page so the browser loads the thumbnail
 *  4. Capture the actual `<img>` request URL via Playwright's network listener
 *  5. Assert the request hit MinIO and not the web-server origin
 *  6. Assert the image actually rendered (naturalWidth > 0)
 */
test.describe('Phase 13E — Pre-Signed URL Direct-from-MinIO Loading', () => {
  test('Album thumbnails load directly from MinIO, not proxied through web server', async ({ page, request }) => {
    test.setTimeout(120_000);

    const loginPage = new LoginPage(page);
    const albumPage = new AlbumDetailPage(page);
    const uploadPage = new PhotoUploadPage(page);

    await loginPage.loginAsDevAdmin();

    const album = await createAlbumViaApi(request, `E2E Phase 13E ${Date.now()}`);
    await albumPage.gotoAlbum(album.id);

    const samplePhotos = getSamplePhotos(1);
    const photoFileName = path.basename(samplePhotos[0]);

    await uploadPage.chooseFiles(samplePhotos);
    await uploadPage.waitForUploadComplete(photoFileName);

    // Find the uploaded photo via the API
    const listResponse = await request.get(`${BACKEND_BASE_URL}/api/albums/${album.id}/photos`, {
      headers: adminAuthHeaders()
    });
    expect(listResponse.ok(), `List photos failed: ${listResponse.status()}`).toBeTruthy();
    const listJson = await listResponse.json() as {
      photos: Array<{ id: string; fileName: string; thumbnailUrl?: string; mediumUrl?: string }>;
      totalCount: number;
    };
    const photos = listJson.photos ?? [];
    const uploaded = photos.find(p => p.fileName === photoFileName);
    expect(uploaded, `Uploaded photo "${photoFileName}" not found in album listing`).toBeDefined();

    // Wait for backend to finish all 4 quality versions + URL generation
    await waitForPhotoProcessing(request, uploaded!.id);

    // Re-fetch with URLs after processing completes
    const refreshedResponse = await request.get(`${BACKEND_BASE_URL}/api/albums/${album.id}/photos`, {
      headers: adminAuthHeaders()
    });
    const refreshedJson = await refreshedResponse.json() as {
      photos: Array<{ id: string; fileName: string; thumbnailUrl?: string; mediumUrl?: string }>;
    };
    const refreshed = (refreshedJson.photos ?? []).find(p => p.id === uploaded!.id);
    expect(refreshed, 'Photo missing from refreshed listing').toBeDefined();

    // CONTRACT 1: API must return a thumbnailUrl
    expect(refreshed!.thumbnailUrl, 'thumbnailUrl missing from API response — Phase 13 regression').toBeTruthy();

    // CONTRACT 2: The URL must point at MinIO, NOT the web server origin
    const thumbnailUrl = refreshed!.thumbnailUrl!;
    const webServerOrigin = new URL(BACKEND_BASE_URL).origin;
    expect(
      thumbnailUrl.startsWith(webServerOrigin),
      `thumbnailUrl=${thumbnailUrl} points at web server (${webServerOrigin}); ` +
        `Phase 13 architecture says it should point directly at MinIO. ` +
        `Are pre-signed URLs being generated correctly?`
    ).toBeFalsy();

    // CONTRACT 3: When the browser loads the page, the thumbnail request
    //             must actually go to that MinIO URL (not be intercepted by
    //             a service worker or rewritten to a web-server proxy).
    const thumbnailRequests: { url: string; method: string }[] = [];
    page.on('request', req => {
      const reqUrl = req.url();
      // Track image requests (resourceType is more reliable than URL pattern)
      if (req.resourceType() === 'image') {
        thumbnailRequests.push({ url: reqUrl, method: req.method() });
      }
    });

    await page.reload();
    await expect(albumPage.byTestId('album-detail')).toBeVisible({ timeout: 10_000 });

    // Wait for the card thumbnail to actually render (helper polls naturalWidth)
    const cardImage = page
      .locator(`[data-testid="photo-card"][data-photo-id="${refreshed!.id}"] [data-testid="photo-card-image"]`)
      .first();
    await expect(cardImage).toBeVisible({ timeout: 10_000 });
    await assertImageLoads(cardImage);

    // CONTRACT 4: At least one image request matched the MinIO host from the API URL
    const minioOrigin = new URL(thumbnailUrl).origin;
    const minioImageRequests = thumbnailRequests.filter(r => r.url.startsWith(minioOrigin));
    expect(
      minioImageRequests.length,
      `Browser made ${thumbnailRequests.length} image requests but none went to MinIO (${minioOrigin}). ` +
        `Phase 13 architecture is broken — thumbnails are being proxied through the web server. ` +
        `All requests: ${JSON.stringify(thumbnailRequests, null, 2)}`
    ).toBeGreaterThan(0);

    // CONTRACT 5: NO image requests went to the web server's proxied
    //             /api/photos/.../download endpoint. The whole point is to
    //             keep that traffic off the web server.
    const proxiedRequests = thumbnailRequests.filter(r =>
      r.url.startsWith(webServerOrigin) && /\/api\/photos\/.+\/download/.test(r.url)
    );
    expect(
      proxiedRequests.length,
      `Browser fell back to web-server proxy for ${proxiedRequests.length} thumbnails. ` +
        `Phase 13 expected zero. Proxied URLs: ${JSON.stringify(proxiedRequests, null, 2)}`
    ).toBe(0);
  });
});
