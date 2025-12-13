#Requires -Version 7.0
<#
.SYNOPSIS
    OrderSystem Deployment Test Script - Comprehensive testing for all deployment modes
.DESCRIPTION
    Tests InMemory, Redis, and NATS deployments with functional and stress testing
.EXAMPLE
    .\test-deployment.ps1 -Mode memory
    .\test-deployment.ps1 -Mode redis -StressTest
    .\test-deployment.ps1 -Mode all -StressTest -Concurrency 50
#>

param(
    [ValidateSet("memory", "redis", "nats", "cluster", "all")]
    [string]$Mode = "memory",

    [switch]$StressTest,

    [int]$Concurrency = 20,

    [int]$RequestCount = 200,

    [int]$WaitSeconds = 30,

    [switch]$SkipBuild,

    [switch]$SkipCleanup
)

$ErrorActionPreference = "Stop"
$BaseUrl = "http://localhost:5275"
$ComposeFile = "docker-compose.prod.yml"

# Colors
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warn { Write-Host $args -ForegroundColor Yellow }
function Write-Err { Write-Host $args -ForegroundColor Red }

# Test results
$script:TestResults = @{
    Passed = 0
    Failed = 0
    Tests = @()
}

function Add-TestResult($Name, $Success, $Duration, $Message = "") {
    $script:TestResults.Tests += @{
        Name = $Name
        Success = $Success
        Duration = $Duration
        Message = $Message
    }
    if ($Success) { $script:TestResults.Passed++ } else { $script:TestResults.Failed++ }
}

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Url,
        [string]$Method = "GET",
        [string]$Body = $null,
        [int]$ExpectedStatus = 200
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $params = @{
            Uri = $Url
            Method = $Method
            ContentType = "application/json"
            TimeoutSec = 30
        }
        if ($Body) { $params.Body = $Body }

        $response = Invoke-RestMethod @params -StatusCodeVariable statusCode
        $sw.Stop()

        $success = $true
        Add-TestResult -Name $Name -Success $success -Duration $sw.ElapsedMilliseconds
        Write-Success "  [PASS] $Name (${statusCode}, $($sw.ElapsedMilliseconds)ms)"
        return $response
    }
    catch {
        $sw.Stop()
        Add-TestResult -Name $Name -Success $false -Duration $sw.ElapsedMilliseconds -Message $_.Exception.Message
        Write-Err "  [FAIL] $Name - $($_.Exception.Message)"
        return $null
    }
}

function Wait-ServiceReady {
    param([int]$TimeoutSeconds = 60)

    Write-Info "Waiting for service to be ready..."
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSeconds) {
        try {
            $null = Invoke-RestMethod -Uri "$BaseUrl/health" -TimeoutSec 5
            Write-Success "Service is ready!"
            return $true
        }
        catch {
            Write-Host "." -NoNewline
            Start-Sleep -Seconds 2
        }
    }
    Write-Err "Service failed to start within $TimeoutSeconds seconds"
    return $false
}

function Start-Deployment {
    param([string]$Profile)

    Write-Info "`n=========================================="
    Write-Info "Starting deployment: $Profile"
    Write-Info "==========================================`n"

    # Stop existing containers
    Write-Info "Stopping existing containers..."
    docker-compose -f $ComposeFile down --remove-orphans 2>$null

    # Build if needed
    if (-not $SkipBuild) {
        Write-Info "Building images..."
        docker-compose -f $ComposeFile build --quiet
    }

    # Start services
    Write-Info "Starting $Profile profile..."
    docker-compose -f $ComposeFile --profile $Profile up -d

    # Wait for ready
    if (-not (Wait-ServiceReady -TimeoutSeconds $WaitSeconds)) {
        throw "Service failed to start"
    }
}

function Stop-Deployment {
    if (-not $SkipCleanup) {
        Write-Info "Stopping deployment..."
        docker-compose -f $ComposeFile down --remove-orphans 2>$null
    }
}

function Run-FunctionalTests {
    Write-Info "`n--- Functional Tests ---`n"

    # Health check
    Test-Endpoint -Name "Health Check" -Url "$BaseUrl/health"

    # System info
    $sysInfo = Test-Endpoint -Name "System Info" -Url "$BaseUrl/api/system/info"
    if ($sysInfo) {
        Write-Info "  Transport: $($sysInfo.transport), Persistence: $($sysInfo.persistence)"
    }

    # Get initial stats
    Test-Endpoint -Name "Get Stats (Initial)" -Url "$BaseUrl/api/orders/stats"

    # Create order
    $orderBody = @{
        customerId = "TEST-FUNC-001"
        items = @(
            @{ productId = "PROD-001"; productName = "Test Product"; quantity = 2; unitPrice = 99.99 }
        )
    } | ConvertTo-Json

    $order = Test-Endpoint -Name "Create Order" -Url "$BaseUrl/api/orders" -Method "POST" -Body $orderBody

    if ($order) {
        $orderId = $order.orderId
        Write-Info "  Created Order: $orderId"

        # Get order
        Test-Endpoint -Name "Get Order" -Url "$BaseUrl/api/orders/$orderId"

        # Pay order
        $payBody = @{ paymentMethod = "Credit Card"; transactionId = "TXN-$(Get-Random)" } | ConvertTo-Json
        Test-Endpoint -Name "Pay Order" -Url "$BaseUrl/api/orders/$orderId/pay" -Method "POST" -Body $payBody

        # Process order
        Test-Endpoint -Name "Process Order" -Url "$BaseUrl/api/orders/$orderId/process" -Method "POST" -Body "{}"

        # Ship order
        $shipBody = @{ trackingNumber = "TRK-$(Get-Random)" } | ConvertTo-Json
        Test-Endpoint -Name "Ship Order" -Url "$BaseUrl/api/orders/$orderId/ship" -Method "POST" -Body $shipBody

        # Deliver order
        Test-Endpoint -Name "Deliver Order" -Url "$BaseUrl/api/orders/$orderId/deliver" -Method "POST" -Body "{}"

        # Verify final status
        $finalOrder = Test-Endpoint -Name "Verify Delivered" -Url "$BaseUrl/api/orders/$orderId"
        if ($finalOrder -and $finalOrder.status -eq 4) {
            Write-Success "  Order lifecycle completed successfully!"
        }
    }

    # Create order with Flow
    $flowOrder = Test-Endpoint -Name "Create Order (Flow)" -Url "$BaseUrl/api/orders/flow" -Method "POST" -Body $orderBody
    if ($flowOrder) {
        Write-Info "  Created Flow Order: $($flowOrder.orderId)"
    }

    # Test cancel
    $cancelBody = @{
        customerId = "TEST-CANCEL-001"
        items = @(@{ productId = "PROD-002"; productName = "Cancel Test"; quantity = 1; unitPrice = 50.00 })
    } | ConvertTo-Json

    $cancelOrder = Test-Endpoint -Name "Create Order (Cancel Test)" -Url "$BaseUrl/api/orders" -Method "POST" -Body $cancelBody
    if ($cancelOrder) {
        $cancelReasonBody = @{ reason = "Test cancellation" } | ConvertTo-Json
        Test-Endpoint -Name "Cancel Order" -Url "$BaseUrl/api/orders/$($cancelOrder.orderId)/cancel" -Method "POST" -Body $cancelReasonBody
    }

    # Get all orders
    Test-Endpoint -Name "Get All Orders" -Url "$BaseUrl/api/orders?limit=50"

    # Get final stats
    Test-Endpoint -Name "Get Stats (Final)" -Url "$BaseUrl/api/orders/stats"
}

function Run-StressTest {
    Write-Info "`n--- Stress Test ($Concurrency concurrent, $RequestCount requests) ---`n"

    $orderBody = @{
        customerId = "STRESS-TEST"
        items = @(@{ productId = "STRESS-001"; productName = "Stress Test"; quantity = 1; unitPrice = 10.00 })
    } | ConvertTo-Json

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $results = @{
        Success = 0
        Failed = 0
        Latencies = @()
    }

    # Create order stress test
    Write-Info "Running Create Order stress test..."
    $jobs = @()
    $requestsPerJob = [math]::Ceiling($RequestCount / $Concurrency)

    1..$Concurrency | ForEach-Object {
        $jobs += Start-Job -ScriptBlock {
            param($BaseUrl, $Body, $Count)
            $results = @{ Success = 0; Failed = 0; Latencies = @() }

            1..$Count | ForEach-Object {
                $reqSw = [System.Diagnostics.Stopwatch]::StartNew()
                try {
                    $null = Invoke-RestMethod -Uri "$BaseUrl/api/orders" -Method POST -Body $Body -ContentType "application/json" -TimeoutSec 30
                    $reqSw.Stop()
                    $results.Success++
                    $results.Latencies += $reqSw.ElapsedMilliseconds
                }
                catch {
                    $reqSw.Stop()
                    $results.Failed++
                }
            }
            return $results
        } -ArgumentList $BaseUrl, $orderBody, $requestsPerJob
    }

    # Wait for all jobs
    $jobs | Wait-Job | ForEach-Object {
        $jobResult = Receive-Job $_
        $results.Success += $jobResult.Success
        $results.Failed += $jobResult.Failed
        $results.Latencies += $jobResult.Latencies
    }
    $jobs | Remove-Job

    $sw.Stop()

    # Calculate metrics
    $totalRequests = $results.Success + $results.Failed
    $successRate = if ($totalRequests -gt 0) { ($results.Success / $totalRequests * 100) } else { 0 }
    $rps = if ($sw.Elapsed.TotalSeconds -gt 0) { $totalRequests / $sw.Elapsed.TotalSeconds } else { 0 }

    $avgLatency = if ($results.Latencies.Count -gt 0) { ($results.Latencies | Measure-Object -Average).Average } else { 0 }
    $minLatency = if ($results.Latencies.Count -gt 0) { ($results.Latencies | Measure-Object -Minimum).Minimum } else { 0 }
    $maxLatency = if ($results.Latencies.Count -gt 0) { ($results.Latencies | Measure-Object -Maximum).Maximum } else { 0 }
    $p95Latency = if ($results.Latencies.Count -gt 0) {
        $sorted = $results.Latencies | Sort-Object
        $sorted[[math]::Floor($sorted.Count * 0.95)]
    } else { 0 }

    Write-Info "`nStress Test Results:"
    Write-Info "  Total Requests: $totalRequests"
    Write-Success "  Successful: $($results.Success)"
    if ($results.Failed -gt 0) { Write-Err "  Failed: $($results.Failed)" }
    Write-Info "  Success Rate: $([math]::Round($successRate, 2))%"
    Write-Info "  Requests/sec: $([math]::Round($rps, 2))"
    Write-Info "  Duration: $([math]::Round($sw.Elapsed.TotalSeconds, 2))s"
    Write-Info "`nLatency (ms):"
    Write-Info "  Min: $([math]::Round($minLatency, 2))"
    Write-Info "  Avg: $([math]::Round($avgLatency, 2))"
    Write-Info "  Max: $([math]::Round($maxLatency, 2))"
    Write-Info "  P95: $([math]::Round($p95Latency, 2))"

    # Add stress test result
    $stressSuccess = $successRate -ge 95
    Add-TestResult -Name "Stress Test ($RequestCount requests)" -Success $stressSuccess -Duration $sw.ElapsedMilliseconds -Message "RPS: $([math]::Round($rps, 2)), Success: $([math]::Round($successRate, 2))%"
}

function Run-ReadStressTest {
    Write-Info "`n--- Read Stress Test ---`n"

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $results = @{ Success = 0; Failed = 0; Latencies = @() }

    $jobs = @()
    $requestsPerJob = [math]::Ceiling($RequestCount / $Concurrency)

    1..$Concurrency | ForEach-Object {
        $jobs += Start-Job -ScriptBlock {
            param($BaseUrl, $Count)
            $results = @{ Success = 0; Failed = 0; Latencies = @() }

            1..$Count | ForEach-Object {
                $reqSw = [System.Diagnostics.Stopwatch]::StartNew()
                try {
                    $null = Invoke-RestMethod -Uri "$BaseUrl/api/orders/stats" -TimeoutSec 30
                    $reqSw.Stop()
                    $results.Success++
                    $results.Latencies += $reqSw.ElapsedMilliseconds
                }
                catch {
                    $reqSw.Stop()
                    $results.Failed++
                }
            }
            return $results
        } -ArgumentList $BaseUrl, $requestsPerJob
    }

    $jobs | Wait-Job | ForEach-Object {
        $jobResult = Receive-Job $_
        $results.Success += $jobResult.Success
        $results.Failed += $jobResult.Failed
        $results.Latencies += $jobResult.Latencies
    }
    $jobs | Remove-Job

    $sw.Stop()

    $totalRequests = $results.Success + $results.Failed
    $rps = if ($sw.Elapsed.TotalSeconds -gt 0) { $totalRequests / $sw.Elapsed.TotalSeconds } else { 0 }
    $avgLatency = if ($results.Latencies.Count -gt 0) { ($results.Latencies | Measure-Object -Average).Average } else { 0 }

    Write-Info "Read Stress Test Results:"
    Write-Info "  Requests/sec: $([math]::Round($rps, 2))"
    Write-Info "  Avg Latency: $([math]::Round($avgLatency, 2))ms"

    Add-TestResult -Name "Read Stress Test" -Success ($results.Failed -eq 0) -Duration $sw.ElapsedMilliseconds
}

function Show-Summary {
    Write-Info "`n=========================================="
    Write-Info "TEST SUMMARY"
    Write-Info "==========================================`n"

    Write-Info "Total Tests: $($script:TestResults.Passed + $script:TestResults.Failed)"
    Write-Success "Passed: $($script:TestResults.Passed)"
    if ($script:TestResults.Failed -gt 0) {
        Write-Err "Failed: $($script:TestResults.Failed)"
    }

    if ($script:TestResults.Failed -gt 0) {
        Write-Warn "`nFailed Tests:"
        $script:TestResults.Tests | Where-Object { -not $_.Success } | ForEach-Object {
            Write-Err "  - $($_.Name): $($_.Message)"
        }
    }

    Write-Info "`n"
}

function Test-Mode {
    param([string]$Profile)

    try {
        Start-Deployment -Profile $Profile
        Run-FunctionalTests

        if ($StressTest) {
            Run-StressTest
            Run-ReadStressTest
        }
    }
    catch {
        Write-Err "Error: $($_.Exception.Message)"
    }
    finally {
        Stop-Deployment
    }
}

# Main
Write-Info @"

 ██████╗ ██████╗ ██████╗ ███████╗██████╗ ███████╗██╗   ██╗███████╗
██╔═══██╗██╔══██╗██╔══██╗██╔════╝██╔══██╗██╔════╝╚██╗ ██╔╝██╔════╝
██║   ██║██████╔╝██║  ██║█████╗  ██████╔╝███████╗ ╚████╔╝ ███████╗
██║   ██║██╔══██╗██║  ██║██╔══╝  ██╔══██╗╚════██║  ╚██╔╝  ╚════██║
╚██████╔╝██║  ██║██████╔╝███████╗██║  ██║███████║   ██║   ███████║
 ╚═════╝ ╚═╝  ╚═╝╚═════╝ ╚══════╝╚═╝  ╚═╝╚══════╝   ╚═╝   ╚══════╝
                     Deployment Test Suite

"@

Push-Location $PSScriptRoot\..

try {
    if ($Mode -eq "all") {
        @("memory", "redis", "nats") | ForEach-Object {
            $script:TestResults = @{ Passed = 0; Failed = 0; Tests = @() }
            Test-Mode -Profile $_
            Show-Summary
        }
    }
    else {
        Test-Mode -Profile $Mode
        Show-Summary
    }
}
finally {
    Pop-Location
}

# Exit with error code if tests failed
if ($script:TestResults.Failed -gt 0) {
    exit 1
}
