export const environment = {
  production: true,
  // Cross-origin call from Azure Static Web App -> Azure Container App API.
  // Backend CORS is configured to allow the SWA host. We deliberately do
  // NOT use SWA's /api/* reverse-proxy block to keep auth-cookie handling
  // simple (JWT in Authorization header, attached by jwtInterceptor).
  apiUrl: 'https://ca-photogallery-api-dev.purplesea-ba9de704.eastus2.azurecontainerapps.io'
  // googleClientId is fetched at runtime from GET /api/config/public.
  // See RuntimeConfigService + provideAppInitializer in app.config.ts.
};
