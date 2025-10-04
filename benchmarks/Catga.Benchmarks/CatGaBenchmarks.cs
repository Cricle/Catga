using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Catga.CatGa.Core;
using Catga.CatGa.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CatCat.Benchmarks;

/// <summary>
/// CatGa 性能基准测试
/// 测试分布式事务的吞吐量和延迟
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, RuntimeMoniker.Net90, warmupCount: 3, iterationCount: 10)]
public class CatGaBenchmarks
{
    private ICatGaExecutor _executor = null!;
    private IServiceProvider _serviceProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 配置 CatGa
        services.AddCatGa(options =>
        {
            options.MaxRetryAttempts = 3;
            options.AutoCompensate = true;
            options.GlobalTimeout = TimeSpan.FromSeconds(30);
        });

        // 注册测试事务 - 必须在 BuildServiceProvider 之前
        services.AddCatGaTransaction<int, int, SimpleTransaction>();
        services.AddCatGaTransaction<ComplexRequest, ComplexResult, ComplexTransaction>();
        services.AddCatGaTransaction<int, int, IdempotentTransaction>();

        // 配置日志（使用 NullLogger 以避免日志开销）
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error));

        _serviceProvider = services.BuildServiceProvider();
        _executor = _serviceProvider.GetRequiredService<ICatGaExecutor>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    /// <summary>
    /// 单次简单事务
    /// </summary>
    [Benchmark(Description = "单次简单事务")]
    public async Task<CatGaResult<int>> ExecuteTransaction_Simple()
    {
        return await _executor.ExecuteAsync<int, int>(
            42,
            null,
            CancellationToken.None);
    }

    /// <summary>
    /// 单次复杂事务（带补偿）
    /// </summary>
    [Benchmark(Description = "单次复杂事务")]
    public async Task<CatGaResult<ComplexResult>> ExecuteTransaction_Complex()
    {
        var request = new ComplexRequest { Id = 1, Amount = 100 };
        return await _executor.ExecuteAsync<ComplexRequest, ComplexResult>(
            request,
            null,
            CancellationToken.None);
    }

    /// <summary>
    /// 批量简单事务 (100 个)
    /// </summary>
    [Benchmark(Description = "批量简单事务 (100)")]
    public async Task ExecuteTransaction_Batch100()
    {
        var tasks = new Task<CatGaResult<int>>[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = _executor.ExecuteAsync<int, int>(
                i,
                null,
                CancellationToken.None);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 高并发事务 (1000 个)
    /// </summary>
    [Benchmark(Description = "高并发事务 (1000)")]
    public async Task ExecuteTransaction_HighConcurrency1000()
    {
        var tasks = new Task<CatGaResult<int>>[1000];
        for (int i = 0; i < 1000; i++)
        {
            tasks[i] = _executor.ExecuteAsync<int, int>(
                i,
                null,
                CancellationToken.None);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 幂等性测试（重复执行同一事务）
    /// </summary>
    [Benchmark(Description = "幂等性测试 (100 次重复)")]
    public async Task ExecuteTransaction_Idempotency100()
    {
        var tasks = new Task<CatGaResult<int>>[100];
        var context = new CatGaContext { IdempotencyKey = "fixed-key" };

        // 使用相同的幂等性键
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = _executor.ExecuteAsync<int, int>(
                42,
                context,
                CancellationToken.None);
        }

        await Task.WhenAll(tasks);
    }
}

// 测试事务

public class SimpleTransaction : ICatGaTransaction<int, int>
{
    public Task<int> ExecuteAsync(int request, CancellationToken cancellationToken = default)
    {
        // 简单计算
        return Task.FromResult(request * 2);
    }

    public Task CompensateAsync(int request, CancellationToken cancellationToken = default)
    {
        // 无需补偿
        return Task.CompletedTask;
    }
}

public class ComplexTransaction : ICatGaTransaction<ComplexRequest, ComplexResult>
{
    public async Task<ComplexResult> ExecuteAsync(
        ComplexRequest request,
        CancellationToken cancellationToken = default)
    {
        // 模拟多步骤操作
        await Task.Delay(1, cancellationToken); // 模拟 I/O

        return new ComplexResult
        {
            Id = request.Id,
            FinalAmount = request.Amount * 1.1m,
            Status = "Success"
        };
    }

    public Task CompensateAsync(
        ComplexRequest request,
        CancellationToken cancellationToken = default)
    {
        // 模拟补偿逻辑
        return Task.CompletedTask;
    }
}

public class IdempotentTransaction : ICatGaTransaction<int, int>
{
    public Task<int> ExecuteAsync(int request, CancellationToken cancellationToken = default)
    {
        // 幂等操作
        return Task.FromResult(request);
    }

    public Task CompensateAsync(int request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public class ComplexRequest
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
}

public class ComplexResult
{
    public int Id { get; set; }
    public decimal FinalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}

