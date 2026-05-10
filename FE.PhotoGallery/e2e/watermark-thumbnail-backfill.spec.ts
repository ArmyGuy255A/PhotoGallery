import { test, expect, APIRequestContext } from '@playwright/test';
import * as path from 'path';
import sharp from 'sharp';
import { LoginPage } from './pages/login.page';
import { AlbumDetailPage } from './pages/album-detail.page';
import { PhotoUploadPage } from './pages/photo-upload.page';
import { createAlbumViaApi, adminAuthHeaders } from './fixtures/auth.fixture';
import { getSamplePhotos } from './fixtures/data.fixture';
import { waitForPhotoProcessing } from './helpers/wait-for-processing';
import { deleteObject, objectExists, variantKey } from './helpers/minio-admin';

const BACKEND_BASE_URL = process.env['BACKEND_BASE_URL'] ?? 'http://localhost:5105';

/**
 * PR #48 — Watermark thumbnail backfill end-to-end coverage.
 *
 * Closes coverage gaps left by the existing watermark-pipeline spec for the
 * thumbnail-watermarked variant introduced in this PR:
 *
 *  A. `GET /api/code/{code}/photos` returns a `thumbnailUrl` whose path
 *     contains `thumbnail-watermarked.jpg` (mirrors Test 3's `mediumUrl`
 *     contract).
 *  B. Image-diff: the public (watermarked) thumbnail differs from the admin
 *     (unwatermarked) thumbnail by >0.5% in the bottom-right quadrant — i.e.
 *     the watermark is actually visible, not just present in the URL.
 *  C. Self-heal backfill: deleting the thumbnail-watermarked.jpg object from
 *     MinIO and then fetching the public thumbnail URL succeeds (HTTP 200,
 *     valid JPEG bytes, latency ≤ 2000ms) and re-creates the missing object.
 *
 * Tests run serial because they share an album/photo/access-code created once.
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
  return body.code;
}

async function fetchImageBytes(
  request: APIRequestContext,
  url: string,
  headers: Record<string, string> = {}
): Promise<Buffer> {
  const response = await request.get(url, { headers });
  expect(
    response.ok(),
    `Image fetch failed for ${url}: ${response.status()}`
  ).toBeTruthy();
  const ab = await response.body();
  return Buffer.from(ab);
}

/**
 * Mean per-pixel RGB difference within the bottom-right quadrant of two
 * images, expressed as a fraction (0.0 – 1.0). Both images are resized to a
 * common 256x256 grid before comparison so origin-server resizing differences
 * do not skew the result.
 */
async function bottomRightQuadrantDiff(
  a: Buffer,
  b: Buffer
): Promise<{ diffFraction: number; sampleSize: number }> {
  const SIZE = 256;
  const HALF = SIZE / 2;

  const decode = (buf: Buffer) =>
    sharp(buf)
      .resize(SIZE, SIZE, { fit: 'fill' })
      .removeAlpha()
      .raw()
      .toBuffer({ resolveWithObject: true });

  const [imgA, imgB] = await Promise.all([decode(a), decode(b)]);

  const channels = imgA.info.channels; // 3 (RGB) after removeAlpha
  expect(imgB.info.channels).toBe(channels);

  // Walk only the bottom-right quadrant: rows [HALF..SIZE), cols [HALF..SIZE).
  let totalAbsDiff = 0;
  let pixelCount = 0;
  for (let y = HALF; y < SIZE; y++) {
    for (let x = HALF; x < SIZE; x++) {
      const idx = (y * SIZE + x) * channels;
      let pixelDiff = 0;
      for (let c = 0; c < channels; c++) {
        pixelDiff += Math.abs(imgA.data[idx + c] - imgB.data[idx + c]);
      }
      totalAbsDiff += pixelDiff / channels;
      pixelCount += 1;
    }
  }

  const meanPerPixelDiff = totalAbsDiff / pixelCount; // 0..255
  return { diffFraction: meanPerPixelDiff / 255, sampleSize: pixelCount };
}

test.describe.serial('PR #48 — Watermark thumbnail backfill', () => {
  let albumId: string;
  let photoId: string;
  let photoFileName: string;
  let accessCode: string;

  test.beforeAll(async ({ browser, request }) => {
    test.setTimeout(180_000);

    const context = await browser.newContext();
    const page = await context.newPage();

    try {
      const loginPage = new LoginPage(page);
      const albumPage = new AlbumDetailPage(page);
      const uploadPage = new PhotoUploadPage(page);

      await loginPage.loginAsDevAdmin();

      const album = await createAlbumViaApi(
        request,
        `E2E PR48 ThumbBackfill ${Date.now()}`
      );
      albumId = album.id;

      // SPA-internal navigation — see watermark-pipeline.spec.ts for the
      // explanation of why a plain page.goto() doesn't survive AppComponent
      // bootstrap.
      await page.evaluate((url) => {
        window.history.pushState({}, '', url);
        window.dispatchEvent(new PopStateEvent('popstate'));
      }, `/albums/${albumId}`);
      await page.waitForURL(new RegExp(`/albums/${albumId}$`), { timeout: 10_000 });
      await expect(albumPage.byTestId('album-detail')).toBeVisible({ timeout: 10_000 });

      const samplePhotos = getSamplePhotos(1);
      photoFileName = path.basename(samplePhotos[0]);
      await uploadPage.chooseFiles(samplePhotos);
      await expect(uploadPage.uploadItemByFileName(photoFileName)).toHaveAttribute(
        'data-upload-status',
        /complete|error/,
        { timeout: 60_000 }
      );

      const listResponse = await request.get(
        `${BACKEND_BASE_URL}/api/albums/${albumId}/photos`,
        { headers: adminAuthHeaders() }
      );
      expect(listResponse.ok()).toBeTruthy();
      const listJson = (await listResponse.json()) as { photos: AdminPhoto[] };
      const uploaded = (listJson.photos ?? []).find(p => p.fileName === photoFileName);
      expect(uploaded, `Uploaded photo "${photoFileName}" not found`).toBeDefined();
      photoId = uploaded!.id;

      await waitForPhotoProcessing(request, photoId, { timeoutMs: 90_000 });

      accessCode = await createAccessCode(request, albumId);
    } finally {
      await context.close();
    }
  });

  test('A. Public /api/code/{code}/photos returns thumbnailUrl with thumbnail-watermarked.jpg', async ({ request }) => {
    const response = await request.get(`${BACKEND_BASE_URL}/api/code/${accessCode}/photos`);
    expect(
      response.ok(),
      `Public photos endpoint failed: ${response.status()} ${await response.text()}`
    ).toBeTruthy();

    const body = (await response.json()) as { photos: PublicPhoto[] };
    expect(body.photos?.length, 'Public album should expose at least one photo').toBeGreaterThan(0);

    const ours = body.photos.find(p => p.photoId === photoId) ?? body.photos[0];
    expect(ours.thumbnailUrl, 'Public thumbnailUrl missing').toBeTruthy();

    expect(
      ours.thumbnailUrl!,
      `Public thumbnailUrl must contain "thumbnail-watermarked.jpg", got: ${ours.thumbnailUrl}`
    ).toContain('thumbnail-watermarked.jpg');

    // Sanity: same MinIO-direct contract as mediumUrl — must not proxy through
    // the API host.
    const webOrigin = new URL(BACKEND_BASE_URL).origin;
    expect(
      ours.thumbnailUrl!.startsWith(webOrigin),
      `Public thumbnailUrl=${ours.thumbnailUrl} unexpectedly proxies via the API host (${webOrigin}).`
    ).toBeFalsy();
  });

  test('B. Public watermarked thumbnail visibly differs from admin thumbnail in bottom-right quadrant', async ({ request }) => {
    const adminResp = await request.get(
      `${BACKEND_BASE_URL}/api/albums/${albumId}/photos`,
      { headers: adminAuthHeaders() }
    );
    expect(adminResp.ok()).toBeTruthy();
    const adminPhotos = ((await adminResp.json()) as { photos: AdminPhoto[] }).photos;
    const adminPhoto = adminPhotos.find(p => p.id === photoId);
    expect(adminPhoto?.thumbnailUrl, 'Admin thumbnailUrl missing').toBeTruthy();
    expect(
      adminPhoto!.thumbnailUrl!,
      `Admin thumbnailUrl should NOT be watermarked, got: ${adminPhoto!.thumbnailUrl}`
    ).not.toContain('thumbnail-watermarked');

    const publicResp = await request.get(`${BACKEND_BASE_URL}/api/code/${accessCode}/photos`);
    expect(publicResp.ok()).toBeTruthy();
    const publicPhotos = ((await publicResp.json()) as { photos: PublicPhoto[] }).photos;
    const publicPhoto = publicPhotos.find(p => p.photoId === photoId) ?? publicPhotos[0];
    expect(publicPhoto.thumbnailUrl).toBeTruthy();

    const adminBytes = await fetchImageBytes(request, adminPhoto!.thumbnailUrl!);
    const publicBytes = await fetchImageBytes(request, publicPhoto.thumbnailUrl!);

    expect(adminBytes.length, 'Admin thumbnail bytes empty').toBeGreaterThan(0);
    expect(publicBytes.length, 'Public thumbnail bytes empty').toBeGreaterThan(0);

    const { diffFraction, sampleSize } = await bottomRightQuadrantDiff(adminBytes, publicBytes);
    const pct = (diffFraction * 100).toFixed(2);
    // eslint-disable-next-line no-console
    console.log(`[watermark-thumbnail-backfill] bottom-right quadrant differs by ${pct}% (${sampleSize} px sampled)`);

    expect(
      diffFraction,
      `Bottom-right quadrant differs by only ${pct}% — watermark not visibly applied to thumbnail`
    ).toBeGreaterThan(0.005);
  });

  test('C. Self-heals when thumbnail-watermarked.jpg is missing in MinIO', async ({ request }) => {
    const objectKey = variantKey(albumId, photoId, 'thumbnail-watermarked.jpg');

    // Pre-condition: the object should currently exist (Test A would have
    // proven the URL works). Delete it via the test-only MinIO helper.
    const existedBefore = await objectExists(objectKey);
    expect(existedBefore, `Expected ${objectKey} to exist before deletion`).toBeTruthy();
    await deleteObject(objectKey);

    const stillThere = await objectExists(objectKey);
    expect(stillThere, `Failed to delete ${objectKey} from MinIO`).toBeFalsy();

    // Re-fetch the public URL list. The /api/code/{code}/photos handler
    // calls PhotoVersionUrlService.GenerateShortLivedUrlAsync which detects
    // the missing watermarked object and self-heals (download unwatermarked
    // → watermark → upload) BEFORE returning the URL. So total first-fetch
    // latency = the /api/code call itself + the subsequent URL fetch.
    const start = Date.now();
    const listResp = await request.get(`${BACKEND_BASE_URL}/api/code/${accessCode}/photos`);
    expect(listResp.ok()).toBeTruthy();
    const photos = ((await listResp.json()) as { photos: PublicPhoto[] }).photos;
    const publicPhoto = photos.find(p => p.photoId === photoId) ?? photos[0];
    const thumbnailUrl = publicPhoto.thumbnailUrl!;
    expect(thumbnailUrl).toContain('thumbnail-watermarked.jpg');

    const fetchResp = await request.get(thumbnailUrl);
    const latencyMs = Date.now() - start;
    // eslint-disable-next-line no-console
    console.log(`[watermark-thumbnail-backfill] first-fetch: ${latencyMs}ms`);

    expect(
      fetchResp.ok(),
      `Backfill self-heal fetch failed: ${fetchResp.status()} ${await fetchResp.text()}`
    ).toBeTruthy();

    const bodyBuf = Buffer.from(await fetchResp.body());
    expect(bodyBuf.length, 'Self-healed thumbnail body empty').toBeGreaterThan(0);

    // Validate the bytes are a real JPEG by decoding metadata (sharp will
    // throw if the buffer is not a valid image).
    const meta = await sharp(bodyBuf).metadata();
    expect(meta.format, `Self-healed thumbnail is not a JPEG: ${meta.format}`).toBe('jpeg');
    expect((meta.width ?? 0) > 0 && (meta.height ?? 0) > 0).toBeTruthy();

    expect(
      latencyMs,
      `Self-heal first-fetch took ${latencyMs}ms (>2000ms ceiling)`
    ).toBeLessThanOrEqual(2000);

    // Post-condition: the object now exists again in MinIO (uploaded by the
    // backfill). Allow a brief eventual-consistency grace window.
    let recreated = false;
    for (let attempt = 0; attempt < 5; attempt++) {
      if (await objectExists(objectKey)) {
        recreated = true;
        break;
      }
      await new Promise(r => setTimeout(r, 200));
    }
    expect(
      recreated,
      `Backfill did not re-create ${objectKey} in MinIO after self-heal`
    ).toBeTruthy();
  });
});
