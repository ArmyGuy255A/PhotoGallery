export const environment = {
  production: true,
  apiUrl: ''  // Use relative path in production (reverse proxy)
  // googleClientId is now fetched at runtime from GET /api/config/public.
  // See RuntimeConfigService and provideAppInitializer in app.config.ts.
};
