/**
 * apiUrl is derived from `document.baseURI` at module load — see
 * the analogous comment in environment.ts. With this derivation,
 * the prod bundle works behind any reverse-proxy mount point
 * (root, /photogallery/, /tenant-foo/, ...) without rebuilding.
 *
 * The build-time `--base-href` from `npm run build:prod` bakes the
 * mount path into the SPA's `<base>` tag; nginx in dev / Trial /
 * Prod is also free to rewrite it via `sub_filter` for "nginx-aware"
 * deployments where the FE artifact stays mount-blind.
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
  production: true,
  apiUrl: deriveApiUrl()
  // googleClientId is fetched at runtime from GET /api/config/public.
};
