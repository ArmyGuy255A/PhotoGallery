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

Run from any shell on the host. The smoke script
[`scripts/smoke-local-proxy.ps1`](../../scripts/smoke-local-proxy.ps1)
automates these checks.

### A. nginx is up

```powershell
curl.exe -sk -i https://localhost:8000/healthz
```

Expected (captured locally 2026-05-17):

```
HTTP/1.1 200 OK
Server: nginx
Content-Type: text/plain
Content-Length: 3

ok
```

### B. Backend reachable through the proxy

```powershell
curl.exe -sk -i https://localhost:8000/photogallery/api/healthz
```

Expected: `HTTP/1.1 200 OK` with the `HealthzController` body.

### C. Public config endpoint (epic's canonical AC)

```powershell
curl.exe -sk https://localhost:8000/photogallery/api/config/public
```

Expected: `200` JSON containing at minimum `googleClientId`. This is the
endpoint the user called out as the canonical failure in #164.

### D. SignalR hub negotiate

```powershell
curl.exe -sk -i -X POST `
  'https://localhost:8000/photogallery/hubs/photo-progress/negotiate?negotiateVersion=1'
```

Expected: `200` with a `connectionId` (when DISABLE_AUTH=true or the
request is authenticated), **or** `401` (auth required). Either is fine
— the smoke is "did the request reach the hub". A `404` here means
S2 + S5 routing is broken or the FE proxy is missing the
`/photogallery/hubs/*` rule (see #167).

### E. SPA loads in a browser

Open `https://localhost:8000/photogallery/` in Chrome / Edge:

1. Accept the self-signed cert warning.
2. SPA shell renders.
3. DevTools → Network: every asset returns 200 with the same origin
   `https://localhost:8000`; no 404s; no `/photogallery/photogallery/`
   double-prefix paths; no CORS errors. The HTML's `<base href>` should
   be `/photogallery/`.

The smoke script **skips** the browser check (it's a script, not a
human). Verify manually after the script reports PASS for A–D.

### F. SignalR end-to-end (full epic AC)

Once authenticated, upload a photo through the SPA. The photo-progress
hub should stream `progress` events that render in the UI. This proves
WebSockets survived three reverse-proxy hops:

```
browser ──wss──> nginx :8000 ──ws──> ng serve :4300 ──ws──> kestrel :5105
                                     (proxy.conf.json forwards /photogallery/hubs/*)
```

This step is out of scope for the smoke script (requires login + a real
photo); it's still on the human runbook checklist.

---

## Teardown

```powershell
# nginx
cd D:\repos\nginx-appeid-s5\local
docker compose down

# Frontend + backend: Ctrl+C in each shell.
# Or kill by PID if they were started detached:
Get-NetTCPConnection -LocalPort 5105,4300 |
  Select-Object -ExpandProperty OwningProcess -Unique |
  ForEach-Object { Stop-Process -Id $_ -Force }
```

Re-running the smoke after teardown should fail check **A** with a
connection-refused (proves nginx is gone).

---

## Troubleshooting

_TODO — added in follow-up commit._
