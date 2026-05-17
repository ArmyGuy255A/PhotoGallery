export const environment = {
  production: true,
  // The SPA is served at /photogallery/ behind the nginx-appeid edge.
  // RuntimeConfigService builds `${apiUrl}/api/config/public`, so set
  // apiUrl to /photogallery → /photogallery/api/config/public, which
  // matches the nginx location that strips /photogallery/api/ -> /api/
  // and forwards to the backend ACA (API_UPSTREAM).
  apiUrl: '/photogallery'
  // googleClientId is fetched at runtime from GET /api/config/public.
  // See RuntimeConfigService + provideAppInitializer in app.config.ts.
};
