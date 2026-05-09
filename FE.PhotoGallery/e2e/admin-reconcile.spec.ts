import { test, expect } from '@playwright/test';
import * as path from 'path';
import { LoginPage } from './pages/login.page';
import { AlbumDetailPage } from './pages/album-detail.page';
import { PhotoUploadPage } from './pages/photo-upload.page';
import { createAlbumViaApi, adminAuthHeaders } from './fixtures/auth.fixture';
import { getSamplePhotos } from './fixtures/data.fixture';
import { waitForPhotoProcessing } from './helpers/wait-for-processing';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';

interface ConsistencyReport {
  photosScanned: number;
  itemsCreatedPending: number;
  itemsBackFilledComplete: number;
  itemsRequeued: number;
  originalsMissing: number;
  queuesCreated: number;
  urlsInvalidated: number;
}

/**
 * E2E spec for D007: the admin reconcile endpoint successfully reports a
 * scan and the worker keeps the system honest over time.
 *
 * This is a smoke test (not a hard correctness test) because we can't easily
 * delete a quality version from MinIO from the Playwright runner. A future
 * iteration should plumb a /api/admin/test-utilities/delete-storage-key
 * endpoint guarded by the DISABLE_AUTH flag so we can prove end-to-end that
 * a deleted thumbnail gets back-filled by reconciliation.
 *
 * Reference: D007 (Storage/Database Consistency Reconciliation).
 */
test.describe('Admin reconcile-storage endpoint', () => {
  test('Returns a ConsistencyReport with non-negative counters', async ({ page, request }) => {
    test.setTimeout(120_000);

    const loginPage = new LoginPage(page);
    const albumPage = new AlbumDetailPage(page);
    const uploadPage = new PhotoUploadPage(page);

    await loginPage.loginAsDevAdmin();

    const album = await createAlbumViaApi(request, `E2E Reconcile ${Date.now()}`);
    await albumPage.gotoAlbum(album.id);

    const samplePhotos = getSamplePhotos(1);
    const photoFileName = path.basename(samplePhotos[0]);
    await uploadPage.chooseFiles(samplePhotos);
    await uploadPage.waitForUploadComplete(photoFileName);

    const listResponse = await request.get(`${BACKEND_BASE_URL}/api/albums/${album.id}/photos`, {
      headers: adminAuthHeaders()
    });
    const listJson = await listResponse.json() as { photos: Array<{ id: string; fileName: string }> };
    const photos = listJson.photos ?? [];
    const uploaded = photos.find(p => p.fileName === photoFileName)!;
    await waitForPhotoProcessing(request, uploaded.id);

    const reconcileResponse = await request.post(`${BACKEND_BASE_URL}/api/photos/admin/reconcile-storage`, {
      headers: adminAuthHeaders()
    });
    expect(
      reconcileResponse.ok(),
      `Reconcile failed: ${reconcileResponse.status()} ${await reconcileResponse.text()}`
    ).toBeTruthy();

    const report = (await reconcileResponse.json()) as ConsistencyReport;
    expect(report.photosScanned, 'photosScanned should include the photo we just uploaded').toBeGreaterThanOrEqual(1);
    expect(report.itemsCreatedPending).toBeGreaterThanOrEqual(0);
    expect(report.itemsBackFilledComplete).toBeGreaterThanOrEqual(0);
    expect(report.itemsRequeued).toBeGreaterThanOrEqual(0);
    expect(report.originalsMissing).toBeGreaterThanOrEqual(0);
    expect(report.queuesCreated).toBeGreaterThanOrEqual(0);
    expect(report.urlsInvalidated).toBeGreaterThanOrEqual(0);
  });
});
