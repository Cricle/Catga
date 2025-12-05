<#
.SYNOPSIS
    Catga OrderSystem Cross-Mode Stress Test

.DESCRIPTION
    Runs stress tests across different deployment modes:
    - Single: Single instance, in-memory storage
    - Cluster: 3 replicas with Redis storage (via Aspire)

    Tests infrastructure combinations:
    - Redis: Distributed cache performance
    - NATS: Message queue performance
    - Combined: Full distributed stack

.EXAMPLE
    .\cross-test.ps1
    .\cross-test.ps1 -SkipSingle
    .\cross-test.ps1 -SkipCluster
#>

param(
    [switch]$SkipSingle,
    [switch]$SkipCluster,
    [int]$SequentialCount = 500,
    [int]$ParallelCount = 100,
    [int]$OrderCount = 50
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $PSScriptRoot
$ExamplesDir = $PSScriptRoot

# Results storage
$AllResults = @()

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  $($Title.PadRight(62))║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
}

function Write-SubHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "┌─ $Title ─" -ForegroundColor Yellow
}

function Test-Endpoint {
    param([string]$Url, [string]$Method = "GET", [object]$Body = $null, [int]$TimeoutSec = 5)
    try {
        $params = @{
            Uri = $Url
            Method = $Method
            ContentType = "application/json"
            TimeoutSec = $TimeoutSec
            ErrorAction = "Stop"
        }
        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
        }
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    } catch {
        return @{ Success = $false; Error = $_.Exception.Message }
    }
}

function Wait-ForService {
    param([string]$Url, [int]$MaxWaitSeconds = 120)

    $elapsed = 0
    while ($elapsed -lt $MaxWaitSeconds) {
        $result = Test-Endpoint "$Url/health" -TimeoutSec 3
        if ($result.Success) {
            return $true
        }
        Start-Sleep -Seconds 2
        $elapsed += 2
        Write-Host "." -NoNewline -ForegroundColor Gray
    }
    Write-Host ""
    return $false
}

function Run-FullStressTest {
    param(
        [string]$Mode,
        [string]$Endpoint,
        [string]$Infrastructure
    )

    $results = @{
        Mode = $Mode
        Infrastructure = $Infrastructure
        Timestamp = Get-Date -Format "HH:mm:ss"

        # Health check
        HealthOk = $false

        # Sequential API test
        SeqCount = $SequentialCount
        SeqSuccess = 0
        SeqRps = 0
        SeqAvgMs = 0
        SeqMinMs = 0
        SeqMaxMs = 0

        # Parallel API test
        ParCount = $ParallelCount
        ParSuccess = 0
        ParRps = 0
        ParAvgMs = 0

        # Order creation test
        OrdCount = $OrderCount
        OrdSuccess = 0
        OrdRps = 0

        # Redis test (if available)
        RedisOk = $false
        RedisLatencyMs = 0

        # NATS test (if available)
        NatsOk = $false
    }

    # Health check
    $healthResult = Test-Endpoint "$Endpoint/health"
    $results.HealthOk = $healthResult.Success
    if (-not $healthResult.Success) {
        Write-Host "  ✗ Health check failed" -ForegroundColor Red
        return $results
    }
    Write-Host "  ✓ Health check passed" -ForegroundColor Green

    # Sequential stress test (direct calls, no Job overhead)
    Write-Host "  Running sequential test ($SequentialCount requests)..." -ForegroundColor Gray
    $seqTimes = [System.Collections.Generic.List[double]]::new()
    $seqStart = Get-Date
    for ($i = 1; $i -le $SequentialCount; $i++) {
        $reqStart = Get-Date
        try {
            $null = Invoke-RestMethod -Uri "$Endpoint/api/cluster/node" -Method GET -TimeoutSec 5
            $results.SeqSuccess++
            $seqTimes.Add(((Get-Date) - $reqStart).TotalMilliseconds)
        } catch { }
    }
    $seqDuration = ((Get-Date) - $seqStart).TotalMilliseconds
    $results.SeqRps = [math]::Round($SequentialCount / ($seqDuration / 1000), 1)
    if ($seqTimes.Count -gt 0) {
        $stats = $seqTimes | Measure-Object -Average -Minimum -Maximum
        $results.SeqAvgMs = [math]::Round($stats.Average, 2)
        $results.SeqMinMs = [math]::Round($stats.Minimum, 2)
        $results.SeqMaxMs = [math]::Round($stats.Maximum, 2)
    }
    Write-Host "  ✓ Sequential: $($results.SeqRps) req/s, avg $($results.SeqAvgMs)ms" -ForegroundColor Green

    # Parallel stress test using runspaces (much faster than Start-Job)
    Write-Host "  Running parallel test ($ParallelCount concurrent)..." -ForegroundColor Gray
    $runspacePool = [runspacefactory]::CreateRunspacePool(1, $ParallelCount)
    $runspacePool.Open()
    $runspaces = [System.Collections.Generic.List[object]]::new()

    $scriptBlock = {
        param($Url)
        $start = [datetime]::Now
        try {
            $null = Invoke-RestMethod -Uri $Url -Method GET -TimeoutSec 5
            return @{ Success = $true; Duration = ([datetime]::Now - $start).TotalMilliseconds }
        } catch {
            return @{ Success = $false; Duration = 0 }
        }
    }

    $parallelStart = Get-Date
    for ($i = 1; $i -le $ParallelCount; $i++) {
        $ps = [powershell]::Create().AddScript($scriptBlock).AddArgument("$Endpoint/api/cluster/node")
        $ps.RunspacePool = $runspacePool
        $runspaces.Add(@{ Pipe = $ps; Handle = $ps.BeginInvoke() })
    }

    $parallelResults = @()
    foreach ($rs in $runspaces) {
        $parallelResults += $rs.Pipe.EndInvoke($rs.Handle)
        $rs.Pipe.Dispose()
    }
    $parallelDuration = ((Get-Date) - $parallelStart).TotalMilliseconds
    $runspacePool.Close()

    $results.ParSuccess = ($parallelResults | Where-Object { $_.Success }).Count
    $results.ParRps = [math]::Round($ParallelCount / ($parallelDuration / 1000), 1)
    $successDurations = @($parallelResults | Where-Object { $_.Success } | ForEach-Object { $_.Duration })
    if ($successDurations.Count -gt 0) {
        $results.ParAvgMs = [math]::Round(($successDurations | Measure-Object -Average).Average, 2)
    }
    Write-Host "  ✓ Parallel: $($results.ParRps) req/s, avg $($results.ParAvgMs)ms" -ForegroundColor Green

    # Order creation stress test using runspaces
    Write-Host "  Running order creation test ($OrderCount concurrent)..." -ForegroundColor Gray
    $orderPool = [runspacefactory]::CreateRunspacePool(1, 50)
    $orderPool.Open()
    $orderRunspaces = [System.Collections.Generic.List[object]]::new()

    $orderScript = {
        param($Url, $Index)
        $payload = @{
            customerId = "CROSS-$Index-$([guid]::NewGuid().ToString('N').Substring(0,8))"
            items = @(@{
                productId = "CROSS-PROD"
                productName = "Cross Test Product"
                quantity = 1
                unitPrice = 10.00
            })
            shippingAddress = "Cross Test Address"
            paymentMethod = "card"
        } | ConvertTo-Json -Depth 5
        try {
            $response = Invoke-RestMethod -Uri $Url -Method POST -ContentType "application/json" -Body $payload -TimeoutSec 10
            return @{ Success = ($null -ne $response.orderId); OrderId = $response.orderId }
        } catch {
            return @{ Success = $false }
        }
    }

    $orderStart = Get-Date
    for ($i = 1; $i -le $OrderCount; $i++) {
        $ps = [powershell]::Create().AddScript($orderScript).AddArgument("$Endpoint/api/orders").AddArgument($i)
        $ps.RunspacePool = $orderPool
        $orderRunspaces.Add(@{ Pipe = $ps; Handle = $ps.BeginInvoke() })
    }

    $orderResults = @()
    foreach ($rs in $orderRunspaces) {
        $orderResults += $rs.Pipe.EndInvoke($rs.Handle)
        $rs.Pipe.Dispose()
    }
    $orderDuration = ((Get-Date) - $orderStart).TotalMilliseconds
    $orderPool.Close()

    $results.OrdSuccess = ($orderResults | Where-Object { $_.Success }).Count
    $results.OrdRps = [math]::Round($OrderCount / ($orderDuration / 1000), 1)
    Write-Host "  ✓ Orders: $($results.OrdSuccess)/$OrderCount created, $($results.OrdRps) req/s" -ForegroundColor Green

    # Test Redis connectivity (via order read - uses Redis in cluster mode)
    if ($Infrastructure -match "Redis") {
        $orderIds = ($orderResults | Where-Object { $_.Success -and $_.OrderId }).OrderId
        if ($orderIds.Count -gt 0) {
            $redisStart = Get-Date
            $readResult = Test-Endpoint "$Endpoint/api/orders/$($orderIds[0])"
            $redisLatency = ((Get-Date) - $redisStart).TotalMilliseconds
            $results.RedisOk = $readResult.Success -and $readResult.Data
            $results.RedisLatencyMs = [math]::Round($redisLatency, 2)
            if ($results.RedisOk) {
                Write-Host "  ✓ Redis: Order read OK, $($results.RedisLatencyMs)ms" -ForegroundColor Green
            } else {
                Write-Host "  ✗ Redis: Order read failed" -ForegroundColor Red
            }
        }
    }

    # NATS is used internally for messaging - check cluster status
    if ($Infrastructure -match "NATS") {
        $clusterResult = Test-Endpoint "$Endpoint/api/cluster/status"
        $results.NatsOk = $clusterResult.Success
        if ($results.NatsOk) {
            Write-Host "  ✓ NATS: Cluster communication OK" -ForegroundColor Green
        }
    }

    return $results
}

function Stop-AllDotnet {
    taskkill /F /IM dotnet.exe 2>$null | Out-Null
    Start-Sleep -Seconds 2
}

function Write-ResultsTable {
    param([array]$Results)

    Write-Header "Cross-Mode Stress Test Results"
    Write-Host ""

    # ===== Table 1: Performance Summary =====
    Write-Host "┌─────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
    Write-Host "│                           PERFORMANCE SUMMARY                                       │" -ForegroundColor Cyan
    Write-Host "└─────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

    $header = "{0,-14} | {1,-12} | {2,10} | {3,10} | {4,10} | {5,10} | {6,8}" -f "Mode", "Infra", "Seq RPS", "Par RPS", "Ord RPS", "Avg(ms)", "Success"
    $separator = "─" * 90

    Write-Host $header -ForegroundColor Yellow
    Write-Host $separator -ForegroundColor Gray

    foreach ($r in $Results) {
        $successRate = if ($r.SeqCount -gt 0) {
            [math]::Round((($r.SeqSuccess + $r.ParSuccess + $r.OrdSuccess) / ($r.SeqCount + $r.ParCount + $r.OrdCount)) * 100, 0)
        } else { 0 }

        $row = "{0,-14} | {1,-12} | {2,10} | {3,10} | {4,10} | {5,10} | {6,7}%" -f `
            $r.Mode, `
            $r.Infrastructure, `
            $r.SeqRps, `
            $r.ParRps, `
            $r.OrdRps, `
            $r.SeqAvgMs, `
            $successRate

        $color = if ($successRate -eq 100) { "Green" } elseif ($successRate -ge 80) { "Yellow" } else { "Red" }
        Write-Host $row -ForegroundColor $color
    }

    Write-Host $separator -ForegroundColor Gray
    Write-Host ""

    # ===== Table 2: Latency Details =====
    Write-Host "┌─────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
    Write-Host "│                           LATENCY DISTRIBUTION                                      │" -ForegroundColor Cyan
    Write-Host "└─────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

    $latHeader = "{0,-14} | {1,-12} | {2,12} | {3,12} | {4,12} | {5,12}" -f "Mode", "Infra", "Min(ms)", "Avg(ms)", "Max(ms)", "Jitter(ms)"
    Write-Host $latHeader -ForegroundColor Yellow
    Write-Host $separator -ForegroundColor Gray

    foreach ($r in $Results) {
        $jitter = [math]::Round($r.SeqMaxMs - $r.SeqMinMs, 2)
        $row = "{0,-14} | {1,-12} | {2,12} | {3,12} | {4,12} | {5,12}" -f `
            $r.Mode, `
            $r.Infrastructure, `
            $r.SeqMinMs, `
            $r.SeqAvgMs, `
            $r.SeqMaxMs, `
            $jitter
        Write-Host $row
    }

    Write-Host $separator -ForegroundColor Gray
    Write-Host ""

    # ===== Table 3: Throughput Comparison =====
    Write-Host "┌─────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
    Write-Host "│                           THROUGHPUT COMPARISON                                     │" -ForegroundColor Cyan
    Write-Host "└─────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

    $tpHeader = "{0,-14} | {1,15} | {2,15} | {3,15} | {4,15}" -f "Mode", "Seq Total", "Par Total", "Ord Total", "Total Req"
    Write-Host $tpHeader -ForegroundColor Yellow
    Write-Host $separator -ForegroundColor Gray

    foreach ($r in $Results) {
        $totalReq = $r.SeqSuccess + $r.ParSuccess + $r.OrdSuccess
        $row = "{0,-14} | {1,15} | {2,15} | {3,15} | {4,15}" -f `
            $r.Mode, `
            "$($r.SeqSuccess)/$($r.SeqCount)", `
            "$($r.ParSuccess)/$($r.ParCount)", `
            "$($r.OrdSuccess)/$($r.OrdCount)", `
            $totalReq
        Write-Host $row
    }

    Write-Host $separator -ForegroundColor Gray
    Write-Host ""

    # ===== Table 4: Infrastructure Status =====
    Write-Host "┌─────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
    Write-Host "│                           INFRASTRUCTURE STATUS                                     │" -ForegroundColor Cyan
    Write-Host "└─────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

    $infraHeader = "{0,-14} | {1,-12} | {2,12} | {3,15} | {4,12}" -f "Mode", "Infra", "Health", "Redis", "NATS"
    Write-Host $infraHeader -ForegroundColor Yellow
    Write-Host $separator -ForegroundColor Gray

    foreach ($r in $Results) {
        $healthIcon = if ($r.HealthOk) { "✓ OK" } else { "✗ FAIL" }
        $redisIcon = if ($r.RedisOk) { "✓ $($r.RedisLatencyMs)ms" } elseif ($r.Infrastructure -match "Redis") { "✗ FAIL" } else { "N/A" }
        $natsIcon = if ($r.NatsOk) { "✓ OK" } elseif ($r.Infrastructure -match "NATS") { "✗ FAIL" } else { "N/A" }

        $row = "{0,-14} | {1,-12} | {2,12} | {3,15} | {4,12}" -f `
            $r.Mode, `
            $r.Infrastructure, `
            $healthIcon, `
            $redisIcon, `
            $natsIcon

        $color = if ($r.HealthOk) { "Green" } else { "Red" }
        Write-Host $row -ForegroundColor $color
    }

    Write-Host $separator -ForegroundColor Gray
    Write-Host ""

    # ===== Table 5: Cross-Mode Comparison =====
    if ($Results.Count -gt 1) {
        Write-Host "┌─────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
        Write-Host "│                           CROSS-MODE COMPARISON                                     │" -ForegroundColor Cyan
        Write-Host "└─────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

        $baseline = $Results[0]
        $compHeader = "{0,-14} | {1,12} | {2,12} | {3,12} | {4,12}" -f "Mode", "vs Baseline", "Seq Δ%", "Par Δ%", "Ord Δ%"
        Write-Host $compHeader -ForegroundColor Yellow
        Write-Host $separator -ForegroundColor Gray

        foreach ($r in $Results) {
            if ($r.Mode -eq $baseline.Mode) {
                $row = "{0,-14} | {1,12} | {2,12} | {3,12} | {4,12}" -f $r.Mode, "BASELINE", "0%", "0%", "0%"
                Write-Host $row -ForegroundColor Cyan
            } else {
                $seqDelta = if ($baseline.SeqRps -gt 0) { [math]::Round((($r.SeqRps - $baseline.SeqRps) / $baseline.SeqRps) * 100, 1) } else { 0 }
                $parDelta = if ($baseline.ParRps -gt 0) { [math]::Round((($r.ParRps - $baseline.ParRps) / $baseline.ParRps) * 100, 1) } else { 0 }
                $ordDelta = if ($baseline.OrdRps -gt 0) { [math]::Round((($r.OrdRps - $baseline.OrdRps) / $baseline.OrdRps) * 100, 1) } else { 0 }

                $seqStr = if ($seqDelta -ge 0) { "+$seqDelta%" } else { "$seqDelta%" }
                $parStr = if ($parDelta -ge 0) { "+$parDelta%" } else { "$parDelta%" }
                $ordStr = if ($ordDelta -ge 0) { "+$ordDelta%" } else { "$ordDelta%" }

                $row = "{0,-14} | {1,12} | {2,12} | {3,12} | {4,12}" -f $r.Mode, "vs $($baseline.Mode)", $seqStr, $parStr, $ordStr
                $color = if ($seqDelta -lt -20) { "Red" } elseif ($seqDelta -lt 0) { "Yellow" } else { "Green" }
                Write-Host $row -ForegroundColor $color
            }
        }

        Write-Host $separator -ForegroundColor Gray
        Write-Host ""
    }

    # ===== Legend =====
    Write-Host "┌─────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Gray
    Write-Host "│ Legend:                                                                             │" -ForegroundColor Gray
    Write-Host "│   Seq RPS  - Sequential requests/sec ($SequentialCount requests)                              │" -ForegroundColor Gray
    Write-Host "│   Par RPS  - Parallel requests/sec ($ParallelCount concurrent runspaces)                      │" -ForegroundColor Gray
    Write-Host "│   Ord RPS  - Order creation requests/sec ($OrderCount concurrent)                             │" -ForegroundColor Gray
    Write-Host "│   Jitter   - Latency variance (Max - Min)                                           │" -ForegroundColor Gray
    Write-Host "└─────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Gray
    Write-Host ""
}

# ===== Main Execution =====

Write-Header "Catga OrderSystem Cross-Mode Stress Test"
Write-Host ""
Write-Host "Test Configuration:" -ForegroundColor Yellow
Write-Host "  Sequential requests: $SequentialCount" -ForegroundColor Gray
Write-Host "  Parallel requests:   $ParallelCount" -ForegroundColor Gray
Write-Host "  Order creations:     $OrderCount" -ForegroundColor Gray
Write-Host ""

# Stop any existing processes
Write-Host "Stopping existing processes..." -ForegroundColor Gray
Stop-AllDotnet

# ===== Single Mode Tests =====
if (-not $SkipSingle) {
    Write-SubHeader "Single Mode (In-Memory)"
    Write-Host "  Starting single instance..." -ForegroundColor Gray

    $singleProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$ExamplesDir\OrderSystem.Api\OrderSystem.Api.csproj" -PassThru -WindowStyle Hidden

    Write-Host "  Waiting for service" -NoNewline -ForegroundColor Gray
    $ready = Wait-ForService "http://localhost:5275" -MaxWaitSeconds 60

    if ($ready) {
        $result = Run-FullStressTest -Mode "Single" -Endpoint "http://localhost:5275" -Infrastructure "In-Memory"
        $AllResults += $result
    } else {
        Write-Host "  ✗ Service failed to start" -ForegroundColor Red
    }

    Stop-AllDotnet
}

# ===== Cluster Mode Tests (Aspire with Redis + NATS) =====
if (-not $SkipCluster) {
    Write-SubHeader "Cluster Mode (Aspire: Redis + NATS)"
    Write-Host "  Starting Aspire with 3 replicas..." -ForegroundColor Gray

    $env:CLUSTER_MODE = "true"
    $aspireProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$ExamplesDir\OrderSystem.AppHost\OrderSystem.AppHost.csproj" -PassThru -WindowStyle Hidden

    Write-Host "  Waiting for service" -NoNewline -ForegroundColor Gray
    $ready = Wait-ForService "http://localhost:5275" -MaxWaitSeconds 180

    if ($ready) {
        $result = Run-FullStressTest -Mode "Cluster (3x)" -Endpoint "http://localhost:5275" -Infrastructure "Redis+NATS"
        $AllResults += $result
    } else {
        Write-Host "  ✗ Service failed to start" -ForegroundColor Red
    }

    $env:CLUSTER_MODE = $null
    Stop-AllDotnet
}

# ===== Aspire Single Mode (Redis + NATS, 1 replica) =====
if (-not $SkipCluster) {
    Write-SubHeader "Aspire Mode (Redis + NATS, 1 replica)"
    Write-Host "  Starting Aspire with 1 replica..." -ForegroundColor Gray

    $env:CLUSTER_MODE = "false"
    $aspireProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$ExamplesDir\OrderSystem.AppHost\OrderSystem.AppHost.csproj" -PassThru -WindowStyle Hidden

    Write-Host "  Waiting for service" -NoNewline -ForegroundColor Gray
    $ready = Wait-ForService "http://localhost:5275" -MaxWaitSeconds 180

    if ($ready) {
        $result = Run-FullStressTest -Mode "Aspire (1x)" -Endpoint "http://localhost:5275" -Infrastructure "Redis+NATS"
        $AllResults += $result
    } else {
        Write-Host "  ✗ Service failed to start" -ForegroundColor Red
    }

    $env:CLUSTER_MODE = $null
    Stop-AllDotnet
}

# ===== Output Results =====
if ($AllResults.Count -gt 0) {
    Write-ResultsTable -Results $AllResults
} else {
    Write-Host ""
    Write-Host "No tests were run." -ForegroundColor Yellow
}

Write-Host "Cross-mode stress test completed." -ForegroundColor Green
Write-Host ""
