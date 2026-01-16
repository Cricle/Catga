#!/usr/bin/env pwsh
# Run a 3-node Catga cluster locally for testing
# Usage: ./run-cluster.ps1

param(
    [int]$Nodes = 3,
    [int]$BasePort = 5000,
    [string]$Transport = "redis",
    [string]$Persistence = "redis",
    [string]$RedisConn = "localhost:6379"
)

Write-Host "Starting $Nodes-node Catga cluster..." -ForegroundColor Green
Write-Host "Transport: $Transport, Persistence: $Persistence" -ForegroundColor Cyan
Write-Host ""

$jobs = @()

for ($i = 0; $i -lt $Nodes; $i++) {
    $port = $BasePort + $i
    $nodeId = "node$i"
    
    # Build member list (all other nodes)
    $members = @()
    for ($j = 0; $j -lt $Nodes; $j++) {
        if ($j -ne $i) {
            $members += "http://localhost:$($BasePort + $j)"
        }
    }
    
    $localEndpoint = "http://localhost:$port"
    $membersStr = $members -join ","
    
    Write-Host "Starting $nodeId on port $port..." -ForegroundColor Yellow
    Write-Host "  Local: $localEndpoint" -ForegroundColor Gray
    Write-Host "  Members: $membersStr" -ForegroundColor Gray
    
    # Start node in background
    $job = Start-Job -ScriptBlock {
        param($port, $nodeId, $transport, $persistence, $redis, $localEndpoint, $members)
        
        $env:ASPNETCORE_URLS = "http://0.0.0.0:$port"
        $env:Cluster__LocalNodeEndpoint = $localEndpoint
        
        # Set member endpoints as environment variables
        for ($k = 0; $k -lt $members.Count; $k++) {
            $envVar = "Cluster__Members__$k"
            Set-Item -Path "env:$envVar" -Value $members[$k]
        }
        
        dotnet run --project examples/OrderSystem `
            -- `
            --cluster `
            --transport $transport `
            --persistence $persistence `
            --redis $redis `
            --node-id $nodeId `
            --port $port
    } -ArgumentList $port, $nodeId, $Transport, $Persistence, $RedisConn, $localEndpoint, $members
    
    $jobs += $job
    Start-Sleep -Milliseconds 500
}

Write-Host ""
Write-Host "All nodes started! Press Ctrl+C to stop all nodes." -ForegroundColor Green
Write-Host ""
Write-Host "Endpoints:" -ForegroundColor Cyan
for ($i = 0; $i -lt $Nodes; $i++) {
    $port = $BasePort + $i
    Write-Host "  Node $i: http://localhost:$port" -ForegroundColor White
    Write-Host "    Health: http://localhost:$port/health" -ForegroundColor Gray
    Write-Host "    Orders: http://localhost:$port/orders" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Monitoring logs (Ctrl+C to stop)..." -ForegroundColor Yellow
Write-Host ""

try {
    # Monitor job output
    while ($true) {
        foreach ($job in $jobs) {
            $output = Receive-Job -Job $job
            if ($output) {
                Write-Host $output
            }
        }
        Start-Sleep -Milliseconds 100
    }
}
finally {
    Write-Host ""
    Write-Host "Stopping all nodes..." -ForegroundColor Yellow
    $jobs | Stop-Job
    $jobs | Remove-Job
    Write-Host "All nodes stopped." -ForegroundColor Green
}
