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

_TODO — added in follow-up commit._
