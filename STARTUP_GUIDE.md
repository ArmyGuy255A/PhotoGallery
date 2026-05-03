# PhotoGallery Development Environment - Startup Guide

## Quick Start

### Windows (PowerShell)
```powershell
.\start-all.ps1
```

### Windows (Command Prompt)
```cmd
start-all.bat
```

### macOS/Linux
```bash
chmod +x start-all.sh
./start-all.sh
```

---

## What the Startup Script Does

The startup script automates launching the complete development stack:

1. **Docker Services** (PostgreSQL 16, MinIO)
   - PostgreSQL for Keycloak and future database needs
   - MinIO for S3-compatible object storage (photos)
   - Automatically cleaned up and restarted

2. **ASP.NET Backend** (Port 5105)
   - Runs with `DISABLE_AUTH=true` for testing
   - Automatically applies database migrations
   - Connects to local MinIO and PostgreSQL

3. **Angular Frontend** (Port 4200)
   - Development server with hot-reload
   - Connects to backend on localhost:5105
   - Opens in default browser automatically

---

## Service URLs

| Service | URL | Credentials |
|---------|-----|-------------|
| Frontend | http://localhost:4200 | N/A (Auth disabled) |
| Backend API | http://localhost:5105 | N/A (Auth disabled) |
| MinIO Console | http://localhost:9000 | minioadmin / minioadmin-password |
| PostgreSQL | localhost:5432 | keycloak / keycloak-password |

---

## Manual Startup (if not using scripts)

### 1. Start Docker Services
```bash
docker-compose up -d postgres minio
```

### 2. Start Backend
```powershell
# Windows PowerShell
$env:DISABLE_AUTH = 'true'
cd PhotoGallery\PhotoGallery
dotnet run
```

### 3. Start Frontend
```bash
# In a new terminal
cd FE.PhotoGallery
ng serve
```

---

## Script Options (PowerShell Only)

### Don't open browser automatically
```powershell
.\start-all.ps1 -NoOpen
```

### Wait indefinitely (useful for CI/CD)
```powershell
.\start-all.ps1 -Wait
```

### Combine options
```powershell
.\start-all.ps1 -NoOpen -Wait
```

---

## Troubleshooting

### "Docker is not installed"
- Install Docker Desktop from https://www.docker.com/products/docker-desktop
- Ensure Docker daemon is running

### "dotnet: command not found"
- Install .NET SDK 9.0+ from https://dotnet.microsoft.com/download
- Verify with `dotnet --version`

### Backend won't start - Port 5105 already in use
```powershell
# Find and kill the process using port 5105
Get-NetTCPConnection -LocalPort 5105 | Select-Object -ExpandProperty OwningProcess | ForEach-Object { Stop-Process -Id $_ -Force }
```

### Frontend won't start - Port 4200 already in use
```powershell
# Find and kill the process using port 4200
Get-NetTCPConnection -LocalPort 4200 | Select-Object -ExpandProperty OwningProcess | ForEach-Object { Stop-Process -Id $_ -Force }
```

### MinIO connection refused
- Check if Docker is running: `docker ps`
- Restart services: `docker-compose restart minio`
- Check MinIO logs: `docker-compose logs minio`

### PostgreSQL connection issues
- Verify PostgreSQL is running: `docker-compose ps postgres`
- Check the password in `appsettings.Development.json`
- Reset: `docker-compose down -v && docker-compose up -d postgres`

---

## Environment Variables

These are automatically set by the startup scripts:

| Variable | Value | Purpose |
|----------|-------|---------|
| DISABLE_AUTH | true | Bypasses authentication for development |
| ASPNETCORE_ENVIRONMENT | Development | Enables detailed errors and auto-migrations |
| Storage__Type | Minio | Use MinIO for file storage |

---

## Stopping Services

### Stop all services
```bash
# Kill the PowerShell/CMD windows running backend and frontend
# Then stop Docker
docker-compose down
```

### Stop only Docker
```bash
docker-compose down
```

### Stop only backend
- Kill the backend terminal window

### Stop only frontend
- Kill the frontend terminal window or press Ctrl+C

---

## Common Development Tasks

### Reset database (clear all data)
```bash
docker-compose down -v
docker-compose up -d postgres
```

### View backend logs
```bash
# If running in separate window, logs stream automatically
# Or check in the backend terminal
```

### View frontend logs
```bash
# Frontend logs appear in its terminal
# Check browser console (F12) for client-side errors
```

### Access MinIO console
1. Open http://localhost:9000
2. Login with:
   - Username: `minioadmin`
   - Password: `minioadmin-password`
3. Create bucket `photogallery` if needed

### Rebuild Angular
```bash
cd FE.PhotoGallery
npm run build
```

### Rebuild backend
```bash
cd PhotoGallery\PhotoGallery
dotnet build
```

---

## Performance Tips

1. **Use SSD for Docker volumes** - MinIO and PostgreSQL store data locally
2. **Close unused applications** - Frees up RAM for services
3. **Disable auto-rebuild if not needed** - Stops unnecessary recompilation
4. **Use Edge/Chrome instead of Firefox** - Slightly faster dev tools

---

## Next Steps

- 📚 Read [Architecture Documentation](./Architecture/README.md)
- 🧪 Run E2E tests: `python verify-persistence.py`
- 📖 Check [Phase 12 Summary](./PHASE_12_SUMMARY.md)
- 🎨 Review [Frontend Components](./FE.PhotoGallery/src/app/components/)

---

## Script Reference

### PowerShell Script (start-all.ps1)
- **Features**: Service health checks, colored output, automatic browser open, graceful error handling
- **Status**: Recommended for Windows developers
- **Syntax**: `.\start-all.ps1 [-NoOpen] [-Wait]`

### Batch Script (start-all.bat)
- **Features**: Compatible with older Windows versions, simple and direct
- **Status**: Alternative for Windows CMD users
- **Syntax**: `start-all.bat`

### Bash Script (start-all.sh - create on Linux/macOS)
- **Features**: Cross-platform compatibility
- **Status**: For macOS and Linux developers

---

## Support

If you encounter issues:

1. Check Docker: `docker ps`
2. Check ports: `netstat -ano | findstr "4200\|5105\|9000"`
3. Check logs: `docker-compose logs`
4. Restart everything: `docker-compose down -v && docker-compose up -d`

---

**Last Updated**: 2026-05-03
**Version**: 1.0
**Status**: Stable
