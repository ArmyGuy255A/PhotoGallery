# Development Startup Scripts - Summary

## Files Created

### 1. `start-all.ps1` (PowerShell - Recommended for Windows)
**Path**: `D:\repos\PhotoGallery\PhotoGallery\start-all.ps1`

**Features:**
- ✅ Service health checks
- ✅ Colored status output
- ✅ Auto-opens browser
- ✅ Graceful error handling
- ✅ Port conflict detection
- ✅ Automatic cleanup of old containers

**Usage:**
```powershell
# Default: starts all services and opens browser
.\start-all.ps1

# Don't open browser
.\start-all.ps1 -NoOpen

# Wait indefinitely (good for CI/CD)
.\start-all.ps1 -Wait

# Combine options
.\start-all.ps1 -NoOpen -Wait
```

**Output:**
```
[18:48:02] PhotoGallery Development Environment Startup
[18:48:02] Step 1/3: Starting Docker services (PostgreSQL, MinIO)...
[18:48:04] Docker services started
[18:48:04] Step 2/3: Starting ASP.NET Backend...
[18:48:14] Step 3/3: Starting Angular Frontend...
================================================
✅ All services started successfully!
================================================

📊 Service Status:
  Frontend:  http://localhost:4200
  Backend:   http://localhost:5105
  MinIO:     http://localhost:9000 (Username: minioadmin, Password: minioadmin-password)
  PostgreSQL: localhost:5432
```

---

### 2. `start-all.bat` (Command Prompt - Windows Alternative)
**Path**: `D:\repos\PhotoGallery\PhotoGallery\start-all.bat`

**Features:**
- ✅ Compatible with older Windows versions
- ✅ Simple and direct
- ✅ No dependencies on PowerShell
- ⚠️ Less advanced error handling

**Usage:**
```cmd
start-all.bat
```

---

### 3. `STARTUP_GUIDE.md` (Comprehensive Documentation)
**Path**: `D:\repos\PhotoGallery\PhotoGallery\STARTUP_GUIDE.md`

**Includes:**
- Quick start commands
- Manual startup instructions
- Service URLs and credentials
- Troubleshooting guide
- Environment variables reference
- Common development tasks
- Performance tips

---

## What Each Script Does

### Step 1: Docker Services
```bash
docker-compose down -v                    # Clean up old containers
docker-compose up -d postgres minio       # Start PostgreSQL and MinIO
```

**Services:**
- PostgreSQL 16 on port 5432
- MinIO on ports 9000-9001
- Automatic health checks

### Step 2: ASP.NET Backend
```powershell
$env:DISABLE_AUTH='true'
cd PhotoGallery\PhotoGallery
dotnet run
```

**Features:**
- Runs on port 5105
- Auth disabled for testing (DISABLE_AUTH=true)
- Auto-applies migrations
- Connects to local MinIO and PostgreSQL
- Seeded with test data

### Step 3: Angular Frontend
```bash
cd FE.PhotoGallery
ng serve
```

**Features:**
- Dev server on port 4200
- Hot reload enabled
- Connects to backend on localhost:5105
- Opens in default browser

---

## Service URLs After Startup

| Service | URL | Purpose |
|---------|-----|---------|
| Frontend | http://localhost:4200 | Web UI for photo gallery |
| Backend | http://localhost:5105 | API endpoints |
| MinIO Console | http://localhost:9000 | S3-compatible storage |
| PostgreSQL | localhost:5432 | Database |

---

## Credentials

| Service | Username | Password |
|---------|----------|----------|
| MinIO | minioadmin | minioadmin-password |
| PostgreSQL | keycloak | keycloak-password |
| Test User | testadmin@localhost | (Auth disabled) |

---

## Troubleshooting

### Port Already in Use
```powershell
# Find process using port 5105
Get-NetTCPConnection -LocalPort 5105 | Select-Object OwningProcess

# Kill it
Stop-Process -Id <PID> -Force
```

### Docker Services Won't Start
```bash
# Verify Docker is running
docker ps

# Check Docker logs
docker-compose logs

# Reset everything
docker-compose down -v
docker-compose up -d
```

### Backend Crashes Immediately
- Check `appsettings.Development.json` for correct MinIO credentials
- Verify PostgreSQL is running
- Check logs in backend terminal

### Frontend Won't Connect to Backend
- Backend must be on port 5105
- Check CORS settings in Program.cs
- Verify backend is running: `curl http://localhost:5105/api/albums`

---

## Environment Variables

These are automatically set by the startup scripts:

| Variable | Value | Purpose |
|----------|-------|---------|
| DISABLE_AUTH | true | Development mode - skip authentication |
| ASPNETCORE_ENVIRONMENT | Development | Enable detailed errors, auto-migrations |
| Storage__Type | Minio | Use MinIO for file storage |
| Storage__Minio__Endpoint | localhost:9000 | MinIO connection |
| Storage__Minio__AccessKey | minioadmin | MinIO username |
| Storage__Minio__SecretKey | minioadmin-password | MinIO password |
| Storage__Minio__BucketName | photogallery | MinIO bucket |

---

## Performance Optimization

For faster startup times:
1. Keep Docker daemon running in background
2. Use SSD for Docker volumes
3. Disable browser auto-open if not needed: `.\start-all.ps1 -NoOpen`
4. Close unused applications to free RAM

---

## Next Steps

After startup:
1. Navigate to http://localhost:4200
2. Explore the dashboard
3. Try creating an album
4. Test photo upload functionality
5. Review [Phase 12 Summary](./PHASE_12_SUMMARY.md)

---

## Integration with Development Tools

### VS Code Debugging
The scripts can be integrated with VS Code launch configurations for integrated debugging.

### GitHub Actions CI/CD
For CI/CD, use: `.\start-all.ps1 -NoOpen -Wait`

### Docker Desktop Integration
The scripts work with Docker Desktop and automatically manage container lifecycle.

---

**Last Updated**: 2026-05-03
**Version**: 1.0
**Status**: Production Ready
