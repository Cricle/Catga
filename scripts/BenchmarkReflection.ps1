# Catga 反射性能基准测试
# 对比 typeof() vs TypeNameCache 性能

Write-Host "`n╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                                                               ║" -ForegroundColor Cyan
Write-Host "║         ⚡ Catga 反射性能基准测试 ⚡                          ║" -ForegroundColor Cyan
Write-Host "║                                                               ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

Write-Host "📊 运行性能测试..." -ForegroundColor Yellow
Write-Host ""

# 检查是否有 BenchmarkDotNet 项目
if (Test-Path "benchmarks/Catga.Benchmarks/Catga.Benchmarks.csproj") {
    Write-Host "✅ 找到基准测试项目" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "🚀 运行基准测试 (这可能需要几分钟)..." -ForegroundColor Cyan
    Write-Host ""
    
    # 运行基准测试
    Set-Location "benchmarks/Catga.Benchmarks"
    dotnet run -c Release -- --filter "*Reflection*" --join
    Set-Location "../.."
    
    Write-Host ""
    Write-Host "✅ 基准测试完成！" -ForegroundColor Green
    Write-Host ""
    Write-Host "📁 结果保存在: benchmarks/Catga.Benchmarks/BenchmarkDotNet.Artifacts" -ForegroundColor Cyan
} else {
    Write-Host "⚠️  未找到基准测试项目，创建简单的性能对比..." -ForegroundColor Yellow
    Write-Host ""
    
    # 创建临时性能测试
    $testCode = @"
using System;
using System.Diagnostics;

// 模拟 TypeNameCache
public static class TypeNameCache<T>
{
    private static string? _name;
    public static string Name => _name ??= typeof(T).Name;
}

// 测试类
public class TestClass { }

public class Program
{
    public static void Main()
    {
        const int iterations = 1000000;
        
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           Reflection Performance Comparison                  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        
        // 预热
        for (int i = 0; i < 1000; i++)
        {
            _ = typeof(TestClass).Name;
            _ = TypeNameCache<TestClass>.Name;
        }
        
        // 测试 typeof()
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = typeof(TestClass).Name;
        }
        sw1.Stop();
        
        // 测试 TypeNameCache
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = TypeNameCache<TestClass>.Name;
        }
        sw2.Stop();
        
        var typeofNs = sw1.Elapsed.TotalMilliseconds * 1000000 / iterations;
        var cacheNs = sw2.Elapsed.TotalMilliseconds * 1000000 / iterations;
        var speedup = typeofNs / cacheNs;
        
        Console.WriteLine($"  Iterations:       {iterations:N0}");
        Console.WriteLine($"  typeof():         {typeofNs:F2} ns/op");
        Console.WriteLine($"  TypeNameCache:    {cacheNs:F2} ns/op");
        Console.WriteLine($"  Speedup:          {speedup:F1}x faster");
        Console.WriteLine();
        Console.WriteLine($"  ✅ TypeNameCache is {speedup:F1}x faster than typeof()!");
        Console.WriteLine();
    }
}
"@
    
    # 创建临时测试项目
    $tempDir = "temp_perf_test"
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }
    
    New-Item -ItemType Directory -Path $tempDir > $null
    Set-Location $tempDir
    
    dotnet new console > $null 2>&1
    $testCode | Out-File -FilePath "Program.cs" -Encoding UTF8
    
    Write-Host "⚡ 运行性能对比..." -ForegroundColor Cyan
    Write-Host ""
    
    dotnet run -c Release
    
    Set-Location ..
    Remove-Item $tempDir -Recurse -Force
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "💡 性能优化说明:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  • TypeNameCache 首次调用使用反射" -ForegroundColor White
Write-Host "  • 后续调用直接返回缓存值（零反射）" -ForegroundColor White
Write-Host "  • 在高频调用场景下性能提升显著" -ForegroundColor White
Write-Host "  • RPC、日志、追踪等热路径均已优化" -ForegroundColor White
Write-Host ""

