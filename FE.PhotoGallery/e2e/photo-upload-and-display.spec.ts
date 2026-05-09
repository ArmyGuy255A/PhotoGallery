import { test, expect } from '@playwright/test';
import * as path from 'path';
import { LoginPage } from './pages/login.page';
import { AlbumDetailPage } from './pages/album-detail.page';
import { PhotoUploadPage } from './pages/photo-upload.page';
import { createAlbumViaApi, adminAuthHeaders } from './fixtures/auth.fixture';
import { getSamplePhotos } from './fixtures/data.fixture';
import { waitForPhotoProcessing } from './helpers/wait-for-processing';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';

/**
 * Regression spec for D006/D008: photo card thumbnails on the album detail
 * page must actually render after upload, not show the broken-image icon.
 *
 * Why this exists: the in-component thumbnail (inside the Upload Photos
 * control) and the album-grid card thumbnail use _different_ URL strategies.
 * The upload component blob-URLs the local file directly, while the cards
 * consume pre-signed MinIO URLs from the API. A Karma/Jasmine spec with a
 * mocked HttpClient cannot tell whether the `<img>` actually loaded — only
 * Playwright with a real browser + real backend + real MinIO can.
 *
 * Reference: D006 (Frontend Testing — Playwright-First),
 *            D008 (Cached Pre-Signed URL Storage Verification).
 */
test.describe('Photo upload + display regression', () => {
  test('Uploaded photo renders its thumbnail in the album-detail card', async ({ page, request }) => {
    test.setTimeout(120_000);

    const loginPage = new LoginPage(page);
    const albumPage = new AlbumDetailPage(page);
    const uploadPage = new PhotoUploadPage(page);

    await loginPage.loginAsDevAdmin();

    const album = await createAlbumViaApi(request, `E2E Thumbnail Regression ${Date.now()}`);
    await albumPage.gotoAlbum(album.id);
    await expect(albumPage.title).toContainText(album.title);

    const samplePhotos = getSamplePhotos(1);
    const photoFileName = path.basename(samplePhotos[0]);

    await uploadPage.chooseFiles(samplePhotos);
    await uploadPage.waitForUploadComplete(photoFileName);
    await uploadPage.expectInComponentThumbnailVisible(photoFileName);

    const listResponse = await request.get(`${BACKEND_BASE_URL}/api/albums/${album.id}/photos`, {
      headers: adminAuthHeaders()
    });
    expect(listResponse.ok(), `List photos failed: ${listResponse.status()}`).toBeTruthy();
    const listJson = await listResponse.json() as { photos: Array<{ id: string; fileName: string }> };
    const photos = listJson.photos ?? [];
    const uploaded = photos.find(p => p.fileName === photoFileName);
    expect(uploaded, `Uploaded photo "${photoFileName}" not found in album listing`).toBeDefined();

    await waitForPhotoProcessing(request, uploaded!.id);

    await page.reload();
    await expect(albumPage.byTestId('album-detail')).toBeVisible({ timeout: 10_000 });

    await albumPage.expectCardThumbnailLoaded(uploaded!.id);
  });
});

declare global {
  // Playwright extends the global `expect` with `any` matchers we use through `expect.any(Number)`.
  // No additional augmentation needed; this block is intentionally left empty as documentation.
}
