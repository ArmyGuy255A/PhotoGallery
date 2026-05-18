import { test, expect } from '@playwright/test';
import * as path from 'path';
import { LoginPage } from './pages/login.page';
import { AlbumDetailPage } from './pages/album-detail.page';
import { PhotoUploadPage } from './pages/photo-upload.page';
import { createAlbumViaApi, adminAuthHeaders } from './fixtures/auth.fixture';
import { getSamplePhotos } from './fixtures/data.fixture';

/**
 * Story S4 / Issue #163 — SignalR upload progress through the /photogallery
 * proxy.
 *
 * What this spec proves:
 *   1. The SPA-served `${environment.apiUrl}/hubs/photo-progress` URL resolves
 *      correctly through the reverse-proxy base-path prefix (e.g.
 *      `/photogallery/hubs/photo-progress` when served behind a path-stripping
 *      proxy).
 *   2. The WebSocket upgrade survives the proxy chain (no mangled
 *      Upgrade/Connection headers).
 *   3. At least one SignalR progress event (`ProcessingStarted`,
 *      `ProcessingProgress`, `ProcessingCompleted`, or `WatermarkCompleted`)
 *      is delivered to the browser after an upload.
 *
 * How it works:
 *   - There is currently no FE component that subscribes to
 *     `PhotoProgressService` (the service exists but no UI consumer is wired
 *     yet — that arrives with the next FE story). So this spec opens its own
 *     `HubConnection` inside the page context using the `@microsoft/signalr`
 *     browser bundle shipped in `node_modules`. The hub URL is constructed
 *     **identically** to `PhotoProgressService.buildHub()` —
 *     `${apiUrl}/hubs/photo-progress` — so we exercise the same proxy path
 *     the real service will use.
 *   - Once the hub is connected we drive the existing photo-upload UI to
 *     enqueue a real photo, then assert at least one progress event arrives
 *     within 30s.
 *
 * Targets (set via `BASE_URL` env, default raw ng-serve for local
 * compile-only loops; full green/red verdict against the docker stack is
 * S6 work):
 *   - `BASE_URL=http://localhost:4300`               (default)
 *   - `BASE_URL=https://localhost:8000/photogallery` (local docker stack)
 *   - `BASE_URL=https://appeid.app/photogallery`     (Trial)
 *
 * Gated by `RUN_SIGNALR_E2E=1` so the existing default e2e matrix doesn't
 * try to run this against environments that aren't standing up the docker
 * proxy or backend yet. S6 flips this on in the docker-stack workflow.
 */
const RUN = process.env['RUN_SIGNALR_E2E'] === '1';

test.describe('SignalR upload progress through proxy (#163)', () => {
  test.skip(!RUN, 'Set RUN_SIGNALR_E2E=1 to enable (needs full docker stack or Trial up).');

  test('Upload triggers at least one SignalR progress event', async ({ page, request }, testInfo) => {
    test.setTimeout(120_000);

    const loginPage = new LoginPage(page);
    const albumPage = new AlbumDetailPage(page);
    const uploadPage = new PhotoUploadPage(page);

    // Login and land on an album.
    await loginPage.loginAsDevAdmin();

    const album = await createAlbumViaApi(request, `E2E SignalR Proxy ${Date.now()}`);
    await albumPage.gotoAlbum(album.id);
    await expect(albumPage.title).toContainText(album.title);

    // ---------------------------------------------------------------------
    // Wire a SignalR HubConnection from inside the page using the same URL
    // shape the production `PhotoProgressService.buildHub()` uses. The
    // browser bundle is shipped in node_modules — we addScriptTag it onto
    // the page so window.signalR is available.
    // ---------------------------------------------------------------------
    const signalrBundle = path.resolve(
      __dirname,
      '../node_modules/@microsoft/signalr/dist/browser/signalr.js'
    );
    await page.addScriptTag({ path: signalrBundle });

    // Mirror the FE service's URL construction. The SPA serves
    // `window.location.origin + <base-href>` as its apiUrl; the spec receives
    // the same prefix via BASE_URL (e.g. https://localhost:8000/photogallery).
    // Strip trailing slash to match `${environment.apiUrl}/hubs/...`.
    const apiUrl = (process.env['BASE_URL'] ?? 'http://localhost:4300').replace(/\/$/, '');
    const devToken = process.env['DEV_BEARER_TOKEN'] ?? 'test-token';

    // Hook into events from inside the page; collect them in a window-level
    // array we can pull out via page.evaluate.
    await page.evaluate(
      async ({ hubUrl, token }) => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const sig = (window as any).signalR as typeof import('@microsoft/signalr');
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (window as any).__signalrEvents = [];

        const hub = new sig.HubConnectionBuilder()
          .withUrl(`${hubUrl}/hubs/photo-progress`, {
            accessTokenFactory: () => token,
            withCredentials: false
          })
          .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
          .configureLogging(sig.LogLevel.Warning)
          .build();

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const record = (kind: string) => (evt: any) => {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          (window as any).__signalrEvents.push({ kind, evt, at: Date.now() });
        };
        hub.on('ProcessingStarted',   record('ProcessingStarted'));
        hub.on('ProcessingProgress',  record('ProcessingProgress'));
        hub.on('ProcessingCompleted', record('ProcessingCompleted'));
        hub.on('WatermarkCompleted',  record('WatermarkCompleted'));

        await hub.start();
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (window as any).__signalrHub = hub;
      },
      { hubUrl: apiUrl, token: devToken }
    );

    // Sanity: connection actually came up. Anything else means the proxy
    // chain (negotiate POST, WebSocket upgrade, or token auth) is broken.
    await expect
      .poll(
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        async () => page.evaluate(() => (window as any).__signalrHub?.state ?? 'Unknown'),
        { timeout: 15_000, message: 'Hub did not reach Connected state through the proxy' }
      )
      .toBe('Connected');

    // ---------------------------------------------------------------------
    // Drive the upload UI — the real path users take. The backend's upload
    // pipeline fires ProcessingStarted within the first ~1s of the file
    // landing, then a stream of ProcessingProgress events.
    // ---------------------------------------------------------------------
    const samplePhotos = getSamplePhotos(1);
    const photoFileName = path.basename(samplePhotos[0]);

    await uploadPage.chooseFiles(samplePhotos);
    await uploadPage.waitForUploadComplete(photoFileName);

    // ---------------------------------------------------------------------
    // The actual story-S4 assertion: at least one event must have been
    // delivered through the hub within 30s of upload completion.
    // ---------------------------------------------------------------------
    await expect
      .poll(
        async () =>
          page.evaluate(
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            () => ((window as any).__signalrEvents as Array<unknown>).length
          ),
        {
          timeout: 30_000,
          message: 'No SignalR progress events received after upload — proxy or hub is broken'
        }
      )
      .toBeGreaterThan(0);

    // Stash the captured events on test failure artifacts to ease triage.
    const events = await page.evaluate(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      () => (window as any).__signalrEvents as unknown[]
    );
    await testInfo.attach('signalr-events.json', {
      body: JSON.stringify(events, null, 2),
      contentType: 'application/json'
    });

    // Belt-and-suspenders: confirm the server-side processing finished too,
    // so a future regression where events fire but the photo never actually
    // processes (e.g. proxy mangling response codes) gets caught here.
    const listResponse = await request.get(
      `${apiUrl}/api/albums/${album.id}/photos`,
      { headers: adminAuthHeaders() }
    );
    expect(listResponse.ok(), `List photos failed: ${listResponse.status()}`).toBeTruthy();

    // Graceful shutdown so retries don't leak hubs.
    await page.evaluate(async () => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const hub = (window as any).__signalrHub;
      if (hub) await hub.stop();
    });
  });
});
