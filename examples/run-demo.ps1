<#
.SYNOPSIS
    Catga OrderSystem Demo Runner

.DESCRIPTION
    One-click script to run OrderSystem in different modes:
    - Single: Single instance mode (default)
    - Cluster: 3 replicas with shared Redis/NATS
    - Aspire: Full Aspire orchestration with monitoring

.PARAMETER Mode
    The mode to run: Single, Cluster, or Aspire

.EXAMPLE
    .\run-demo.ps1 -Mode Single
    .\run-demo.ps1 -Mode Cluster
    .\run-demo.ps1 -Mode Aspire
#>

param(
    [ValidateSet("Single", "Cluster", "Aspire")]
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

function Start-Infrastructure {
    Write-Host "[1/3] Starting infrastructure (Redis + NATS)..." -ForegroundColor Green

    # Check if containers already running
    $redisRunning = docker ps --filter "name=catga-redis" --format "{{.Names}}" 2>$null
    $natsRunning = docker ps --filter "name=catga-nats" --format "{{.Names}}" 2>$null

    if (-not $redisRunning) {
        Write-Host "  Starting Redis..." -ForegroundColor Gray
        docker run -d --name catga-redis -p 6379:6379 redis:alpine 2>$null
        if ($LASTEXITCODE -ne 0) {
            # Container might exist but stopped
            docker start catga-redis 2>$null
        }
    } else {
        Write-Host "  Redis already running" -ForegroundColor Gray
    }

    if (-not $natsRunning) {
        Write-Host "  Starting NATS with JetStream..." -ForegroundColor Gray
        docker run -d --name catga-nats -p 4222:4222 -p 8222:8222 nats:alpine -js 2>$null
        if ($LASTEXITCODE -ne 0) {
            docker start catga-nats 2>$null
        }
    } else {
        Write-Host "  NATS already running" -ForegroundColor Gray
    }

    Start-Sleep -Seconds 2
    Write-Host "  Infrastructure ready!" -ForegroundColor Green
}

function Stop-Infrastructure {
    Write-Host "Stopping infrastructure..." -ForegroundColor Yellow
    docker stop catga-redis catga-nats 2>$null
    docker rm catga-redis catga-nats 2>$null
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

function Run-ClusterMode {
    if (-not (Test-DockerRunning)) {
        Write-Host "ERROR: Docker is not running. Please start Docker first." -ForegroundColor Red
        exit 1
    }

    Start-Infrastructure

    Write-Host "[2/3] Building OrderSystem.Api..." -ForegroundColor Green
    dotnet build "$RootDir\examples\OrderSystem.Api\OrderSystem.Api.csproj" -c Release --verbosity quiet

    Write-Host "[3/3] Starting 3 cluster nodes..." -ForegroundColor Green

    $jobs = @()
    $ports = @(5275, 5276, 5277)

    foreach ($i in 0..2) {
        $port = $ports[$i]
        $nodeId = "node-$($i + 1)"

        $job = Start-Job -ScriptBlock {
            param($RootDir, $Port, $NodeId)
            $env:ASPNETCORE_URLS = "http://localhost:$Port"
            $env:ASPNETCORE_ENVIRONMENT = "Development"
            $env:Catga__NodeId = $NodeId
            $env:Catga__ClusterEnabled = "true"
            $env:ConnectionStrings__redis = "localhost:6379"
            $env:ConnectionStrings__nats = "nats://localhost:4222"
            dotnet run --project "$RootDir\examples\OrderSystem.Api\OrderSystem.Api.csproj" -c Release --no-build 2>&1
        } -ArgumentList $RootDir, $port, $nodeId

        $jobs += $job
        Write-Host "  Started $nodeId on port $port" -ForegroundColor Gray
    }

    Start-Sleep -Seconds 3
    Show-Urls -Mode "Cluster" -Ports $ports

    Write-Host "Cluster running with 3 nodes. Press Enter to stop..." -ForegroundColor Yellow
    Read-Host

    Write-Host "Stopping cluster nodes..." -ForegroundColor Yellow
    $jobs | Stop-Job -PassThru | Remove-Job
    Stop-Infrastructure
}

function Run-AspireMode {
    if (-not (Test-DockerRunning)) {
        Write-Host "ERROR: Docker is not running. Please start Docker first." -ForegroundColor Red
        exit 1
    }

    Write-Host "[2/3] Building Aspire AppHost..." -ForegroundColor Green
    dotnet build "$RootDir\examples\OrderSystem.AppHost\OrderSystem.AppHost.csproj" -c Release --verbosity quiet

    Write-Host "[3/3] Starting Aspire orchestration..." -ForegroundColor Green

    $env:CLUSTER_MODE = "true"

    Write-Host ""
    Write-Host "Aspire will start:" -ForegroundColor Cyan
    Write-Host "  - 3x OrderSystem.Api replicas" -ForegroundColor Gray
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
        "Cluster" { Run-ClusterMode }
        "Aspire" { Run-AspireMode }
    }
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}
