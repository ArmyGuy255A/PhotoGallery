/**
 * apiUrl is derived from `document.baseURI` at module load.
 *
 * - Raw mode: `<base href="/">` -> baseURI path is `/` -> apiUrl = `''`.
 *   HttpClient calls like `${apiUrl}/api/...` resolve to `/api/...`.
 *
 * - Proxy mode: nginx sub_filter (or build-time --base-href) sets
 *   `<base href="/photogallery/">` -> baseURI path is `/photogallery/` ->
 *   apiUrl = `/photogallery`. HttpClient calls resolve to
 *   `/photogallery/api/...`, which nginx then handles per its routing.
 *
 * Same bundle works under any sub-path mount without rebuilding.
 */
function deriveApiUrl(): string {
  if (typeof document === 'undefined') {
    return '';
  }
  const path = new URL(document.baseURI).pathname;
  if (!path || path === '/') {
    return '';
  }
  return path.replace(/\/$/, '');
}

export const environment = {
  production: false,
  apiUrl: deriveApiUrl()
  // googleClientId is now fetched at runtime from GET /api/config/public.
  // See RuntimeConfigService and provideAppInitializer in app.config.ts.
};
