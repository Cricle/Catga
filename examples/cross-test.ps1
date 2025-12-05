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
        RedisReadRps = 0
        RedisWriteRps = 0

        # NATS test (if available)
        NatsOk = $false
        NatsLatencyMs = 0
        NatsPubRps = 0
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

    # Test Redis performance (via order read/write - uses Redis in cluster mode)
    if ($Infrastructure -match "Redis") {
        Write-Host "  Running Redis tests (100 operations)..." -ForegroundColor Gray
        $orderIds = @($orderResults | Where-Object { $_.Success -and $_.OrderId } | ForEach-Object { $_.OrderId })

        if ($orderIds.Count -gt 0) {
            # Redis Read Test - 100 sequential reads
            $redisReadCount = 100
            $redisReadTimes = [System.Collections.Generic.List[double]]::new()
            $redisReadStart = Get-Date
            for ($i = 0; $i -lt $redisReadCount; $i++) {
                $orderId = $orderIds[$i % $orderIds.Count]
                $reqStart = Get-Date
                try {
                    $null = Invoke-RestMethod -Uri "$Endpoint/api/orders/$orderId" -Method GET -TimeoutSec 5
                    $redisReadTimes.Add(((Get-Date) - $reqStart).TotalMilliseconds)
                } catch { }
            }
            $redisReadDuration = ((Get-Date) - $redisReadStart).TotalMilliseconds

            if ($redisReadTimes.Count -gt 0) {
                $results.RedisOk = $true
                $readStats = $redisReadTimes | Measure-Object -Average -Minimum -Maximum
                $results.RedisLatencyMs = [math]::Round($readStats.Average, 2)
                $results.RedisReadRps = [math]::Round($redisReadCount / ($redisReadDuration / 1000), 1)
                $results.RedisReadMinMs = [math]::Round($readStats.Minimum, 2)
                $results.RedisReadMaxMs = [math]::Round($readStats.Maximum, 2)
            }

            # Redis Write Test - 50 order creations (direct write to Redis)
            $redisWriteCount = 50
            $redisWriteTimes = [System.Collections.Generic.List[double]]::new()
            $redisWriteStart = Get-Date

            $writePool = [runspacefactory]::CreateRunspacePool(1, 20)
            $writePool.Open()
            $writeRunspaces = [System.Collections.Generic.List[object]]::new()

            $writeScript = {
                param($Url, $Index)
                $payload = @{
                    customerId = "REDIS-WRITE-$Index"
                    items = @(@{ productId = "RW-PROD"; productName = "Redis Write Test"; quantity = 1; unitPrice = 1.00 })
                    shippingAddress = "Redis Test"; paymentMethod = "card"
                } | ConvertTo-Json -Depth 5
                $start = [datetime]::Now
                try {
                    $null = Invoke-RestMethod -Uri $Url -Method POST -ContentType "application/json" -Body $payload -TimeoutSec 5
                    return @{ Success = $true; Duration = ([datetime]::Now - $start).TotalMilliseconds }
                } catch {
                    return @{ Success = $false; Duration = 0 }
                }
            }

            for ($i = 0; $i -lt $redisWriteCount; $i++) {
                $ps = [powershell]::Create().AddScript($writeScript).AddArgument("$Endpoint/api/orders").AddArgument($i)
                $ps.RunspacePool = $writePool
                $writeRunspaces.Add(@{ Pipe = $ps; Handle = $ps.BeginInvoke() })
            }

            $writeResults = @()
            foreach ($rs in $writeRunspaces) {
                $writeResults += $rs.Pipe.EndInvoke($rs.Handle)
                $rs.Pipe.Dispose()
            }
            $redisWriteDuration = ((Get-Date) - $redisWriteStart).TotalMilliseconds
            $writePool.Close()

            $successWrites = @($writeResults | Where-Object { $_.Success })
            if ($successWrites.Count -gt 0) {
                $writeStats = $successWrites.Duration | Measure-Object -Average -Minimum -Maximum
                $results.RedisWriteRps = [math]::Round($redisWriteCount / ($redisWriteDuration / 1000), 1)
                $results.RedisWriteAvgMs = [math]::Round($writeStats.Average, 2)
                $results.RedisWriteMinMs = [math]::Round($writeStats.Minimum, 2)
                $results.RedisWriteMaxMs = [math]::Round($writeStats.Maximum, 2)
            }

            Write-Host "  ✓ Redis Read:  $($results.RedisReadRps) req/s, Avg $($results.RedisLatencyMs)ms (Min $($results.RedisReadMinMs), Max $($results.RedisReadMaxMs))" -ForegroundColor Green
            Write-Host "  ✓ Redis Write: $($results.RedisWriteRps) req/s, Avg $($results.RedisWriteAvgMs)ms (Min $($results.RedisWriteMinMs), Max $($results.RedisWriteMaxMs))" -ForegroundColor Green
        }
    }

    # Test NATS performance (via rapid API calls that trigger NATS messaging)
    if ($Infrastructure -match "NATS") {
        Write-Host "  Running NATS tests (200 operations)..." -ForegroundColor Gray

        # NATS Sequential Test - 100 rapid calls
        $natsSeqCount = 100
        $natsSeqTimes = [System.Collections.Generic.List[double]]::new()
        $natsSeqStart = Get-Date
        for ($i = 0; $i -lt $natsSeqCount; $i++) {
            $reqStart = Get-Date
            try {
                $null = Invoke-RestMethod -Uri "$Endpoint/api/cluster/node" -Method GET -TimeoutSec 5
                $natsSeqTimes.Add(((Get-Date) - $reqStart).TotalMilliseconds)
            } catch { }
        }
        $natsSeqDuration = ((Get-Date) - $natsSeqStart).TotalMilliseconds

        if ($natsSeqTimes.Count -gt 0) {
            $results.NatsOk = $true
            $natsSeqStats = $natsSeqTimes | Measure-Object -Average -Minimum -Maximum
            $results.NatsLatencyMs = [math]::Round($natsSeqStats.Average, 2)
            $results.NatsSeqRps = [math]::Round($natsSeqCount / ($natsSeqDuration / 1000), 1)
            $results.NatsMinMs = [math]::Round($natsSeqStats.Minimum, 2)
            $results.NatsMaxMs = [math]::Round($natsSeqStats.Maximum, 2)
        }

        # NATS Parallel Test - 100 concurrent calls
        $natsParCount = 100
        $natsPool = [runspacefactory]::CreateRunspacePool(1, $natsParCount)
        $natsPool.Open()
        $natsRunspaces = [System.Collections.Generic.List[object]]::new()

        $natsScript = {
            param($Url)
            $start = [datetime]::Now
            try {
                $null = Invoke-RestMethod -Uri $Url -Method GET -TimeoutSec 5
                return @{ Success = $true; Duration = ([datetime]::Now - $start).TotalMilliseconds }
            } catch {
                return @{ Success = $false; Duration = 0 }
            }
        }

        $natsParStart = Get-Date
        for ($i = 0; $i -lt $natsParCount; $i++) {
            $ps = [powershell]::Create().AddScript($natsScript).AddArgument("$Endpoint/api/cluster/node")
            $ps.RunspacePool = $natsPool
            $natsRunspaces.Add(@{ Pipe = $ps; Handle = $ps.BeginInvoke() })
        }

        $natsParResults = @()
        foreach ($rs in $natsRunspaces) {
            $natsParResults += $rs.Pipe.EndInvoke($rs.Handle)
            $rs.Pipe.Dispose()
        }
        $natsParDuration = ((Get-Date) - $natsParStart).TotalMilliseconds
        $natsPool.Close()

        $successNats = @($natsParResults | Where-Object { $_.Success })
        if ($successNats.Count -gt 0) {
            $natsParStats = $successNats.Duration | Measure-Object -Average -Minimum -Maximum
            $results.NatsPubRps = [math]::Round($natsParCount / ($natsParDuration / 1000), 1)
            $results.NatsParAvgMs = [math]::Round($natsParStats.Average, 2)
        }

        Write-Host "  ✓ NATS Seq:  $($results.NatsSeqRps) req/s, Avg $($results.NatsLatencyMs)ms (Min $($results.NatsMinMs), Max $($results.NatsMaxMs))" -ForegroundColor Green
        Write-Host "  ✓ NATS Par:  $($results.NatsPubRps) req/s, Avg $($results.NatsParAvgMs)ms ($natsParCount concurrent)" -ForegroundColor Green
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

    # ===== Table 4: Redis Performance (Detailed) =====
    $hasRedis = $Results | Where-Object { $_.Infrastructure -match "Redis" }
    if ($hasRedis) {
        Write-Host "┌───────────────────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
        Write-Host "│                                    REDIS PERFORMANCE (100 reads, 50 writes)                       │" -ForegroundColor Cyan
        Write-Host "└───────────────────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

        # Redis Read Details
        Write-Host ""
        Write-Host "  Redis READ Performance:" -ForegroundColor Yellow
        $readHeader = "  {0,-14} | {1,10} | {2,10} | {3,10} | {4,10}" -f "Mode", "RPS", "Min(ms)", "Avg(ms)", "Max(ms)"
        Write-Host $readHeader -ForegroundColor Gray
        Write-Host "  $("-" * 65)" -ForegroundColor Gray

        foreach ($r in $Results) {
            if ($r.Infrastructure -match "Redis") {
                $rps = if ($r.RedisReadRps -gt 0) { $r.RedisReadRps } else { "N/A" }
                $minMs = if ($r.RedisReadMinMs) { $r.RedisReadMinMs } else { "N/A" }
                $avgMs = if ($r.RedisLatencyMs -gt 0) { $r.RedisLatencyMs } else { "N/A" }
                $maxMs = if ($r.RedisReadMaxMs) { $r.RedisReadMaxMs } else { "N/A" }
                $row = "  {0,-14} | {1,10} | {2,10} | {3,10} | {4,10}" -f $r.Mode, $rps, $minMs, $avgMs, $maxMs
                Write-Host $row -ForegroundColor Green
            }
        }

        # Redis Write Details
        Write-Host ""
        Write-Host "  Redis WRITE Performance:" -ForegroundColor Yellow
        $writeHeader = "  {0,-14} | {1,10} | {2,10} | {3,10} | {4,10}" -f "Mode", "RPS", "Min(ms)", "Avg(ms)", "Max(ms)"
        Write-Host $writeHeader -ForegroundColor Gray
        Write-Host "  $("-" * 65)" -ForegroundColor Gray

        foreach ($r in $Results) {
            if ($r.Infrastructure -match "Redis") {
                $rps = if ($r.RedisWriteRps -gt 0) { $r.RedisWriteRps } else { "N/A" }
                $minMs = if ($r.RedisWriteMinMs) { $r.RedisWriteMinMs } else { "N/A" }
                $avgMs = if ($r.RedisWriteAvgMs) { $r.RedisWriteAvgMs } else { "N/A" }
                $maxMs = if ($r.RedisWriteMaxMs) { $r.RedisWriteMaxMs } else { "N/A" }
                $row = "  {0,-14} | {1,10} | {2,10} | {3,10} | {4,10}" -f $r.Mode, $rps, $minMs, $avgMs, $maxMs
                Write-Host $row -ForegroundColor Green
            }
        }

        Write-Host ""
    }

    # ===== Table 5: NATS Performance (Detailed) =====
    $hasNats = $Results | Where-Object { $_.Infrastructure -match "NATS" }
    if ($hasNats) {
        Write-Host "┌───────────────────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
        Write-Host "│                                    NATS PERFORMANCE (100 seq, 100 par)                            │" -ForegroundColor Cyan
        Write-Host "└───────────────────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

        # NATS Sequential Details
        Write-Host ""
        Write-Host "  NATS SEQUENTIAL Performance:" -ForegroundColor Yellow
        $seqHeader = "  {0,-14} | {1,10} | {2,10} | {3,10} | {4,10}" -f "Mode", "RPS", "Min(ms)", "Avg(ms)", "Max(ms)"
        Write-Host $seqHeader -ForegroundColor Gray
        Write-Host "  $("-" * 65)" -ForegroundColor Gray

        foreach ($r in $Results) {
            if ($r.Infrastructure -match "NATS") {
                $rps = if ($r.NatsSeqRps) { $r.NatsSeqRps } else { "N/A" }
                $minMs = if ($r.NatsMinMs) { $r.NatsMinMs } else { "N/A" }
                $avgMs = if ($r.NatsLatencyMs -gt 0) { $r.NatsLatencyMs } else { "N/A" }
                $maxMs = if ($r.NatsMaxMs) { $r.NatsMaxMs } else { "N/A" }
                $row = "  {0,-14} | {1,10} | {2,10} | {3,10} | {4,10}" -f $r.Mode, $rps, $minMs, $avgMs, $maxMs
                Write-Host $row -ForegroundColor Green
            }
        }

        # NATS Parallel Details
        Write-Host ""
        Write-Host "  NATS PARALLEL Performance (100 concurrent):" -ForegroundColor Yellow
        $parHeader = "  {0,-14} | {1,10} | {2,10}" -f "Mode", "RPS", "Avg(ms)"
        Write-Host $parHeader -ForegroundColor Gray
        Write-Host "  $("-" * 40)" -ForegroundColor Gray

        foreach ($r in $Results) {
            if ($r.Infrastructure -match "NATS") {
                $rps = if ($r.NatsPubRps -gt 0) { $r.NatsPubRps } else { "N/A" }
                $avgMs = if ($r.NatsParAvgMs) { $r.NatsParAvgMs } else { "N/A" }
                $row = "  {0,-14} | {1,10} | {2,10}" -f $r.Mode, $rps, $avgMs
                Write-Host $row -ForegroundColor Green
            }
        }

        Write-Host ""
    }

    # ===== Table 6: Infrastructure Summary =====
    Write-Host "┌─────────────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
    Write-Host "│                           INFRASTRUCTURE SUMMARY                                    │" -ForegroundColor Cyan
    Write-Host "└─────────────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

    $infraHeader = "{0,-14} | {1,-12} | {2,10} | {3,10} | {4,10}" -f "Mode", "Infra", "Health", "Redis", "NATS"
    Write-Host $infraHeader -ForegroundColor Yellow
    Write-Host $separator -ForegroundColor Gray

    foreach ($r in $Results) {
        $healthIcon = if ($r.HealthOk) { "✓ OK" } else { "✗ FAIL" }
        $redisIcon = if ($r.RedisOk) { "✓ OK" } elseif ($r.Infrastructure -match "Redis") { "✗ FAIL" } else { "N/A" }
        $natsIcon = if ($r.NatsOk) { "✓ OK" } elseif ($r.Infrastructure -match "NATS") { "✗ FAIL" } else { "N/A" }

        $row = "{0,-14} | {1,-12} | {2,10} | {3,10} | {4,10}" -f `
            $r.Mode, `
            $r.Infrastructure, `
            $healthIcon, `
            $redisIcon, `
            $natsIcon

        $color = if ($r.HealthOk -and ($r.RedisOk -or $r.Infrastructure -notmatch "Redis") -and ($r.NatsOk -or $r.Infrastructure -notmatch "NATS")) { "Green" } else { "Red" }
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
