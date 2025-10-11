#!/usr/bin/env pwsh

# Run Order System in Cluster Mode (3 nodes)

Write-Host "🚀 Starting Order System Cluster (3 nodes)" -ForegroundColor Green
Write-Host ""

# Check if NATS is running
Write-Host "Checking NATS..." -ForegroundColor Yellow
try {
    $null = Test-NetConnection -ComputerName localhost -Port 4222 -InformationLevel Quiet -ErrorAction Stop
    Write-Host "✅ NATS is running on port 4222" -ForegroundColor Green
} catch {
    Write-Host "❌ NATS is not running. Please start NATS with JetStream:" -ForegroundColor Red
    Write-Host "  docker run -d -p 4222:4222 -p 8222:8222 --name nats nats:latest -js" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Or install NATS locally: https://docs.nats.io/running-a-nats-service/introduction/installation" -ForegroundColor Cyan
    exit 1
}

Write-Host ""

# Build the project first
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green
Write-Host ""

# Start Node 1
Write-Host "Starting Node 1 (Port 5001)..." -ForegroundColor Cyan
$node1 = Start-Process pwsh -ArgumentList "-Command", "
    `$env:DeploymentMode='Cluster';
    `$env:NodeId='node-1';
    `$env:ASPNETCORE_URLS='http://localhost:5001';
    `$env:Nats__Url='nats://localhost:4222';
    dotnet run --no-build --configuration Release
" -PassThru -WindowStyle Normal
Write-Host "  PID: $($node1.Id)" -ForegroundColor Gray

Start-Sleep -Seconds 3

# Start Node 2
Write-Host "Starting Node 2 (Port 5002)..." -ForegroundColor Cyan
$node2 = Start-Process pwsh -ArgumentList "-Command", "
    `$env:DeploymentMode='Cluster';
    `$env:NodeId='node-2';
    `$env:ASPNETCORE_URLS='http://localhost:5002';
    `$env:Nats__Url='nats://localhost:4222';
    dotnet run --no-build --configuration Release
" -PassThru -WindowStyle Normal
Write-Host "  PID: $($node2.Id)" -ForegroundColor Gray

Start-Sleep -Seconds 3

# Start Node 3
Write-Host "Starting Node 3 (Port 5003)..." -ForegroundColor Cyan
$node3 = Start-Process pwsh -ArgumentList "-Command", "
    `$env:DeploymentMode='Cluster';
    `$env:NodeId='node-3';
    `$env:ASPNETCORE_URLS='http://localhost:5003';
    `$env:Nats__Url='nats://localhost:4222';
    dotnet run --no-build --configuration Release
" -PassThru -WindowStyle Normal
Write-Host "  PID: $($node3.Id)" -ForegroundColor Gray

Write-Host ""
Write-Host "⏳ Waiting for cluster to initialize..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

# Test cluster health
Write-Host ""
Write-Host "🔍 Checking cluster health..." -ForegroundColor Yellow
$healthChecks = @()
foreach ($port in 5001..5003) {
    try {
        $health = Invoke-RestMethod -Uri "http://localhost:$port/health" -TimeoutSec 5
        $healthChecks += @{
            Port = $port
            NodeId = $health.NodeId
            Status = $health.Status
            Mode = $health.DeploymentMode
        }
        Write-Host "  ✅ Node $($health.NodeId) (Port $port): $($health.Status)" -ForegroundColor Green
    } catch {
        Write-Host "  ❌ Node on Port $port: Not responding" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=" * 70 -ForegroundColor Green
Write-Host "✅ Cluster Started Successfully!" -ForegroundColor Green
Write-Host "=" * 70 -ForegroundColor Green
Write-Host ""
Write-Host "📍 Cluster Information:" -ForegroundColor Cyan
Write-Host "  - Node 1: http://localhost:5001 (Swagger: http://localhost:5001/swagger)" -ForegroundColor Gray
Write-Host "  - Node 2: http://localhost:5002 (Swagger: http://localhost:5002/swagger)" -ForegroundColor Gray
Write-Host "  - Node 3: http://localhost:5003 (Swagger: http://localhost:5003/swagger)" -ForegroundColor Gray
Write-Host "  - NATS: http://localhost:8222 (Monitoring)" -ForegroundColor Gray
Write-Host ""
Write-Host "🧪 Test Commands:" -ForegroundColor Cyan
Write-Host "  # Create order on Node 1" -ForegroundColor Gray
Write-Host "  Invoke-RestMethod -Uri 'http://localhost:5001/api/orders' -Method Post -ContentType 'application/json' -Body '{`"customerName`":`"Test`",`"items`":[{`"productName`":`"Product`",`"quantity`":1,`"price`":10}]}'" -ForegroundColor Gray
Write-Host ""
Write-Host "  # Query from Node 2 (load balanced)" -ForegroundColor Gray
Write-Host "  Invoke-RestMethod -Uri 'http://localhost:5002/api/orders/pending'" -ForegroundColor Gray
Write-Host ""
Write-Host "  # Run full test suite" -ForegroundColor Gray
Write-Host "  .\test-api.ps1" -ForegroundColor Gray
Write-Host ""
Write-Host "⏹️  Press Ctrl+C to stop all nodes" -ForegroundColor Yellow
Write-Host ""

# Wait for user to stop
try {
    while ($true) {
        Start-Sleep -Seconds 1
    }
} finally {
    Write-Host ""
    Write-Host "🛑 Stopping cluster..." -ForegroundColor Yellow
    Stop-Process -Id $node1.Id -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $node2.Id -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $node3.Id -Force -ErrorAction SilentlyContinue
    Write-Host "✅ Cluster stopped" -ForegroundColor Green
}

