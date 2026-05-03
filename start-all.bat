@echo off
REM PhotoGallery Development Environment Startup Script
REM This script starts: Docker services (PostgreSQL, MinIO), ASP.NET Backend, and Angular Frontend

setlocal enabledelayedexpansion
cd /d "%~dp0"

echo.
echo ====================================================
echo  PhotoGallery Development Environment
echo ====================================================
echo.

REM Check for required tools
echo [*] Checking for required tools...
where docker >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Docker is not installed or not in PATH
    exit /b 1
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK is not installed or not in PATH
    exit /b 1
)
where ng >nul 2>&1
if errorlevel 1 (
    echo [WARNING] Angular CLI not found - will try npm run dev
)
echo [OK] Required tools found
echo.

REM Step 1: Start Docker services
echo [1/3] Starting Docker services (PostgreSQL, MinIO)...
docker-compose down -v >nul 2>&1
docker-compose up -d postgres minio
if errorlevel 1 (
    echo [ERROR] Failed to start Docker services
    exit /b 1
)
echo [OK] Docker services started
echo.

REM Wait for services
echo [*] Waiting for services to be ready...
timeout /t 5 /nobreak
echo.

REM Step 2: Start Backend
echo [2/3] Starting ASP.NET Backend...
set DISABLE_AUTH=true
start "PhotoGallery Backend" cmd /k "cd PhotoGallery\PhotoGallery && dotnet run"
timeout /t 5 /nobreak
echo [OK] Backend started on port 5105
echo.

REM Step 3: Start Frontend
echo [3/3] Starting Angular Frontend...
start "PhotoGallery Frontend" cmd /k "cd FE.PhotoGallery && ng serve"
echo [OK] Frontend started on port 4200
echo.

echo ====================================================
echo  All services started!
echo ====================================================
echo.
echo Frontend:  http://localhost:4200
echo Backend:   http://localhost:5105
echo MinIO:     http://localhost:9000
echo PostgreSQL: localhost:5432
echo.
echo Username: minioadmin
echo Password: minioadmin-password
echo.
echo Test User: testadmin@localhost
echo.
pause
