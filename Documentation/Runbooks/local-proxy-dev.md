# Local docker proxy dev stack — runbook

> Story **S6** of epic "Configurable base path for reverse-proxy deployment"
> (#159). Closes #164.

This runbook brings up the full PhotoGallery dev loop **behind nginx-appeid**
on a single workstation, so we can validate the prod-shaped path
`https://localhost:8000/photogallery/...` end to end before merging the
S1–S5 work to `trial`. This is the "Local docker stack" dev shape #2 from
[`FE.PhotoGallery/README.md` — Three dev shapes][1].

[1]: ../../FE.PhotoGallery/README.md#three-dev-shapes

---

## Prereqs

- **Docker Desktop** running. The nginx-appeid container terminates TLS on
  host port `8000` and proxies to the host loopback via
  `host.docker.internal`.
- **Node 20+** (Angular 19.2 dev server).
- **.NET 9 SDK** (ASP.NET 9 backend).
- **PowerShell 7+** (`pwsh`). This runbook's commands are PowerShell.
- **SQL Server reachable from the backend.** Per repo memory, SQL Server
  runs in Docker for local dev. The S1 backend needs
  `ConnectionStrings:DefaultConnection` resolved either via
  `appsettings.Development.json`, an `appsettings.Development.Local.json`
  overlay (gitignored), or `ConnectionStrings__DefaultConnection` env var.
  Without it `dotnet run` aborts at startup with
  `Connection string 'DefaultConnection' not found.`
- **Authentication secrets.** S1's `Authentication:Jwt:Key` must be
  configured (≥32 bytes for HMAC). For pure smoke testing you can set
  `DISABLE_AUTH=true` and provide a dummy key via env var, but real OAuth
  flows obviously need the Google client id/secret.
- **Repo checkouts at the right branches.** This runbook assumes the
  worktrees set up by the S1–S5 fleet:

  | Worktree | Branch | Used for |
  |---|---|---|
  | `D:\repos\PhotoGallery-s1-basepath` | `u/copilot/feature/backend-basepath` | Backend with `UsePathBase` |
  | `D:\repos\PhotoGallery-s3-basehref` | `u/copilot/feature/fe-base-href` | FE `npm run start:proxy` |
  | `D:\repos\nginx-appeid-s5` | `u/copilot/feature/preserve-prefix-add-hub` | nginx with `/photogallery/api/*` + `/photogallery/hubs/*` locations |

  Once the predecessor PRs merge to `trial`, swap the worktrees for a
  single `trial`-tracking checkout per repo and update the paths below.

### Self-signed certs

nginx-appeid's local stack terminates TLS with a self-signed RSA cert that
matches what Azure Container Apps fronts in prod (CN `localhost`, SANs
`localhost` / `appeid.app` / `local.appeid.app` / `127.0.0.1`). Generate
the pair once per checkout:

```powershell
cd D:\repos\nginx-appeid-s5\local
pwsh -File .\generate-certs.ps1
# Emits local\certs\server.crt and local\certs\server.key (gitignored).
```

The cert is **not** added to the OS / browser trust store. Expect:
- Browsers will warn ("Your connection is not private") — click through.
- `curl` needs `-k` (or `--insecure`) to skip verification.

If you want the warning to disappear permanently in your browser, import
`local\certs\server.crt` into the OS root store yourself; we deliberately
don't do that in script form.

---

## Start sequence

Order matters: nginx will start regardless, but the FE and BE must be
listening when traffic hits or you'll see 502 from nginx. Bring them up
**backend → frontend → nginx**.

### 1. Backend — ASP.NET 9 with `BasePath=/photogallery`

```powershell
cd D:\repos\PhotoGallery-s1-basepath

$env:ASPNETCORE_ENVIRONMENT          = "Development"
$env:ASPNETCORE_URLS                 = "http://0.0.0.0:5105"
$env:ConfigurationSettings__BasePath = "/photogallery"
# Plus whatever your Development overlay doesn't already provide:
#   $env:ConnectionStrings__DefaultConnection = "Server=localhost,1433;..."
#   $env:Authentication__Jwt__Key             = "<≥32-byte secret>"
#   $env:DISABLE_AUTH                         = "true"   # smoke-only

dotnet run --project PhotoGallery\PhotoGallery.csproj
```

What "good" looks like in the log:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://0.0.0.0:5105
```

`0.0.0.0` is required — `localhost`-only binding is unreachable from
`host.docker.internal` inside the nginx container. S1's `Program.cs`
already defaults to `http://0.0.0.0:5105`; the override above is belt-
and-braces.

The `ConfigurationSettings__BasePath=/photogallery` env var feeds S1's
`UsePathBase(...)` middleware, which:
- **strips** the `/photogallery` prefix into `Request.PathBase`, so
  `[Route("api/healthz")]` still matches when the URL is
  `/photogallery/api/healthz`, and
- **rejects** requests that arrived without the prefix (returns 404). So
  hitting `http://localhost:5105/api/healthz` directly should be 404
  while `http://localhost:5105/photogallery/api/healthz` is 200. This is
  the S1 acceptance criterion (#160).

### 2. Frontend — Angular dev server in proxy shape

```powershell
cd D:\repos\PhotoGallery-s3-basehref\FE.PhotoGallery
npm ci                      # first time only
npm run start:proxy         # ng serve --base-href=/photogallery/
```

What "good" looks like:

```
➜  Local:   http://localhost:4300/photogallery/
```

The dev server listens at `http://localhost:4300/` and serves
`index.html` with `<base href="/photogallery/">` rewritten in. nginx
will then proxy `/photogallery/` → `host.docker.internal:4300/` with the
prefix stripped, so the dev server sees plain `/`-rooted URLs.

> ⚠️ **Known gap — see ["Known gaps until follow-ups land"](#known-gaps-until-follow-ups-land).**
> As of S3 the dev server's `proxy.conf.json` only forwards `/api/*` to
> `http://localhost:5105`; it does **not** yet forward
> `/photogallery/api/*` or `/photogallery/hubs/*`. That's filed as
> #167 and means the API + hub curls in step 4 below will fail with
> SPA-fallback HTML (200 with `<title>Photo Gallery</title>`) instead of
> a JSON 200 / 401 until #167 lands.

### 3. nginx-appeid — TLS edge on host `:8000`

```powershell
cd D:\repos\nginx-appeid-s5\local
docker compose up -d --build
```

What "good" looks like:

```
 Container nginx-appeid-local  Started
```

Verify the container is healthy:

```powershell
docker ps --filter name=nginx-appeid-local --format '{{.Status}}'
# -> Up X seconds (healthy)
```

The container's internal HEALTHCHECK hits `http://127.0.0.1:8080/healthz`
(plain HTTP server inside the container). Externally we go through
`https://localhost:8000/...`.

---

## Validation curls

_TODO — added in follow-up commit._
