using Catga.Streaming;

// ============================================================
// Catga 实时流处理示例
// ============================================================

Console.WriteLine("🌊 Catga 实时流处理示例\n");

// ============================================================
// 示例 1: 基础流处理 - 过滤和转换
// ============================================================
Console.WriteLine("📊 示例 1: 基础流处理 - 过滤和转换");

var numbers = GenerateNumberStream(1, 20);

var results = StreamProcessor
    .From(numbers)
    .Where(n => n % 2 == 0)  // 只保留偶数
    .Select(n => n * 2)       // 乘以 2
    .Do(n => Console.WriteLine($"  处理: {n}"));

await foreach (var result in results.ExecuteAsync())
{
    // 消费结果
}

Console.WriteLine();

// ============================================================
// 示例 2: 批处理 - 批量写入数据库
// ============================================================
Console.WriteLine("📦 示例 2: 批处理 - 批量写入数据库");

var orders = GenerateOrderStream(50);

var batches = StreamProcessor
    .From(orders)
    .Batch(10, timeout: TimeSpan.FromSeconds(2))  // 每 10 个一批，或超时 2 秒
    .DoAsync(async batch =>
    {
        // 模拟批量写入数据库
        Console.WriteLine($"  💾 批量写入 {batch.Count} 个订单到数据库");
        await Task.Delay(100);
    });

await foreach (var batch in batches.ExecuteAsync())
{
    // 批次已处理
}

Console.WriteLine();

// ============================================================
// 示例 3: 窗口聚合 - 实时统计
// ============================================================
Console.WriteLine("📈 示例 3: 窗口聚合 - 实时统计");

var events = GenerateEventStream(100);

var windows = StreamProcessor
    .From(events)
    .Window(TimeSpan.FromSeconds(2))  // 2 秒时间窗口
    .Select(window => new
    {
        Count = window.Count,
        Sum = window.Sum(e => e.Value),
        Average = window.Average(e => e.Value),
        Timestamp = DateTime.UtcNow
    })
    .Do(stats => Console.WriteLine(
        $"  📊 窗口统计: 数量={stats.Count}, 总和={stats.Sum}, 平均={stats.Average:F2}, 时间={stats.Timestamp:HH:mm:ss}"));

await foreach (var window in windows.ExecuteAsync())
{
    // 窗口统计已处理
}

Console.WriteLine();

// ============================================================
// 示例 4: 去重 - 防止重复处理
// ============================================================
Console.WriteLine("🔍 示例 4: 去重 - 防止重复处理");

var messages = GenerateMessageStream(20);

var uniqueMessages = StreamProcessor
    .From(messages)
    .Distinct(m => m.Id)  // 基于 ID 去重
    .Do(m => Console.WriteLine($"  ✅ 唯一消息: {m.Id} - {m.Content}"));

await foreach (var msg in uniqueMessages.ExecuteAsync())
{
    // 唯一消息已处理
}

Console.WriteLine();

// ============================================================
// 示例 5: 限流 - 控制处理速率
// ============================================================
Console.WriteLine("⏱️ 示例 5: 限流 - 控制处理速率");

var fastData = GenerateFastDataStream(20);

var throttled = StreamProcessor
    .From(fastData)
    .Throttle(5)  // 每秒最多 5 个
    .Do(d => Console.WriteLine($"  🐌 限流处理: {d} - {DateTime.Now:HH:mm:ss.fff}"));

await foreach (var item in throttled.ExecuteAsync())
{
    // 限流处理
}

Console.WriteLine();

// ============================================================
// 示例 6: 组合管道 - 复杂数据处理
// ============================================================
Console.WriteLine("🔄 示例 6: 组合管道 - 复杂数据处理");

var transactions = GenerateTransactionStream(30);

var processedTransactions = StreamProcessor
    .From(transactions)
    .Where(t => t.Amount > 100)                    // 过滤：金额 > 100
    .SelectAsync(async t =>                         // 异步验证
    {
        await Task.Delay(10);  // 模拟验证
        return new { t.Id, t.Amount, Verified = true };
    })
    .Batch(5)                                       // 批量处理
    .DoAsync(async batch =>
    {
        // 批量发送通知
        Console.WriteLine($"  📧 发送 {batch.Count} 个交易通知");
        await Task.Delay(50);
    });

await foreach (var batch in processedTransactions.ExecuteAsync())
{
    // 交易已处理
}

Console.WriteLine();

// ============================================================
// 示例 7: 实时监控 - 异常检测
// ============================================================
Console.WriteLine("🚨 示例 7: 实时监控 - 异常检测");

var metrics = GenerateMetricStream(50);

var anomalies = StreamProcessor
    .From(metrics)
    .Window(TimeSpan.FromSeconds(1))
    .Select(window =>
    {
        var avg = window.Average(m => m.CpuUsage);
        var max = window.Max(m => m.CpuUsage);
        return new { Average = avg, Max = max, IsAnomaly = max > 90 };
    })
    .Where(stat => stat.IsAnomaly)
    .Do(anomaly => Console.WriteLine(
        $"  ⚠️  异常检测: CPU 使用率过高! 平均={anomaly.Average:F2}%, 最大={anomaly.Max}%"));

await foreach (var anomaly in anomalies.ExecuteAsync())
{
    // 异常已检测
}

Console.WriteLine("\n✅ 所有示例完成！");

// ============================================================
// 辅助方法
// ============================================================

static async IAsyncEnumerable<int> GenerateNumberStream(int start, int count)
{
    for (int i = start; i < start + count; i++)
    {
        await Task.Delay(50);
        yield return i;
    }
}

static async IAsyncEnumerable<Order> GenerateOrderStream(int count)
{
    for (int i = 1; i <= count; i++)
    {
        await Task.Delay(100);
        yield return new Order(i, $"Order-{i}", Random.Shared.Next(50, 500));
    }
}

static async IAsyncEnumerable<Event> GenerateEventStream(int count)
{
    for (int i = 0; i < count; i++)
    {
        await Task.Delay(100);
        yield return new Event($"Event-{i}", Random.Shared.Next(1, 100));
    }
}

static async IAsyncEnumerable<Message> GenerateMessageStream(int count)
{
    var ids = new[] { "A", "B", "C", "D", "A", "B", "A", "E", "C", "A" }; // 有重复
    for (int i = 0; i < Math.Min(count, ids.Length * 2); i++)
    {
        await Task.Delay(50);
        var id = ids[i % ids.Length];
        yield return new Message(id, $"Content-{i}");
    }
}

static async IAsyncEnumerable<string> GenerateFastDataStream(int count)
{
    for (int i = 0; i < count; i++)
    {
        // 快速生成数据（不延迟）
        yield return $"Data-{i}";
        await Task.Yield();
    }
}

static async IAsyncEnumerable<Transaction> GenerateTransactionStream(int count)
{
    for (int i = 1; i <= count; i++)
    {
        await Task.Delay(80);
        yield return new Transaction(i, Random.Shared.Next(50, 500));
    }
}

static async IAsyncEnumerable<Metric> GenerateMetricStream(int count)
{
    for (int i = 0; i < count; i++)
    {
        await Task.Delay(100);
        // 模拟 CPU 使用率（偶尔异常）
        var cpu = Random.Shared.Next(0, 100);
        if (i % 10 == 0) cpu = Random.Shared.Next(85, 100); // 10% 异常
        yield return new Metric(cpu);
    }
}

// ============================================================
// 数据模型
// ============================================================

record Order(int Id, string Name, decimal Amount);
record Event(string Name, int Value);
record Message(string Id, string Content);
record Transaction(int Id, decimal Amount);
record Metric(int CpuUsage);

