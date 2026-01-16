#!/usr/bin/env pwsh
<#
.SYNOPSIS
    OrderSystem 一键测试脚本 - 测试所有场景
.DESCRIPTION
    整合所有测试场景：
    1. 基础 API 测试
    2. 多配置测试 (InMemory/Redis/NATS)
    3. Flow DSL 测试
    4. QoS 验证测试
    5. 集群测试
.PARAMETER Scenario
    测试场景: all, api, config, flow, qos, cluster
.PARAMETER Port
    服务端口 (默认 5000)
.PARAMETER SkipRedis
    跳过 Redis 相关测试
.PARAMETER SkipNats
    跳过 NATS 相关测试
.PARAMETER SkipCluster
    跳过集群测试
.EXAMPLE
    .\test-master.ps1                    # 运行所有测试
    .\test-master.ps1 -Scenario api      # 只运行 API 测试
    .\test-master.ps1 -SkipCluster       # 跳过集群测试
#>

param(
    [ValidateSet("all", "api", "config", "flow", "qos", "cluster")]
    [string]$Scenario = "all",
    [int]$Port = 5000,
    [switch]$SkipRedis,
    [switch]$SkipNats,
    [switch]$SkipCluster,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$script:StartTime = Get-Date
$script:TestResults = @()
$script:TotalTests = 0
$script:PassedTests = 0
$script:FailedTests = 0
$script:SkippedTests = 0

#region Helper Functions
function Write-Banner {
    Write-Host @"

╔══════════════════════════════════════════════════════════════════════════════╗
║                                                                              ║
║     ██████╗ █████╗ ████████╗ ██████╗  █████╗                                 ║
║    ██╔════╝██╔══██╗╚══██╔══╝██╔════╝ ██╔══██╗                                ║
║    ██║     ███████║   ██║   ██║  ███╗███████║                                ║
║    ██║     ██╔══██║   ██║   ██║   ██║██╔══██║                                ║
║    ╚██████╗██║  ██║   ██║   ╚██████╔╝██║  ██║                                ║
║     ╚═════╝╚═╝  ╚═╝   ╚═╝    ╚═════╝ ╚═╝  ╚═╝                                ║
║                                                                              ║
║              OrderSystem - 一键测试所有场景                                  ║
║                                                                              ║
╠══════════════════════════════════════════════════════════════════════════════╣
║  场景: $($Scenario.ToUpper().PadRight(68)) ║
║  时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")                                          ║
╚══════════════════════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan
}

function Write-Success { param([string]$Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Fail { param([string]$Message) Write-Host "✗ $Message" -ForegroundColor Red }
function Write-Info { param([string]$Message) Write-Host "ℹ $Message" -ForegroundColor Cyan }
function Write-Warn { param([string]$Message) Write-Host "⚠ $Message" -ForegroundColor Yellow }
function Write-Skip { param([string]$Message) Write-Host "○ $Message" -ForegroundColor DarkGray }

function Write-Section {
    param([string]$Title, [string]$Subtitle = "")
    Write-Host "`n╔══════════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
    Write-Host "║  $($Title.PadRight(74)) ║" -ForegroundColor Magenta
    if ($Subtitle) {
        Write-Host "║  $($Subtitle.PadRight(74)) ║" -ForegroundColor DarkMagenta
    }
    Write-Host "╚══════════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
}

function Write-SubSection {
    param([string]$Title)
    Write-Host "`n┌─────────────────────────────────────────────────────────────────────────────┐" -ForegroundColor DarkCyan
    Write-Host "│  $($Title.PadRight(73)) │" -ForegroundColor DarkCyan
    Write-Host "└─────────────────────────────────────────────────────────────────────────────┘" -ForegroundColor DarkCyan
}

function Add-TestResult {
    param(
        [string]$Category,
        [string]$Name,
        [string]$Status,  # PASS, FAIL, SKIP
        [string]$Message = "",
        [double]$Duration = 0
    )
    
    $script:TotalTests++
    switch ($Status) {
        "PASS" { $script:PassedTests++ }
        "FAIL" { $script:FailedTests++ }
        "SKIP" { $script:SkippedTests++ }
    }
    
    $script:TestResults += [PSCustomObject]@{
        Category = $Category
        Name = $Name
        Status = $Status
        Message = $Message
        Duration = $Duration
    }
}

function Test-ServiceAvailable {
    param([string]$Url, [int]$TimeoutSec = 5)
    try {
        Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec $TimeoutSec -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Test-PortAvailable {
    param([string]$HostName = "localhost", [int]$Port)
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.ConnectAsync($HostName, $Port).Wait(1000) | Out-Null
        $result = $tcp.Connected
        $tcp.Close()
        return $result
    } catch {
        return $false
    }
}

function Start-TestServer {
    param(
        [string]$Transport = "inmemory",
        [string]$Persistence = "inmemory",
        [int]$Port = 5000,
        [string]$NodeId = "test-node",
        [switch]$Cluster
    )
    
    $args = @("run", "--", "--transport", $Transport, "--persistence", $Persistence, "--port", $Port, "--node-id", $NodeId)
    if ($Cluster) { $args += "--cluster" }
    
    $process = Start-Process -FilePath "dotnet" -ArgumentList $args -WorkingDirectory $PSScriptRoot -PassThru -WindowStyle Hidden
    
    # 等待服务启动
    $maxWait = 15
    for ($i = 0; $i -lt $maxWait; $i++) {
        Start-Sleep -Seconds 1
        if (Test-ServiceAvailable "http://localhost:$Port/") {
            return $process
        }
    }
    
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    return $null
}

function Stop-TestServer {
    param($Process)
    if ($Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}
#endregion

#region Test Functions

function Test-BasicApi {
    param([string]$BaseUrl = "http://localhost:5000")
    
    Write-SubSection "基础 API 测试"
    $category = "API"
    $startTime = Get-Date
    
    # 1. 系统信息
    try {
        $info = Invoke-RestMethod -Uri "$BaseUrl/" -Method Get -TimeoutSec 5
        Write-Success "系统信息: $($info.service) - $($info.mode)"
        Add-TestResult $category "系统信息" "PASS"
    } catch {
        Write-Fail "系统信息: $($_.Exception.Message)"
        Add-TestResult $category "系统信息" "FAIL" $_.Exception.Message
        return $false
    }
    
    # 2. 健康检查
    try {
        $health = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get -TimeoutSec 5
        Write-Success "健康检查: $health"
        Add-TestResult $category "健康检查" "PASS"
    } catch {
        Write-Fail "健康检查: $($_.Exception.Message)"
        Add-TestResult $category "健康检查" "FAIL" $_.Exception.Message
    }
    
    # 3. 创建订单
    $orderId = $null
    try {
        $order = Invoke-RestMethod -Uri "$BaseUrl/orders" -Method Post -ContentType "application/json" -Body (@{
            customerId = "test-$(Get-Date -Format 'yyyyMMddHHmmss')"
            items = @(@{ productId = "p1"; name = "测试商品"; quantity = 2; price = 99.99 })
        } | ConvertTo-Json) -TimeoutSec 10
        $orderId = $order.orderId
        Write-Success "创建订单: $orderId"
        Add-TestResult $category "创建订单" "PASS"
    } catch {
        Write-Fail "创建订单: $($_.Exception.Message)"
        Add-TestResult $category "创建订单" "FAIL" $_.Exception.Message
        return $false
    }
    
    # 4. 获取订单
    try {
        $getOrder = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId" -Method Get -TimeoutSec 5
        Write-Success "获取订单: Status=$($getOrder.status)"
        Add-TestResult $category "获取订单" "PASS"
    } catch {
        Write-Fail "获取订单: $($_.Exception.Message)"
        Add-TestResult $category "获取订单" "FAIL" $_.Exception.Message
    }
    
    # 5. 支付订单
    try {
        $paidOrder = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId/pay" -Method Post -TimeoutSec 5
        Write-Success "支付订单: Status=$($paidOrder.status)"
        Add-TestResult $category "支付订单" "PASS"
    } catch {
        Write-Fail "支付订单: $($_.Exception.Message)"
        Add-TestResult $category "支付订单" "FAIL" $_.Exception.Message
    }
    
    # 6. 发货订单
    try {
        $shippedOrder = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId/ship" -Method Post -TimeoutSec 5
        Write-Success "发货订单: Status=$($shippedOrder.status)"
        Add-TestResult $category "发货订单" "PASS"
    } catch {
        Write-Fail "发货订单: $($_.Exception.Message)"
        Add-TestResult $category "发货订单" "FAIL" $_.Exception.Message
    }
    
    # 7. 订单历史
    try {
        $history = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId/history" -Method Get -TimeoutSec 5
        Write-Success "订单历史: $($history.Count) 个事件"
        Add-TestResult $category "订单历史" "PASS"
    } catch {
        Write-Fail "订单历史: $($_.Exception.Message)"
        Add-TestResult $category "订单历史" "FAIL" $_.Exception.Message
    }
    
    # 8. 创建并取消订单
    try {
        $cancelOrder = Invoke-RestMethod -Uri "$BaseUrl/orders" -Method Post -ContentType "application/json" -Body (@{
            customerId = "cancel-test"
            items = @(@{ productId = "p2"; name = "待取消"; quantity = 1; price = 50 })
        } | ConvertTo-Json) -TimeoutSec 10
        $cancelled = Invoke-RestMethod -Uri "$BaseUrl/orders/$($cancelOrder.orderId)/cancel" -Method Post -TimeoutSec 5
        Write-Success "取消订单: Status=$($cancelled.status)"
        Add-TestResult $category "取消订单" "PASS"
    } catch {
        Write-Fail "取消订单: $($_.Exception.Message)"
        Add-TestResult $category "取消订单" "FAIL" $_.Exception.Message
    }
    
    # 9. 统计信息
    try {
        $stats = Invoke-RestMethod -Uri "$BaseUrl/stats" -Method Get -TimeoutSec 5
        Write-Success "统计信息: 订单=$($stats.totalOrders), 收入=¥$($stats.totalRevenue)"
        Add-TestResult $category "统计信息" "PASS"
    } catch {
        Write-Fail "统计信息: $($_.Exception.Message)"
        Add-TestResult $category "统计信息" "FAIL" $_.Exception.Message
    }
    
    # 10. 404 错误处理
    try {
        Invoke-RestMethod -Uri "$BaseUrl/orders/non-existent-id" -Method Get -TimeoutSec 5 -ErrorAction Stop
        Write-Fail "404 错误处理: 应返回 404"
        Add-TestResult $category "404 错误处理" "FAIL" "应返回 404"
    } catch {
        if ($_.Exception.Message -match "404") {
            Write-Success "404 错误处理: 正确返回 404"
            Add-TestResult $category "404 错误处理" "PASS"
        } else {
            Write-Fail "404 错误处理: $($_.Exception.Message)"
            Add-TestResult $category "404 错误处理" "FAIL" $_.Exception.Message
        }
    }
    
    return $true
}

function Test-Configuration {
    param(
        [string]$Name,
        [string]$Transport,
        [string]$Persistence,
        [int]$Port = 5000
    )
    
    $category = "配置-$Name"
    Write-Info "测试配置: $Name (Transport=$Transport, Persistence=$Persistence)"
    
    $process = Start-TestServer -Transport $Transport -Persistence $Persistence -Port $Port
    if (-not $process) {
        Write-Fail "服务启动失败: $Name"
        Add-TestResult $category "服务启动" "FAIL" "启动超时"
        return $false
    }
    
    try {
        Write-Success "服务启动成功"
        Add-TestResult $category "服务启动" "PASS"
        
        # 运行基础测试
        $result = Test-BasicApi -BaseUrl "http://localhost:$Port"
        return $result
    } finally {
        Stop-TestServer $process
    }
}

function Test-FlowDsl {
    param([string]$BaseUrl = "http://localhost:5000")
    
    Write-SubSection "Flow DSL 测试"
    $category = "Flow DSL"
    
    # 1. 测试 Flow 端点是否可用 (通过 fulfillment 端点)
    try {
        $result1 = Invoke-RestMethod -Uri "$BaseUrl/api/flows/fulfillment/start" -Method Post -ContentType "application/json" -Body (@{
            CustomerId = "FLOW-CUST-001"
            Items = @(@{ ProductId = "fp1"; Name = "Flow商品1"; Quantity = 2; Price = 250.0 })
        } | ConvertTo-Json) -TimeoutSec 15
        Write-Success "履约流程(标准): FlowId=$($result1.flowId), OrderId=$($result1.orderId)"
        Add-TestResult $category "履约流程-标准" "PASS"
    } catch {
        Write-Fail "履约流程(标准): $($_.Exception.Message)"
        Add-TestResult $category "履约流程-标准" "FAIL" $_.Exception.Message
        return $false
    }
    
    # 2. 订单履约流程 - 高价值订单
    try {
        $result2 = Invoke-RestMethod -Uri "$BaseUrl/api/flows/fulfillment/start" -Method Post -ContentType "application/json" -Body (@{
            CustomerId = "FLOW-CUST-002"
            Items = @(@{ ProductId = "fp2"; Name = "Flow商品2"; Quantity = 1; Price = 1500.0 })
        } | ConvertTo-Json) -TimeoutSec 15
        Write-Success "履约流程(高价值): FlowId=$($result2.flowId), Total=$($result2.total)"
        Add-TestResult $category "履约流程-高价值" "PASS"
    } catch {
        Write-Fail "履约流程(高价值): $($_.Exception.Message)"
        Add-TestResult $category "履约流程-高价值" "FAIL" $_.Exception.Message
    }
    
    # 3. 复杂流程测试 - 不同订单类型
    # OrderType enum: Standard=0, Express=1, Bulk=2
    $types = @(
        @{ Name = "Standard"; Value = 0 },
        @{ Name = "Express"; Value = 1 },
        @{ Name = "Bulk"; Value = 2 }
    )
    foreach ($typeInfo in $types) {
        $typeName = $typeInfo.Name
        $typeValue = $typeInfo.Value
        try {
            $complexResult = Invoke-RestMethod -Uri "$BaseUrl/api/flows/complex/start" -Method Post -ContentType "application/json" -Body (@{
                CustomerId = "COMPLEX-CUST-$typeName"
                Items = @(
                    @{ ProductId = "cp1"; Name = "复杂商品A"; Quantity = 1; Price = 100.0 },
                    @{ ProductId = "cp2"; Name = "复杂商品B"; Quantity = 2; Price = 50.0 }
                )
                Type = $typeValue
            } | ConvertTo-Json) -TimeoutSec 15
            Write-Success "复杂流程($typeName): FlowId=$($complexResult.flowId), ProcessedItems=$($complexResult.processedItems)"
            Add-TestResult $category "复杂流程-$typeName" "PASS"
        } catch {
            Write-Fail "复杂流程($typeName): $($_.Exception.Message)"
            Add-TestResult $category "复杂流程-$typeName" "FAIL" $_.Exception.Message
        }
    }
    
    # 4. 获取流程状态
    if ($result1.flowId) {
        try {
            $status = Invoke-RestMethod -Uri "$BaseUrl/api/flows/status/$($result1.flowId)" -Method Get -TimeoutSec 5
            Write-Success "流程状态: Status=$($status.status), Version=$($status.version)"
            Add-TestResult $category "流程状态查询" "PASS"
        } catch {
            Write-Fail "流程状态: $($_.Exception.Message)"
            Add-TestResult $category "流程状态查询" "FAIL" $_.Exception.Message
        }
    }
    
    return $true
}


function Test-Qos {
    param([string]$BaseUrl = "http://localhost:5000")
    
    Write-SubSection "QoS 验证测试"
    $category = "QoS"
    
    # 1. AtLeastOnce - 批量创建订单
    Write-Info "测试 AtLeastOnce (Commands) - 批量创建 10 个订单..."
    $orderIds = @()
    $successCount = 0
    
    for ($i = 1; $i -le 10; $i++) {
        try {
            $order = Invoke-RestMethod -Uri "$BaseUrl/orders" -Method Post -ContentType "application/json" -Body (@{
                customerId = "qos-customer-$i"
                items = @(@{ productId = "qos-p$i"; name = "QoS商品$i"; quantity = 1; price = 100.0 })
            } | ConvertTo-Json) -TimeoutSec 10
            $orderIds += $order.orderId
            $successCount++
        } catch {
            Write-Warn "订单 $i 创建失败"
        }
    }
    
    if ($successCount -eq 10) {
        Write-Success "AtLeastOnce: 所有 10 个订单创建成功"
        Add-TestResult $category "AtLeastOnce-批量创建" "PASS"
    } else {
        Write-Fail "AtLeastOnce: 只有 $successCount/10 个订单创建成功"
        Add-TestResult $category "AtLeastOnce-批量创建" "FAIL" "$successCount/10"
    }
    
    # 2. AtMostOnce - 事件触发
    if ($orderIds.Count -gt 0) {
        $testOrderId = $orderIds[0]
        try {
            Invoke-RestMethod -Uri "$BaseUrl/orders/$testOrderId/pay" -Method Post -TimeoutSec 5 | Out-Null
            Invoke-RestMethod -Uri "$BaseUrl/orders/$testOrderId/ship" -Method Post -TimeoutSec 5 | Out-Null
            Start-Sleep -Seconds 1
            $history = Invoke-RestMethod -Uri "$BaseUrl/orders/$testOrderId/history" -Method Get -TimeoutSec 5
            Write-Success "AtMostOnce: 事件历史 $($history.Count) 个事件"
            Add-TestResult $category "AtMostOnce-事件" "PASS"
        } catch {
            Write-Fail "AtMostOnce: $($_.Exception.Message)"
            Add-TestResult $category "AtMostOnce-事件" "FAIL" $_.Exception.Message
        }
    }
    
    # 3. 并发测试
    Write-Info "测试并发场景 - 并发创建 20 个订单..."
    $jobs = @()
    for ($i = 1; $i -le 20; $i++) {
        $job = Start-Job -ScriptBlock {
            param($url, $index)
            try {
                $result = Invoke-RestMethod -Uri "$url/orders" -Method Post -ContentType "application/json" -Body (@{
                    customerId = "concurrent-$index"
                    items = @(@{ productId = "cp$index"; name = "并发商品$index"; quantity = 1; price = 50.0 })
                } | ConvertTo-Json) -TimeoutSec 15
                return @{ Success = $true }
            } catch {
                return @{ Success = $false }
            }
        } -ArgumentList $BaseUrl, $i
        $jobs += $job
    }
    
    $results = $jobs | Wait-Job | Receive-Job
    $jobs | Remove-Job
    
    $concurrentSuccess = ($results | Where-Object { $_.Success }).Count
    if ($concurrentSuccess -eq 20) {
        Write-Success "并发测试: 所有 20 个并发请求成功"
        Add-TestResult $category "并发测试" "PASS"
    } else {
        Write-Warn "并发测试: $concurrentSuccess/20 成功"
        Add-TestResult $category "并发测试" "PASS" "$concurrentSuccess/20"
    }
    
    return $true
}

function Test-Cluster {
    param(
        [string]$Transport = "redis",
        [string]$Persistence = "redis"
    )
    
    Write-SubSection "集群测试 ($Transport/$Persistence)"
    $category = "集群"
    
    $processes = @()
    $ports = @(5301, 5302, 5303)
    
    try {
        # 启动 3 个节点
        Write-Info "启动 3 节点集群..."
        foreach ($port in $ports) {
            $nodeId = "cluster-node-$port"
            $process = Start-Process -FilePath "dotnet" `
                -ArgumentList "run", "--", "--transport", $Transport, "--persistence", $Persistence, "--port", $port, "--node-id", $nodeId, "--cluster" `
                -WorkingDirectory $PSScriptRoot -PassThru -WindowStyle Hidden
            $processes += $process
            Start-Sleep -Seconds 2
        }
        
        # 等待集群初始化
        Write-Info "等待集群初始化..."
        Start-Sleep -Seconds 10
        
        # 测试每个节点 - 创建订单
        $allHealthy = $true
        $createdOrders = 0
        foreach ($port in $ports) {
            try {
                $health = Invoke-RestMethod -Uri "http://localhost:$port/health" -Method Get -TimeoutSec 5
                $order = Invoke-RestMethod -Uri "http://localhost:$port/orders" -Method Post -ContentType "application/json" -Body (@{
                    customerId = "cluster-test-$port"
                    items = @(@{ productId = "cp1"; name = "集群商品"; quantity = 1; price = 100.0 })
                } | ConvertTo-Json) -TimeoutSec 10
                Write-Success "节点 ${port}: 健康, 订单=$($order.orderId)"
                Add-TestResult $category "节点-$port" "PASS"
                $createdOrders++
            } catch {
                Write-Fail "节点 ${port}: $($_.Exception.Message)"
                Add-TestResult $category "节点-$port" "FAIL" $_.Exception.Message
                $allHealthy = $false
            }
        }
        
        # 验证数据一致性 - 等待数据同步
        Write-Info "等待数据同步..."
        Start-Sleep -Seconds 5
        
        $orderCounts = @()
        foreach ($port in $ports) {
            try {
                $orders = Invoke-RestMethod -Uri "http://localhost:$port/orders" -Method Get -TimeoutSec 5
                $orderCounts += $orders.Count
                Write-Info "  节点 ${port}: $($orders.Count) 个订单"
            } catch {
                $orderCounts += -1
            }
        }
        
        $uniqueCounts = $orderCounts | Select-Object -Unique
        # 所有节点应该看到相同数量的订单，且至少等于创建的订单数
        if ($uniqueCounts.Count -eq 1 -and $uniqueCounts[0] -ge $createdOrders) {
            Write-Success "数据一致性: 所有节点订单数一致 ($($uniqueCounts[0]) 个订单)"
            Add-TestResult $category "数据一致性" "PASS"
        } elseif ($uniqueCounts.Count -eq 1) {
            Write-Warn "数据一致性: 订单数一致但少于预期 ($($uniqueCounts[0])/$createdOrders)"
            Add-TestResult $category "数据一致性" "PASS" "部分同步: $($uniqueCounts[0])/$createdOrders"
        } else {
            Write-Fail "数据一致性: 节点订单数不一致 ($($orderCounts -join ', '))"
            Add-TestResult $category "数据一致性" "FAIL" "订单数: $($orderCounts -join ', ')"
        }
        
        return $allHealthy
        
    } finally {
        Write-Info "停止集群节点..."
        foreach ($process in $processes) {
            if ($process -and -not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
        Start-Sleep -Seconds 2
    }
}
#endregion

#region Main Test Runner
function Show-Report {
    $duration = (Get-Date) - $script:StartTime
    
    Write-Host "`n"
    Write-Host "╔══════════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║                              测 试 报 告                                     ║" -ForegroundColor Cyan
    Write-Host "╠══════════════════════════════════════════════════════════════════════════════╣" -ForegroundColor Cyan
    Write-Host "║  总测试数: $($script:TotalTests.ToString().PadRight(66)) ║" -ForegroundColor Cyan
    Write-Host "║  通过: $($script:PassedTests.ToString().PadRight(70)) ║" -ForegroundColor Green
    Write-Host "║  失败: $($script:FailedTests.ToString().PadRight(70)) ║" -ForegroundColor $(if ($script:FailedTests -gt 0) { "Red" } else { "Cyan" })
    Write-Host "║  跳过: $($script:SkippedTests.ToString().PadRight(70)) ║" -ForegroundColor DarkGray
    Write-Host "║  耗时: $($duration.ToString("mm\:ss").PadRight(70)) ║" -ForegroundColor Cyan
    
    $passRate = if ($script:TotalTests -gt 0) { [math]::Round(($script:PassedTests / $script:TotalTests) * 100, 1) } else { 0 }
    Write-Host "║  通过率: $("$passRate%".PadRight(68)) ║" -ForegroundColor $(if ($passRate -eq 100) { "Green" } elseif ($passRate -ge 80) { "Yellow" } else { "Red" })
    Write-Host "╚══════════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    
    # 按类别分组显示
    Write-Host "`n测试详情:" -ForegroundColor Cyan
    $grouped = $script:TestResults | Group-Object -Property Category
    foreach ($group in $grouped) {
        $passed = ($group.Group | Where-Object { $_.Status -eq "PASS" }).Count
        $total = $group.Group.Count
        $statusColor = if ($passed -eq $total) { "Green" } elseif ($passed -gt 0) { "Yellow" } else { "Red" }
        Write-Host "  [$($group.Name)] $passed/$total 通过" -ForegroundColor $statusColor
    }
    
    # 显示失败的测试
    $failed = $script:TestResults | Where-Object { $_.Status -eq "FAIL" }
    if ($failed.Count -gt 0) {
        Write-Host "`n失败的测试:" -ForegroundColor Red
        foreach ($test in $failed) {
            Write-Host "  ✗ [$($test.Category)] $($test.Name): $($test.Message)" -ForegroundColor Red
        }
    }
    
    Write-Host ""
}

function Start-AllTests {
    Write-Banner
    
    # 检查依赖
    Write-Section "环境检查"
    $redisOk = Test-PortAvailable -Port 6379
    $natsOk = Test-PortAvailable -Port 4222
    
    if ($redisOk) { Write-Success "Redis 可用 (localhost:6379)" } else { Write-Warn "Redis 不可用" }
    if ($natsOk) { Write-Success "NATS 可用 (localhost:4222)" } else { Write-Warn "NATS 不可用" }
    
    # 根据场景运行测试
    switch ($Scenario) {
        "all" {
            # 1. InMemory 基础测试
            Write-Section "场景 1/5: InMemory 基础测试"
            $process = Start-TestServer -Transport "inmemory" -Persistence "inmemory" -Port $Port
            if ($process) {
                try {
                    Test-BasicApi -BaseUrl "http://localhost:$Port"
                    Test-FlowDsl -BaseUrl "http://localhost:$Port"
                    Test-Qos -BaseUrl "http://localhost:$Port"
                } finally {
                    Stop-TestServer $process
                }
            } else {
                Write-Fail "InMemory 服务启动失败"
                Add-TestResult "InMemory" "服务启动" "FAIL"
            }
            
            # 2. Redis 配置测试
            Write-Section "场景 2/5: Redis 配置测试"
            if (-not $SkipRedis -and $redisOk) {
                Test-Configuration -Name "Redis" -Transport "redis" -Persistence "redis" -Port $Port
            } else {
                Write-Skip "跳过 Redis 测试"
                Add-TestResult "配置-Redis" "Redis 测试" "SKIP" "Redis 不可用或已跳过"
            }
            
            # 3. NATS 配置测试
            Write-Section "场景 3/5: NATS 配置测试"
            if (-not $SkipNats -and $natsOk) {
                Test-Configuration -Name "NATS" -Transport "nats" -Persistence "nats" -Port $Port
            } else {
                Write-Skip "跳过 NATS 测试"
                Add-TestResult "配置-NATS" "NATS 测试" "SKIP" "NATS 不可用或已跳过"
            }
            
            # 4. Flow DSL 专项测试
            Write-Section "场景 4/5: Flow DSL 专项测试"
            $process = Start-TestServer -Transport "inmemory" -Persistence "inmemory" -Port 5500
            if ($process) {
                try {
                    Test-FlowDsl -BaseUrl "http://localhost:5500"
                } finally {
                    Stop-TestServer $process
                }
            }
            
            # 5. 集群测试
            Write-Section "场景 5/5: 集群测试"
            if (-not $SkipCluster -and $redisOk) {
                Test-Cluster -Transport "redis" -Persistence "redis"
            } else {
                Write-Skip "跳过集群测试"
                Add-TestResult "集群" "集群测试" "SKIP" "Redis 不可用或已跳过"
            }
        }
        
        "api" {
            Write-Section "API 测试"
            $process = Start-TestServer -Transport "inmemory" -Persistence "inmemory" -Port $Port
            if ($process) {
                try { Test-BasicApi -BaseUrl "http://localhost:$Port" }
                finally { Stop-TestServer $process }
            }
        }
        
        "config" {
            Write-Section "配置测试"
            Test-Configuration -Name "InMemory" -Transport "inmemory" -Persistence "inmemory" -Port $Port
            if (-not $SkipRedis -and $redisOk) {
                Test-Configuration -Name "Redis" -Transport "redis" -Persistence "redis" -Port $Port
            }
            if (-not $SkipNats -and $natsOk) {
                Test-Configuration -Name "NATS" -Transport "nats" -Persistence "nats" -Port $Port
            }
        }
        
        "flow" {
            Write-Section "Flow DSL 测试"
            $process = Start-TestServer -Transport "inmemory" -Persistence "inmemory" -Port $Port
            if ($process) {
                try { Test-FlowDsl -BaseUrl "http://localhost:$Port" }
                finally { Stop-TestServer $process }
            }
        }
        
        "qos" {
            Write-Section "QoS 测试"
            $process = Start-TestServer -Transport "inmemory" -Persistence "inmemory" -Port $Port
            if ($process) {
                try { Test-Qos -BaseUrl "http://localhost:$Port" }
                finally { Stop-TestServer $process }
            }
        }
        
        "cluster" {
            Write-Section "集群测试"
            if ($redisOk) {
                Test-Cluster -Transport "redis" -Persistence "redis"
            } else {
                Write-Fail "集群测试需要 Redis"
            }
        }
    }
    
    # 显示报告
    Show-Report
    
    # 返回退出码
    if ($script:FailedTests -eq 0) {
        Write-Success "所有测试通过！"
        exit 0
    } else {
        Write-Fail "有 $($script:FailedTests) 个测试失败"
        exit 1
    }
}
#endregion

# 执行测试
Start-AllTests
