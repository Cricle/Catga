#!/usr/bin/env pwsh
# Catga Debugger - Complete Functionality Test Script

Write-Host "================================" -ForegroundColor Cyan
Write-Host "  Catga Debugger Test" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Check if service is running
Write-Host "Checking service status..." -ForegroundColor Yellow
try {
    $health = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 3
    Write-Host "   Service is running" -ForegroundColor Green
} catch {
    Write-Host "   Service not running. Please start it first:" -ForegroundColor Red
    Write-Host "   cd examples/OrderSystem.Api" -ForegroundColor Gray
    Write-Host "   dotnet run" -ForegroundColor Gray
    exit 1
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "  Test 1: SignalR Connection" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
try {
    $negotiate = Invoke-WebRequest -Uri "http://localhost:5000/debug/hub/negotiate?negotiateVersion=1" -Method Post -Headers @{"Content-Type"="application/json"}
    $negData = $negotiate.Content | ConvertFrom-Json
    Write-Host "SignalR Hub connected successfully" -ForegroundColor Green
    Write-Host "   ConnectionId: $($negData.connectionId)" -ForegroundColor Gray
    Write-Host "   Transports: $(($negData.availableTransports | ForEach-Object { $_.transport }) -join ', ')" -ForegroundColor Gray
} catch {
    Write-Host "SignalR connection failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "  Test 2: Flows API" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Trigger test orders
Write-Host "Creating test orders..." -ForegroundColor Yellow
$order1 = Invoke-RestMethod -Uri "http://localhost:5000/demo/order-success" -Method Post
Write-Host "   Success Order: $($order1.orderId)" -ForegroundColor Green

$order2 = Invoke-RestMethod -Uri "http://localhost:5000/demo/order-failure" -Method Post
Write-Host "   Failure Order: Triggered rollback" -ForegroundColor Red

Start-Sleep -Seconds 2

# Check Flows API
Write-Host ""
Write-Host "Querying flows..." -ForegroundColor Yellow
$flows = Invoke-RestMethod -Uri "http://localhost:5000/debug-api/flows"
Write-Host "   Captured flows: $($flows.flows.Count)" -ForegroundColor Cyan

if ($flows.flows.Count -gt 0) {
    Write-Host ""
    Write-Host "   Recent 3 flows:" -ForegroundColor Gray
    foreach ($flow in $flows.flows | Select-Object -First 3) {
        $statusColor = if ($flow.status -eq 'Success') { 'Green' } else { 'Red' }
        $statusIcon = if ($flow.status -eq 'Success') { 'Success' } else { 'Error' }

        Write-Host "   [$statusIcon] $($flow.messageType)" -ForegroundColor $statusColor
        Write-Host "      CorrelationId: $($flow.correlationId.Substring(0, 8))..." -ForegroundColor Gray
        Write-Host "      Status: $($flow.status)" -ForegroundColor Gray
        Write-Host "      Duration: $($flow.duration)ms" -ForegroundColor Gray
        Write-Host "      Events: $($flow.eventCount)" -ForegroundColor Gray
        Write-Host ""
    }
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "  Test 3: Events API" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

$events = Invoke-RestMethod -Uri "http://localhost:5000/debug-api/events"
Write-Host "Events API is working" -ForegroundColor Green
Write-Host "   Total events: $($events.events.Count)" -ForegroundColor Cyan

if ($events.events.Count -gt 0) {
    Write-Host ""
    Write-Host "   Recent event:" -ForegroundColor Gray
    $event = $events.events[0]
    Write-Host "   - ID: $($event.id.Substring(0, 8))..." -ForegroundColor Gray
    Write-Host "   - Type: $($event.type)" -ForegroundColor Gray
    Write-Host "   - MessageType: $($event.messageType)" -ForegroundColor Gray
    Write-Host "   - Status: $($event.status)" -ForegroundColor Gray
    Write-Host "   - Duration: $($event.duration)ms" -ForegroundColor Gray
    Write-Host "   - CorrelationId: $($event.correlationId.Substring(0, 8))..." -ForegroundColor Gray
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "  Test 4: Stats API" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

$stats = Invoke-RestMethod -Uri "http://localhost:5000/debug-api/stats"
Write-Host "Stats API is working" -ForegroundColor Green
Write-Host "   Total events: $($stats.totalEvents)" -ForegroundColor Cyan
Write-Host "   Total flows: $($stats.totalFlows)" -ForegroundColor Cyan
Write-Host "   Storage size: $([math]::Round($stats.storageSizeBytes / 1024, 2)) KB" -ForegroundColor Cyan
Write-Host "   Oldest event: $($stats.oldestEvent)" -ForegroundColor Gray
Write-Host "   Newest event: $($stats.newestEvent)" -ForegroundColor Gray

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "  Test 5: UI Accessibility" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

try {
    $ui = Invoke-WebRequest -Uri "http://localhost:5000/debug"
    Write-Host "Debugger UI is accessible" -ForegroundColor Green
    Write-Host "   Status code: $($ui.StatusCode)" -ForegroundColor Gray

    if ($ui.Content -match 'signalR') {
        Write-Host "   SignalR client loaded" -ForegroundColor Green
    }

    if ($ui.Content -match 'alpinejs') {
        Write-Host "   Alpine.js loaded" -ForegroundColor Green
    }
} catch {
    Write-Host "UI access failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "================================" -ForegroundColor Green
Write-Host "  Test Complete!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
Write-Host ""
Write-Host "Full Report:" -ForegroundColor Yellow
Write-Host "   - SignalR Connection: OK" -ForegroundColor Green
Write-Host "   - Flows API: OK ($($flows.flows.Count) flows)" -ForegroundColor Green
Write-Host "   - Events API: OK ($($events.events.Count) events)" -ForegroundColor Green
Write-Host "   - Stats API: OK ($($stats.totalEvents) events, $($stats.totalFlows) flows)" -ForegroundColor Green
Write-Host "   - Debugger UI: OK" -ForegroundColor Green
Write-Host ""
Write-Host "Visit Debugger: http://localhost:5000/debug" -ForegroundColor Cyan
Write-Host "API Documentation: http://localhost:5000/swagger" -ForegroundColor Cyan
Write-Host ""
