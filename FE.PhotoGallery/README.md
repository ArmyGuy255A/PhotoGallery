# FEPhotoGallery

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 19.2.8.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4300/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build              # raw build, base href stays '/' (matches `npm start`)
npm run build:prod    # production build with --base-href=/photogallery/ (used by release.yml)
```

This will compile your project and store the build artifacts in the `dist/` directory. The production build optimizes your application for performance and speed and bakes `<base href="/photogallery/">` into `dist/fe.photo-gallery/browser/index.html` so the SPA can be served behind the nginx-appeid sub-path. See **Three dev shapes** below for when to use each.

## Running unit tests

To execute unit tests with the [Karma](https://karma-runner.github.io) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.


## Three dev shapes

PhotoGallery's FE supports three valid run shapes. Pick the one that
matches what you're actually testing. The base href and `apiUrl` differ
in each.

### 1. Raw dev loop — no proxy

For day-to-day component / service work. No nginx, no sub-path.

```bash
npm start                # ng serve --host 0.0.0.0
```

- URL: `http://localhost:4300/`
- `<base href>`: `/` (the source `src/index.html` value, unmodified)
- `apiUrl`: `''` (from `environment.ts`)
- Backend: `dotnet watch` at `http://localhost:5000` with `BasePath=''`
  (no path prefix).

### 2. Local docker stack — behind nginx-appeid

Mirrors the prod topology. Use when you're touching anything that
involves the proxy (base href, SignalR, CORS, sub-path routing). Bring
up `nginx-appeid/local/docker-compose.yml` first, then:

```bash
npm run start:proxy      # ng serve --base-href=/photogallery/
```

- URL: `https://localhost:8000/photogallery/`
- `<base href>`: `/photogallery/` (rewritten by `ng serve --base-href`)
- `apiUrl`: `''` (still `environment.ts`; nginx routes `/photogallery/api/*`
  to the backend container)
- Backend: `dotnet watch` (or container) with `BasePath=/photogallery`
  so `UsePathBase` strips the prefix.

### 3. Production — SWA behind nginx-appeid

What `release.yml` builds and deploys. Not something you run locally.

```bash
npm run build:prod       # ng build --configuration production --base-href=/photogallery/
```

- URL: `https://appeid.app/photogallery/`
- `<base href>`: `/photogallery/` (baked into the emitted
  `dist/fe.photo-gallery/browser/index.html` at build time)
- `apiUrl`: `/photogallery` (from `environment.prod.ts`; produces
  absolute `/photogallery/api/...` URLs that nginx forwards to the
  backend ACA)
- Backend: container app with `BasePath=/photogallery`.

> **Note:** until the S5 nginx-appeid PR lands, the prod nginx config
> still includes a `sub_filter` that tried to rewrite `<base href="/">`
> to `<base href="/photogallery/">`. With build-time `--base-href` the
> source string no longer appears in the bundle, so the directive is a
> harmless no-op until it is removed.


## Production deployment (Azure Static Web Apps)

The frontend is deployed to an Azure Static Web App via GitHub Actions
(`.github/workflows/release.yml`) on every published release. The
workflow runs `npm ci` + `npm run build:prod` and ships the contents of
`dist/fe.photo-gallery/browser` (staged under `deploy/photogallery/`)
to the SWA via the `Azure/static-web-apps-deploy@v1` action.

### Production runtime topology

- **Frontend host:** Azure Static Web App (Free tier), public hostname
  `https://<random>.azurestaticapps.net` (provisioned by `pg-platform-engineer`).
- **Backend API:** Azure Container App
  `https://ca-photogallery-api-dev.purplesea-ba9de704.eastus2.azurecontainerapps.io`,
  reached via direct cross-origin XHR. Backend CORS allows the SWA host.
- **`apiUrl`** lives in `src/environments/environment.prod.ts` and is
  baked in at build time via Angular `fileReplacements` (see `angular.json`).
- **Runtime config** (Google ClientId etc.) is still fetched at startup
  from `GET {apiUrl}/api/config/public` by `RuntimeConfigService` inside a
  `provideAppInitializer`. No env-file rebuild is required to rotate the
  Google ClientId.
- **SPA fallback + headers** live in `staticwebapp.config.json` (SPA
  rewrites unknown routes to `/index.html`, sets a few security headers).

### Manual one-time setup

1. **GitHub secret** `AZURE_STATIC_WEB_APPS_API_TOKEN` must be set in the
   repository (Settings → Secrets and variables → Actions). Value comes
   from the SWA resource's deployment-token blade.
2. **Google Cloud Console** — add the SWA host to the OAuth 2.0 Client's
   **Authorized JavaScript origins** (e.g. `https://<random>.azurestaticapps.net`).
   Without this, Google Sign-In will fail with `redirect_uri_mismatch` /
   origin errors on the production host.
3. **Backend CORS** — verify the API allows the SWA host as a CORS origin
   (owned by `pg-aspnet-backend-dev` / `pg-platform-engineer`).
