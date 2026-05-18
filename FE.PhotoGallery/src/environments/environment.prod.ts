export const environment = {
  production: true,
  // The SPA is served at /photogallery/ behind the nginx-appeid edge.
  //
  // The <base href="/photogallery/"> is now baked into the emitted
  // index.html at build time via `ng build --base-href=/photogallery/`
  // (see the build:prod npm script). The nginx sub_filter that used to
  // rewrite <base href="/"> -> <base href="/photogallery/"> at the edge
  // is being retired in S5 (nginx-appeid repo); until that PR lands the
  // sub_filter is a harmless no-op against a base href that no longer
  // appears in the bundle.
  //
  // apiUrl is still required because HttpClient does NOT apply <base>
  // to absolute paths. RuntimeConfigService builds `${apiUrl}/api/...`,
  // so setting apiUrl to /photogallery produces /photogallery/api/...,
  // which matches the nginx location that forwards to the backend ACA
  // (API_UPSTREAM).
  apiUrl: '/photogallery'
  // googleClientId is fetched at runtime from GET /api/config/public.
  // See RuntimeConfigService + provideAppInitializer in app.config.ts.
};
