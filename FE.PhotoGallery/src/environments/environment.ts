export const environment = {
  production: false,
  // Empty so HttpClient calls are same-origin. The Angular dev server's
  // proxy.conf.json forwards /api/* to the backend on http://localhost:5105;
  // nginx-appeid (https://localhost:8000) forwards /photogallery/ (and /)
  // straight through to the dev server, so /api/* ends up at the same
  // backend no matter which entrypoint you use.
  apiUrl: ''
  // googleClientId is now fetched at runtime from GET /api/config/public.
  // See RuntimeConfigService and provideAppInitializer in app.config.ts.
};
