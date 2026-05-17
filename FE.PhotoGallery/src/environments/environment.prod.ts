export const environment = {
  production: true,
  // Same-origin from https://appeid.app: the nginx-appeid edge proxy
  // forwards /api/* to the PhotoGallery API ACA (see API_UPSTREAM in
  // nginx-appeid Services/appeid/server-appeid.conf). That keeps the
  // SPA out of CORS-preflight land and removes the backend's need to
  // allow-list per frontend host. Matches the local dev shape
  // (Vite proxy.conf.json /api -> http://localhost:5105).
  apiUrl: ''
  // googleClientId is fetched at runtime from GET /api/config/public.
  // See RuntimeConfigService + provideAppInitializer in app.config.ts.
};
