# CI/CD Pipeline Documentation

This document describes the GitHub Actions workflows for PhotoGallery.

## Workflows

### 1. Build & Test (`build.yml`)

Runs on every push to `main` or `develop`, and on all pull requests.

**Jobs:**
- **build-backend**: Builds .NET project, runs unit tests, publishes release build
- **build-frontend**: Installs Node deps, builds Angular app, runs unit tests
- **lint-backend**: Static code analysis for .NET
- **build-docker**: Builds Docker images (only on main branch)
- **security-scan**: Scans for vulnerable NuGet packages

**Artifacts:**
- Frontend build output (dist/fe.photo-gallery/)

**Status Badge:**
Add to README.md:
```markdown
![Build & Test](https://github.com/YOUR_ORG/PhotoGallery/actions/workflows/build.yml/badge.svg)
```

### 2. E2E Tests (`e2e.yml`)

Runs on pull requests and daily at 2 AM UTC.

**Setup:**
- PostgreSQL service container
- MinIO service container
- Backend starts in Testing environment with DISABLE_AUTH=true
- Frontend starts dev server
- Playwright runs E2E tests

**Artifacts:**
- Playwright HTML report
- Test videos (on failure)

**Browsers Tested:**
- Chrome
- Firefox
- WebKit (Safari)

## Secrets Configuration

Set these in GitHub repository Settings → Secrets:

```
# Production Deployment (future)
AZURE_STORAGE_CONNECTION_STRING=<azure-conn-string>
GOOGLE_CLIENT_ID=<google-client-id>
GOOGLE_CLIENT_SECRET=<google-client-secret>
DOCKER_REGISTRY_USERNAME=<registry-username>
DOCKER_REGISTRY_PASSWORD=<registry-password>
```

## Protected Branch Rules

For `main` branch, configure in GitHub Settings → Branches:

```
Require status checks to pass before merging:
  ✓ build-backend (Build & Test)
  ✓ build-frontend (Build & Test)
  ✓ e2e-tests (E2E Tests)

Require code reviews before merging:
  ✓ At least 1 approval
  ✓ Dismiss stale pull request approvals when new commits pushed

Require branches to be up to date before merging:
  ✓ Enabled
```

## Test Coverage Reports

To add code coverage:

1. Install coverlet for backend:
   ```bash
   dotnet add PhotoGallery.Tests package coverlet.collector
   ```

2. Update `build.yml` to generate coverage:
   ```yaml
   - name: Run unit tests with coverage
     run: dotnet test ./PhotoGallery.Tests/PhotoGallery.Tests.csproj --collect:"XPlat Code Coverage"
   ```

3. Upload to Codecov:
   ```yaml
   - uses: codecov/codecov-action@v3
     with:
       files: ./coverage.xml
   ```

## Deployment Workflow (Future)

When ready for production deployment, add `deploy.yml`:

```yaml
name: Deploy

on:
  push:
    branches: [ main ]
  workflow_run:
    workflows: [ "Build & Test", "E2E Tests" ]
    types: [ completed ]

jobs:
  deploy:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    
    steps:
    - name: Deploy to Azure Container Instances
      # Or AWS ECS, Kubernetes, etc.
```

## Local Testing

To run the same tests locally:

**Backend tests:**
```bash
cd PhotoGallery.Tests
dotnet test
```

**Frontend unit tests:**
```bash
cd FE.PhotoGallery
npm test
```

**E2E tests:**
```bash
cd FE.PhotoGallery
npm run e2e
```

**With UI (interactive):**
```bash
npm run e2e:ui
```

**With headed browsers (see execution):**
```bash
npm run e2e:headed
```

## Troubleshooting

### Tests fail in CI but pass locally

Common causes:
1. Different .NET SDK versions - pin version in `build.yml`
2. Node version mismatch - ensure LTS version used
3. Database state - CI uses fresh database each run
4. Timezone issues - use UTC in tests
5. Port conflicts - CI tests use fixed ports

### Docker build fails

1. Check `.dockerignore` excludes unnecessary files
2. Verify build context in workflow
3. Check volume mounts and working directory

### E2E tests timeout

1. Increase wait times in tests (default 30 seconds)
2. Check service health checks
3. Increase startup wait times in workflow

## Monitoring

### GitHub Actions Dashboard

1. Go to repository → Actions
2. View workflow runs and logs
3. Check artifact downloads

### Status Page

Add status badge to README:

```markdown
## Status
[![Build & Test](https://github.com/YOUR_ORG/PhotoGallery/actions/workflows/build.yml/badge.svg)](https://github.com/YOUR_ORG/PhotoGallery/actions)
[![E2E Tests](https://github.com/YOUR_ORG/PhotoGallery/actions/workflows/e2e.yml/badge.svg)](https://github.com/YOUR_ORG/PhotoGallery/actions)
```

## Performance

Average build times (with caching):
- Backend: 2-3 minutes
- Frontend: 3-5 minutes
- E2E Tests: 5-10 minutes

Total CI/CD time: ~10-15 minutes per PR

## Cost Optimization

GitHub Actions provides:
- 2,000 free minutes/month for public repos
- 3,000 free minutes/month for private repos

To optimize:
1. Use caching (npm, dotnet)
2. Run E2E tests only on PR (not every commit)
3. Use matrix builds only when needed
4. Clean up artifacts after 30 days

## Next Steps

1. Setup branch protection rules
2. Configure secrets
3. Test workflows with sample PR
4. Setup status checks as required
5. Add deployment workflow
6. Configure notifications
