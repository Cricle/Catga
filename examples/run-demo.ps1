<#
.SYNOPSIS
    Catga OrderSystem Demo Runner

.DESCRIPTION
    One-click script to run OrderSystem in different modes:
    - Single: Single instance mode, in-memory (no Docker required)
    - Aspire: Full Aspire orchestration with Redis, NATS, Jaeger (requires Docker)
    - Cluster: Aspire with 3 replicas for distributed demo (requires Docker)

.PARAMETER Mode
    The mode to run: Single, Aspire, or Cluster

.EXAMPLE
    .\run-demo.ps1 -Mode Single   # No Docker, simplest
    .\run-demo.ps1 -Mode Aspire   # Full stack, 1 replica
    .\run-demo.ps1 -Mode Cluster  # Full stack, 3 replicas
#>

param(
    [ValidateSet("Single", "Aspire", "Cluster")]
    [string]$Mode = "Single"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Catga OrderSystem Demo" -ForegroundColor Cyan
Write-Host "   Mode: $Mode" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Test-DockerRunning {
    try {
        $null = docker info 2>&1
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

function Show-Urls {
    param([string]$Mode, [int[]]$Ports)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "   Available URLs" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    foreach ($port in $Ports) {
        Write-Host "  Web UI:      http://localhost:$port" -ForegroundColor White
        Write-Host "  Swagger:     http://localhost:$port/swagger" -ForegroundColor Gray
        Write-Host "  Health:      http://localhost:$port/health" -ForegroundColor Gray
        Write-Host "  Cluster:     http://localhost:$port/api/cluster/status" -ForegroundColor Gray
        Write-Host ""
    }

    if ($Mode -eq "Aspire") {
        Write-Host "  Aspire:      http://localhost:15888" -ForegroundColor Magenta
        Write-Host "  Jaeger:      http://localhost:16686" -ForegroundColor Magenta
        Write-Host "  Redis Cmd:   http://localhost:8081" -ForegroundColor Magenta
    }

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Run-SingleMode {
    Write-Host "[2/3] Building OrderSystem.Api..." -ForegroundColor Green
    dotnet build "$RootDir\examples\OrderSystem.Api\OrderSystem.Api.csproj" -c Release --verbosity quiet

    Write-Host "[3/3] Starting single instance..." -ForegroundColor Green
    Show-Urls -Mode "Single" -Ports @(5275)

    Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow
    Write-Host ""

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    dotnet run --project "$RootDir\examples\OrderSystem.Api\OrderSystem.Api.csproj" -c Release --no-build
}

function Run-AspireMode {
    param([bool]$ClusterEnabled = $false)

    if (-not (Test-DockerRunning)) {
        Write-Host "ERROR: Docker is not running. Please start Docker first." -ForegroundColor Red
        exit 1
    }

    Write-Host "[2/3] Building Aspire AppHost..." -ForegroundColor Green
    dotnet build "$RootDir\examples\OrderSystem.AppHost\OrderSystem.AppHost.csproj" -c Release --verbosity quiet

    $replicaCount = if ($ClusterEnabled) { 3 } else { 1 }
    Write-Host "[3/3] Starting Aspire orchestration ($replicaCount replica(s))..." -ForegroundColor Green

    $env:CLUSTER_MODE = $ClusterEnabled.ToString().ToLower()

    Write-Host ""
    Write-Host "Aspire will start:" -ForegroundColor Cyan
    Write-Host "  - ${replicaCount}x OrderSystem.Api replica(s)" -ForegroundColor Gray
    Write-Host "  - Redis with Commander" -ForegroundColor Gray
    Write-Host "  - NATS with JetStream" -ForegroundColor Gray
    Write-Host "  - Jaeger for distributed tracing" -ForegroundColor Gray
    Write-Host ""

    Show-Urls -Mode "Aspire" -Ports @(5275)

    Write-Host "Press Ctrl+C to stop Aspire" -ForegroundColor Yellow
    Write-Host ""

    dotnet run --project "$RootDir\examples\OrderSystem.AppHost\OrderSystem.AppHost.csproj" -c Release --no-build
}

# Main
try {
    switch ($Mode) {
        "Single" { Run-SingleMode }
        "Aspire" { Run-AspireMode -ClusterEnabled $false }
        "Cluster" { Run-AspireMode -ClusterEnabled $true }
    }
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}
