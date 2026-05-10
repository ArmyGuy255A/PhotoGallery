#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Starts the PhotoGallery application stack (Backend, Frontend, and Docker services)

.DESCRIPTION
    This script orchestrates startup of:
    - Docker Compose (PostgreSQL and MinIO)
    - ASP.NET Backend (port 5105)
    - Angular Frontend (port 4200)

.EXAMPLE
    .\start-all.ps1

.NOTES
    Requires: Docker, .NET SDK, Node.js
    Environment: Development only (with DISABLE_AUTH=true)
#>

param(
    [switch]$NoOpen,  # Don't open browser automatically
    [switch]$Wait     # Wait for all services before returning
)

$ErrorActionPreference = "Stop"
$projectRoot = (Get-Location).Path
if (-not (Test-Path (Join-Path $projectRoot "docker-compose.yml"))) {
    Write-Host "Error: Please run this script from the project root directory" -ForegroundColor Red
    exit 1
}
$backendPath = Join-Path $projectRoot "PhotoGallery"
$frontendPath = Join-Path $projectRoot "FE.PhotoGallery"

# Colors for output
$colors = @{
    Success = "Green"
    Error   = "Red"
    Warning = "Yellow"
    Info    = "Cyan"
    Wait    = "Magenta"
}

function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    $color = $colors[$Type]
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor $color
}

function Test-Port {
    param([int]$Port, [string]$Service)
    try {
        $connection = Test-NetConnection -ComputerName localhost -Port $Port -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
        return $connection.TcpTestSucceeded
    }
    catch {
        return $false
    }
}

function Wait-ForService {
    param([int]$Port, [string]$Service, [int]$MaxWait = 60)
    
    $elapsed = 0
    Write-Status "Waiting for $Service on port $Port..." Wait
    
    while ($elapsed -lt $MaxWait) {
        if (Test-Port -Port $Port -Service $Service) {
            Write-Status "$Service is ready!" Success
            return $true
        }
        Start-Sleep -Seconds 1
        $elapsed++
        
        if ($elapsed % 5 -eq 0) {
            Write-Host "." -NoNewline
        }
    }
    
    Write-Host ""
    Write-Status "$Service failed to start within $MaxWait seconds" Error
    return $false
}

Write-Status "PhotoGallery Development Environment Startup" Info
Write-Host ""

# Step 1: Start Docker Compose services
Write-Status "Step 1/3: Starting Docker services (PostgreSQL, MinIO)..." Info
try {
    Push-Location $projectRoot
    
    # Start postgres and minio only (not backend/frontend)
    # docker-compose up -d is idempotent - only starts containers that aren't running
    Write-Status "Ensuring PostgreSQL and MinIO are running..." Info
    docker-compose up -d postgres minio
    
    if ($LASTEXITCODE -ne 0) {
        Write-Status "Docker Compose failed" Error
        exit 1
    }
    
    Pop-Location
    Write-Status "Docker services ready" Success
}
catch {
    Write-Status "Error starting Docker services: $_" Error
    exit 1
}

Write-Host ""

# Wait for MinIO
if (-not (Wait-ForService -Port 9000 -Service "MinIO")) {
    Write-Status "MinIO failed to start. Continuing anyway..." Warning
}

# Step 2: Start Backend
Write-Status "Step 2/3: Starting ASP.NET Backend..." Info
try {
    $backendJob = Start-Process -FilePath "pwsh" `
        -ArgumentList @(
            "-NoProfile",
            "-Command",
            "Push-Location '$backendPath'; `$env:DISABLE_AUTH='true'; dotnet run"
        ) `
        -PassThru `
        -NoNewWindow

    Write-Status "Backend process started (PID: $($backendJob.Id))" Info
    
    if (-not (Wait-ForService -Port 5105 -Service "Backend")) {
        Write-Status "Backend failed to start" Error
        exit 1
    }
}
catch {
    Write-Status "Error starting backend: $_" Error
    exit 1
}

Write-Host ""

# Step 3: Start Frontend
Write-Status "Step 3/3: Starting Angular Frontend..." Info
try {
    $frontendJob = Start-Process -FilePath "pwsh" `
        -ArgumentList @(
            "-NoProfile",
            "-Command",
            "Push-Location '$frontendPath'; ng serve --open=false"
        ) `
        -PassThru `
        -NoNewWindow

    Write-Status "Frontend process started (PID: $($frontendJob.Id))" Info
    
    if (-not (Wait-ForService -Port 4200 -Service "Frontend")) {
        Write-Status "Frontend failed to start" Error
        exit 1
    }
}
catch {
    Write-Status "Error starting frontend: $_" Error
    exit 1
}

Write-Host ""
Write-Status "================================================" Success
Write-Status "✅ All services started successfully!" Success
Write-Status "================================================" Success
Write-Host ""

Write-Host "📊 Service Status:" -ForegroundColor Cyan
Write-Host "  Frontend:  http://localhost:4300" -ForegroundColor Green
Write-Host "  Backend:   http://localhost:5105" -ForegroundColor Green
Write-Host "  MinIO:     http://localhost:9000 (Username: minioadmin, Password: minioadmin-password)" -ForegroundColor Green
Write-Host "  PostgreSQL: localhost:5432" -ForegroundColor Green
Write-Host ""

Write-Host "📝 Test User:" -ForegroundColor Cyan
Write-Host "  Email: testadmin@localhost" -ForegroundColor Yellow
Write-Host "  (Login is disabled - using DISABLE_AUTH middleware)" -ForegroundColor Yellow
Write-Host ""

if (-not $NoOpen) {
    Write-Host "Opening browser..." -ForegroundColor Cyan
    Start-Process "http://localhost:4300"
}

Write-Host ""
Write-Status "✋ Press Ctrl+C in the backend/frontend windows to stop services" Warning
Write-Status "To stop Docker services, run: docker-compose down" Warning

if ($Wait) {
    Write-Host ""
    Write-Status "Waiting indefinitely (Ctrl+C to exit)..." Wait
    try {
        while ($true) {
            Start-Sleep -Seconds 30
        }
    }
    catch {
        Write-Status "Shutting down..." Info
    }
}
