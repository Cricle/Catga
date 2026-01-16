#!/usr/bin/env pwsh
<#
.SYNOPSIS
    OrderSystem 全配置自动化测试脚本
.DESCRIPTION
    测试所有配置组合：InMemory、Redis、NATS，以及集群模式
.PARAMETER SkipInMemory
    跳过 InMemory 测试
.PARAMETER SkipRedis
    跳过 Redis 测试
.PARAMETER SkipNats
    跳过 NATS 测试
.PARAMETER SkipCluster
    跳过集群测试
.EXAMPLE
    .\test-all-configurations.ps1
.EXAMPLE
    .\test-all-configurations.ps1 -SkipCluster
#>

param(
    [switch]$SkipInMemory,
    [switch]$SkipRedis,
    [switch]$SkipNats,
    [switch]$SkipCluster
)

# 颜色输出函数
function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Error-Custom {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ $Message" -ForegroundColor Cyan
}

function Write-Warning-Custom {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor Yellow
}

function Write-Section {
    param([string]$Message)
    Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║ $($Message.PadRight(60)) ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
}

# 测试结果统计
$script:TotalConfigurations = 0
$script:PassedConfigurations = 0
$script:FailedConfigurations = 0
$script:ConfigurationResults = @()

# 检查依赖服务
function Test-RedisAvailability {
    Write-Info "检查 Redis 可用性..."
    try {
        $result = Test-NetConnection -ComputerName localhost -Port 6379 -WarningAction SilentlyContinue -ErrorAction Stop
        if ($result.TcpTestSucceeded) {
            Write-Success "Redis 正在运行 (localhost:6379)"
            return $true
        }
    } catch {
        Write-Warning-Custom "Redis 不可用: $($_.Exception.Message)"
        Write-Info "启动 Redis: docker run -d -p 6379:6379 redis:latest"
        return $false
    }
    return $false
}

function Test-NatsAvailability {
    Write-Info "检查 NATS 可用性..."
    try {
        $result = Test-NetConnection -ComputerName localhost -Port 4222 -WarningAction SilentlyContinue -ErrorAction Stop
        if ($result.TcpTestSucceeded) {
            Write-Success "NATS 正在运行 (localhost:4222)"
            return $true
        }
    } catch {
        Write-Warning-Custom "NATS 不可用: $($_.Exception.Message)"
        Write-Info "启动 NATS: docker run -d -p 4222:4222 nats:latest"
        return $false
    }
    return $false
}

# 启动服务
function Start-OrderSystem {
    param(
        [string]$Transport,
        [string]$Persistence,
        [int]$Port = 5000,
        [string]$NodeId = "test-node",
        [switch]$Cluster
    )
    
    $args = @(
        "run",
        "--",
        "--transport", $Transport,
        "--persistence", $Persistence,
        "--port", $Port,
        "--node-id", $NodeId
    )
    
    if ($Cluster) {
        $args += "--cluster"
    }
    
    Write-Info "启动服务: dotnet $($args -join ' ')"
    
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList $args `
        -WorkingDirectory "." `
        -PassThru `
        -NoNewWindow `
        -RedirectStandardOutput ".\logs\$NodeId-stdout.log" `
        -RedirectStandardError ".\logs\$NodeId-stderr.log"
    
    # 等待服务启动
    Write-Info "等待服务启动..."
    Start-Sleep -Seconds 5
    
    # 检查服务是否启动成功
    $maxRetries = 10
    $retryCount = 0
    $isRunning = $false
    
    while ($retryCount -lt $maxRetries -and -not $isRunning) {
        try {
            $response = Invoke-RestMethod -Uri "http://localhost:$Port/" -Method Get -TimeoutSec 2 -ErrorAction Stop
            $isRunning = $true
            Write-Success "服务启动成功 (PID: $($process.Id))"
        } catch {
            $retryCount++
            Write-Info "等待服务响应... ($retryCount/$maxRetries)"
            Start-Sleep -Seconds 2
        }
    }
    
    if (-not $isRunning) {
        Write-Error-Custom "服务启动失败"
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        return $null
    }
    
    return $process
}

# 停止服务
function Stop-OrderSystem {
    param($Process)
    
    if ($Process -and -not $Process.HasExited) {
        Write-Info "停止服务 (PID: $($Process.Id))..."
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Write-Success "服务已停止"
    }
}

# 运行测试脚本
function Invoke-ApiTests {
    param([int]$Port = 5000)
    
    Write-Info "运行 API 测试..."
    
    try {
        $result = & .\test-api.ps1 -BaseUrl "http://localhost:$Port" -ErrorAction Stop
        return $LASTEXITCODE -eq 0
    } catch {
        Write-Error-Custom "测试执行失败: $($_.Exception.Message)"
        return $false
    }
}

# 测试单个配置
function Test-Configuration {
    param(
        [string]$Name,
        [string]$Transport,
        [string]$Persistence,
        [int]$Port = 5000,
        [switch]$Cluster
    )
    
    $script:TotalConfigurations++
    
    Write-Section "测试配置 #$script:TotalConfigurations : $Name"
    Write-Info "Transport: $Transport, Persistence: $Persistence, Port: $Port, Cluster: $Cluster"
    
    # 启动服务
    $process = Start-OrderSystem -Transport $Transport -Persistence $Persistence -Port $Port -NodeId "test-$script:TotalConfigurations" -Cluster:$Cluster
    
    if (-not $process) {
        $script:FailedConfigurations++
        $script:ConfigurationResults += [PSCustomObject]@{
            Name = $Name
            Status = "FAIL"
            Transport = $Transport
            Persistence = $Persistence
            Cluster = $Cluster
            Message = "服务启动失败"
        }
        return
    }
    
    try {
        # 运行测试
        $testPassed = Invoke-ApiTests -Port $Port
        
        if ($testPassed) {
            $script:PassedConfigurations++
            Write-Success "配置测试通过: $Name"
            $script:ConfigurationResults += [PSCustomObject]@{
                Name = $Name
                Status = "PASS"
                Transport = $Transport
                Persistence = $Persistence
                Cluster = $Cluster
                Message = "所有测试通过"
            }
        } else {
            $script:FailedConfigurations++
            Write-Error-Custom "配置测试失败: $Name"
            $script:ConfigurationResults += [PSCustomObject]@{
                Name = $Name
                Status = "FAIL"
                Transport = $Transport
                Persistence = $Persistence
                Cluster = $Cluster
                Message = "API 测试失败"
            }
        }
    } finally {
        # 停止服务
        Stop-OrderSystem -Process $process
    }
    
    # 清理间隔
    Start-Sleep -Seconds 2
}

# 测试集群配置
function Test-ClusterConfiguration {
    param(
        [string]$Name,
        [string]$Transport,
        [string]$Persistence
    )
    
    $script:TotalConfigurations++
    
    Write-Section "测试集群配置 #$script:TotalConfigurations : $Name"
    Write-Info "Transport: $Transport, Persistence: $Persistence, Nodes: 3"
    
    $processes = @()
    
    try {
        # 启动 3 个节点
        Write-Info "启动节点 1 (端口 5001)..."
        $process1 = Start-OrderSystem -Transport $Transport -Persistence $Persistence -Port 5001 -NodeId "cluster-node-1" -Cluster
        if ($process1) { $processes += $process1 }
        
        Write-Info "启动节点 2 (端口 5002)..."
        $process2 = Start-OrderSystem -Transport $Transport -Persistence $Persistence -Port 5002 -NodeId "cluster-node-2" -Cluster
        if ($process2) { $processes += $process2 }
        
        Write-Info "启动节点 3 (端口 5003)..."
        $process3 = Start-OrderSystem -Transport $Transport -Persistence $Persistence -Port 5003 -NodeId "cluster-node-3" -Cluster
        if ($process3) { $processes += $process3 }
        
        if ($processes.Count -ne 3) {
            throw "无法启动所有集群节点"
        }
        
        # 等待集群选举
        Write-Info "等待集群选举完成..."
        Start-Sleep -Seconds 10
        
        # 测试每个节点
        $allPassed = $true
        foreach ($port in @(5001, 5002, 5003)) {
            Write-Info "测试节点 (端口 $port)..."
            $testPassed = Invoke-ApiTests -Port $port
            if (-not $testPassed) {
                $allPassed = $false
                Write-Warning-Custom "节点 $port 测试失败"
            }
        }
        
        if ($allPassed) {
            $script:PassedConfigurations++
            Write-Success "集群配置测试通过: $Name"
            $script:ConfigurationResults += [PSCustomObject]@{
                Name = $Name
                Status = "PASS"
                Transport = $Transport
                Persistence = $Persistence
                Cluster = $true
                Message = "所有节点测试通过"
            }
        } else {
            $script:FailedConfigurations++
            Write-Error-Custom "集群配置测试失败: $Name"
            $script:ConfigurationResults += [PSCustomObject]@{
                Name = $Name
                Status = "FAIL"
                Transport = $Transport
                Persistence = $Persistence
                Cluster = $true
                Message = "部分节点测试失败"
            }
        }
    } catch {
        $script:FailedConfigurations++
        Write-Error-Custom "集群测试失败: $($_.Exception.Message)"
        $script:ConfigurationResults += [PSCustomObject]@{
            Name = $Name
            Status = "FAIL"
            Transport = $Transport
            Persistence = $Persistence
            Cluster = $true
            Message = $_.Exception.Message
        }
    } finally {
        # 停止所有节点
        foreach ($process in $processes) {
            Stop-OrderSystem -Process $process
        }
    }
    
    # 清理间隔
    Start-Sleep -Seconds 3
}

# 打印横幅
function Show-Banner {
    Write-Host @"

╔══════════════════════════════════════════════════════════════╗
║       Catga OrderSystem - 全配置自动化测试                   ║
╠══════════════════════════════════════════════════════════════╣
║  时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")                        ║
╚══════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan
}

# 打印测试报告
function Show-Report {
    $passRate = if ($script:TotalConfigurations -gt 0) { 
        [math]::Round(($script:PassedConfigurations / $script:TotalConfigurations) * 100, 2) 
    } else { 
        0 
    }
    
    Write-Host "`n`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║                      测试报告                                ║" -ForegroundColor Cyan
    Write-Host "╠══════════════════════════════════════════════════════════════╣" -ForegroundColor Cyan
    Write-Host "║  总配置数: $($script:TotalConfigurations.ToString().PadRight(48)) ║" -ForegroundColor Cyan
    Write-Host "║  通过: $($script:PassedConfigurations.ToString().PadRight(52)) ║" -ForegroundColor Green
    Write-Host "║  失败: $($script:FailedConfigurations.ToString().PadRight(52)) ║" -ForegroundColor $(if ($script:FailedConfigurations -gt 0) { "Red" } else { "Cyan" })
    Write-Host "║  通过率: $($passRate.ToString() + "%")".PadRight(50) "║" -ForegroundColor $(if ($passRate -eq 100) { "Green" } elseif ($passRate -ge 80) { "Yellow" } else { "Red" })
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    
    Write-Host "`n配置测试结果:" -ForegroundColor Cyan
    $script:ConfigurationResults | Format-Table -Property Name, Status, Transport, Persistence, Cluster, Message -AutoSize
    
    if ($script:FailedConfigurations -gt 0) {
        Write-Host "`n失败的配置:" -ForegroundColor Red
        $script:ConfigurationResults | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
            Write-Host "  • $($_.Name): $($_.Message)" -ForegroundColor Red
        }
    }
    
    Write-Host ""
}

# 主测试流程
function Start-Tests {
    Show-Banner
    
    # 创建日志目录
    if (-not (Test-Path ".\logs")) {
        New-Item -ItemType Directory -Path ".\logs" | Out-Null
    }
    
    # 检查依赖
    $redisAvailable = Test-RedisAvailability
    $natsAvailable = Test-NatsAvailability
    
    Write-Host "`n开始执行配置测试..." -ForegroundColor Cyan
    Write-Host "=" * 64
    
    # 1. InMemory 配置测试
    if (-not $SkipInMemory) {
        Test-Configuration -Name "InMemory (Standalone)" -Transport "inmemory" -Persistence "inmemory" -Port 5000
    }
    
    # 2. Redis 配置测试
    if (-not $SkipRedis -and $redisAvailable) {
        Test-Configuration -Name "Redis Transport + InMemory Persistence" -Transport "redis" -Persistence "inmemory" -Port 5000
        Test-Configuration -Name "InMemory Transport + Redis Persistence" -Transport "inmemory" -Persistence "redis" -Port 5000
        Test-Configuration -Name "Redis (Full Stack)" -Transport "redis" -Persistence "redis" -Port 5000
    } elseif (-not $SkipRedis) {
        Write-Warning-Custom "跳过 Redis 测试 (服务不可用)"
    }
    
    # 3. NATS 配置测试
    if (-not $SkipNats -and $natsAvailable) {
        Test-Configuration -Name "NATS Transport + InMemory Persistence" -Transport "nats" -Persistence "inmemory" -Port 5000
        Test-Configuration -Name "InMemory Transport + NATS Persistence" -Transport "inmemory" -Persistence "nats" -Port 5000
        Test-Configuration -Name "NATS (Full Stack)" -Transport "nats" -Persistence "nats" -Port 5000
    } elseif (-not $SkipNats) {
        Write-Warning-Custom "跳过 NATS 测试 (服务不可用)"
    }
    
    # 4. 集群配置测试
    if (-not $SkipCluster) {
        if ($redisAvailable) {
            Test-ClusterConfiguration -Name "Redis Cluster (3 Nodes)" -Transport "redis" -Persistence "redis"
        } else {
            Write-Warning-Custom "跳过 Redis 集群测试 (服务不可用)"
        }
        
        if ($natsAvailable) {
            Test-ClusterConfiguration -Name "NATS Cluster (3 Nodes)" -Transport "nats" -Persistence "nats"
        } else {
            Write-Warning-Custom "跳过 NATS 集群测试 (服务不可用)"
        }
    }
    
    # 显示测试报告
    Show-Report
    
    # 返回退出码
    if ($script:FailedConfigurations -gt 0) {
        exit 1
    } else {
        Write-Success "`n所有配置测试通过！系统运行正常。"
        exit 0
    }
}

# 执行测试
Start-Tests
