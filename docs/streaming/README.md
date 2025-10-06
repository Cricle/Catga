# 🌊 实时流处理

Catga 提供简洁而强大的流处理能力，用于处理持续的数据流。

---

## 📋 目录

- [核心概念](#核心概念)
- [快速开始](#快速开始)
- [管道操作](#管道操作)
- [实际应用](#实际应用)
- [性能优化](#性能优化)
- [最佳实践](#最佳实践)

---

## 核心概念

### 什么是流处理？

流处理是一种处理持续数据流的编程范式，数据源源不断地到来，我们需要实时处理。

### Catga 的流处理特点

✅ **声明式 API** - 链式调用，代码简洁
✅ **异步优先** - 基于 `IAsyncEnumerable<T>`
✅ **零分配** - 流式处理，不缓存所有数据
✅ **组合性强** - 操作符可自由组合
✅ **平台无关** - 纯 .NET 实现

---

## 快速开始

### 基础示例

```csharp
using Catga.Streaming;

// 数据源
async IAsyncEnumerable<int> GetNumbersAsync()
{
    for (int i = 1; i <= 100; i++)
    {
        await Task.Delay(100);
        yield return i;
    }
}

// 流处理管道
var results = StreamProcessor
    .From(GetNumbersAsync())
    .Where(n => n % 2 == 0)       // 过滤：只要偶数
    .Select(n => n * 2)            // 转换：乘以 2
    .Batch(10)                     // 批处理：每 10 个一批
    .Do(batch => Console.WriteLine($"处理了 {batch.Count} 个数字"));

// 执行管道
await foreach (var batch in results.ExecuteAsync())
{
    // 批次已处理
}
```

---

## 管道操作

### 过滤操作

#### Where - 条件过滤

```csharp
var filtered = StreamProcessor
    .From(dataStream)
    .Where(item => item.Price > 100)  // 只保留价格 > 100 的
    .Where(item => item.Category == "Electronics");  // 可以链式调用
```

#### Distinct - 去重

```csharp
var unique = StreamProcessor
    .From(messageStream)
    .Distinct(msg => msg.Id);  // 基于 ID 去重
```

### 转换操作

#### Select - 同步转换

```csharp
var transformed = StreamProcessor
    .From(orderStream)
    .Select(order => new OrderDto
    {
        Id = order.Id,
        Total = order.Items.Sum(i => i.Price)
    });
```

#### SelectAsync - 异步转换

```csharp
var enriched = StreamProcessor
    .From(userStream)
    .SelectAsync(async user =>
    {
        var profile = await _database.GetProfileAsync(user.Id);
        return new { User = user, Profile = profile };
    });
```

### 批处理操作

#### Batch - 按数量批处理

```csharp
var batches = StreamProcessor
    .From(dataStream)
    .Batch(100);  // 每 100 个一批

await foreach (var batch in batches.ExecuteAsync())
{
    // 批量写入数据库
    await _database.BulkInsertAsync(batch);
}
```

#### Batch with Timeout - 按数量或超时

```csharp
var batches = StreamProcessor
    .From(eventStream)
    .Batch(batchSize: 50, timeout: TimeSpan.FromSeconds(5));
    // 满 50 个或 5 秒超时，哪个先到就触发
```

### 时间窗口操作

#### Window - 时间窗口

```csharp
var windows = StreamProcessor
    .From(metricsStream)
    .Window(TimeSpan.FromMinutes(1))  // 1 分钟时间窗口
    .Select(window => new
    {
        Count = window.Count,
        Average = window.Average(m => m.Value),
        Max = window.Max(m => m.Value),
        Timestamp = DateTime.UtcNow
    });
```

### 限流操作

#### Throttle - 控制速率

```csharp
var throttled = StreamProcessor
    .From(fastDataStream)
    .Throttle(100);  // 每秒最多 100 个
```

#### Delay - 延迟处理

```csharp
var delayed = StreamProcessor
    .From(dataStream)
    .Delay(TimeSpan.FromMilliseconds(100));  // 每个元素延迟 100ms
```

### 副作用操作

#### Do - 执行同步操作

```csharp
var logged = StreamProcessor
    .From(dataStream)
    .Do(item => _logger.LogInformation("Processing {Item}", item))
    .Do(item => _metrics.Increment("items_processed"));
```

#### DoAsync - 执行异步操作

```csharp
var notified = StreamProcessor
    .From(orderStream)
    .DoAsync(async order =>
    {
        await _emailService.SendConfirmationAsync(order.Email);
        await _smsService.SendNotificationAsync(order.Phone);
    });
```

### 并行处理

#### Parallel - 并行执行

```csharp
var parallel = StreamProcessor
    .From(dataStream)
    .Parallel(degreeOfParallelism: 4);  // 4 个并行任务
```

---

## 实际应用

### 1. 实时数据分析

```csharp
// 实时统计订单数据
var orderStats = StreamProcessor
    .From(orderStream)
    .Window(TimeSpan.FromSeconds(10))  // 10 秒窗口
    .Select(window => new OrderStatistics
    {
        TotalOrders = window.Count,
        TotalRevenue = window.Sum(o => o.Amount),
        AverageOrderValue = window.Average(o => o.Amount),
        TopProducts = window
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList(),
        Timestamp = DateTime.UtcNow
    })
    .DoAsync(async stats =>
    {
        // 发送到实时仪表板
        await _dashboard.UpdateAsync(stats);
    });

await foreach (var stats in orderStats.ExecuteAsync())
{
    Console.WriteLine($"[{stats.Timestamp}] 订单: {stats.TotalOrders}, 收入: ${stats.TotalRevenue}");
}
```

### 2. 异常检测

```csharp
// 实时检测异常行为
var anomalies = StreamProcessor
    .From(metricsStream)
    .Window(TimeSpan.FromMinutes(1))
    .Select(window => new
    {
        Average = window.Average(m => m.CpuUsage),
        StdDev = CalculateStdDev(window.Select(m => m.CpuUsage)),
        Max = window.Max(m => m.CpuUsage)
    })
    .Where(stats => stats.Max > stats.Average + 2 * stats.StdDev)  // 2 个标准差
    .DoAsync(async anomaly =>
    {
        await _alerting.SendAlertAsync($"CPU 使用率异常: {anomaly.Max}%");
    });
```

### 3. ETL 数据管道

```csharp
// Extract -> Transform -> Load
var etl = StreamProcessor
    .From(rawDataSource)
    // Extract
    .Select(raw => ParseRawData(raw))
    .Where(data => data != null)
    // Transform
    .SelectAsync(async data => await TransformDataAsync(data))
    .Where(data => ValidateData(data))
    // Load
    .Batch(1000)
    .DoAsync(async batch => await _database.BulkInsertAsync(batch));

await foreach (var batch in etl.ExecuteAsync())
{
    Console.WriteLine($"已加载 {batch.Count} 条数据");
}
```

### 4. 日志聚合

```csharp
// 聚合和分析日志
var logAnalysis = StreamProcessor
    .From(logStream)
    .Where(log => log.Level >= LogLevel.Warning)  // 只关注警告和错误
    .Window(TimeSpan.FromMinutes(5))  // 5 分钟窗口
    .Select(window => new LogSummary
    {
        WarningCount = window.Count(l => l.Level == LogLevel.Warning),
        ErrorCount = window.Count(l => l.Level == LogLevel.Error),
        TopErrors = window
            .Where(l => l.Level == LogLevel.Error)
            .GroupBy(l => l.Message)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new { Message = g.Key, Count = g.Count() })
            .ToList()
    })
    .Where(summary => summary.ErrorCount > 10)  // 错误数超过 10
    .DoAsync(async summary =>
    {
        await _monitoring.SendAlertAsync($"错误激增: {summary.ErrorCount} 个错误");
    });
```

### 5. 消息队列消费

```csharp
// 从消息队列消费并处理
var messageProcessing = StreamProcessor
    .From(messageQueue.ConsumeAsync())
    .Distinct(msg => msg.MessageId)  // 去重
    .SelectAsync(async msg =>
    {
        // 业务处理
        var result = await _handler.HandleAsync(msg);
        return new { Message = msg, Result = result };
    })
    .Where(x => x.Result.IsSuccess)
    .Batch(50)
    .DoAsync(async batch =>
    {
        // 批量确认
        var messageIds = batch.Select(x => x.Message.MessageId).ToArray();
        await messageQueue.AcknowledgeAsync(messageIds);
    });
```

### 6. 实时推荐

```csharp
// 实时推荐系统
var recommendations = StreamProcessor
    .From(userActivityStream)
    .Batch(100, TimeSpan.FromSeconds(10))
    .SelectAsync(async batch =>
    {
        // 批量获取用户特征
        var userFeatures = await _mlService.GetFeaturesAsync(batch);

        // 批量预测
        var predictions = await _mlService.PredictAsync(userFeatures);

        return batch.Zip(predictions, (activity, pred) => new
        {
            UserId = activity.UserId,
            RecommendedItems = pred.TopItems
        });
    })
    .SelectMany(batch => batch)
    .DoAsync(async rec =>
    {
        // 推送推荐
        await _pushService.SendRecommendationAsync(rec.UserId, rec.RecommendedItems);
    });
```

---

## 性能优化

### 1. 批处理优化吞吐量

```csharp
// 不好：逐个处理
await foreach (var item in dataStream)
{
    await _database.InsertAsync(item);  // 每次一个网络调用
}

// 好：批量处理
var batched = StreamProcessor
    .From(dataStream)
    .Batch(1000)  // 批量 1000 个
    .DoAsync(async batch => await _database.BulkInsertAsync(batch));
```

### 2. 并行处理

```csharp
// 不好：串行处理慢操作
var processed = StreamProcessor
    .From(dataStream)
    .SelectAsync(async item => await SlowOperationAsync(item));

// 好：并行处理
var processed = StreamProcessor
    .From(dataStream)
    .Parallel(degreeOfParallelism: Environment.ProcessorCount)
    .SelectAsync(async item => await SlowOperationAsync(item));
```

### 3. 限流保护下游

```csharp
// 保护下游服务
var throttled = StreamProcessor
    .From(dataStream)
    .Throttle(100)  // 限制每秒 100 个请求
    .SelectAsync(async item => await _downstreamApi.CallAsync(item));
```

### 4. 早期过滤

```csharp
// 不好：过滤在转换后
var processed = StreamProcessor
    .From(dataStream)
    .SelectAsync(async item => await ExpensiveTransformAsync(item))  // 昂贵操作
    .Where(item => item.IsValid);  // 过滤无效的

// 好：过滤在转换前
var processed = StreamProcessor
    .From(dataStream)
    .Where(item => QuickValidation(item))  // 快速过滤
    .SelectAsync(async item => await ExpensiveTransformAsync(item));
```

---

## 最佳实践

### 1. 使用流式处理而非缓存

```csharp
// ❌ 不好：缓存所有数据
var allData = await dataStream.ToListAsync();  // 内存爆炸
foreach (var item in allData)
{
    await ProcessAsync(item);
}

// ✅ 好：流式处理
await foreach (var item in dataStream)
{
    await ProcessAsync(item);
}
```

### 2. 合理使用批处理

```csharp
// 批大小选择
// - 太小：网络开销大
// - 太大：延迟高、内存压力大
// - 推荐：100-1000（根据实际测试调整）

var batches = StreamProcessor
    .From(dataStream)
    .Batch(500, TimeSpan.FromSeconds(5));  // 平衡吞吐量和延迟
```

### 3. 错误处理

```csharp
var resilient = StreamProcessor
    .From(dataStream)
    .SelectAsync(async item =>
    {
        try
        {
            return await ProcessAsync(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理失败: {Item}", item);
            return null;  // 或发送到死信队列
        }
    })
    .Where(result => result != null);
```

### 4. 取消支持

```csharp
var cts = new CancellationTokenSource();

var processing = Task.Run(async () =>
{
    await foreach (var item in pipeline.ExecuteAsync(cts.Token))
    {
        // 处理
    }
}, cts.Token);

// 优雅关闭
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await processing;
```

### 5. 监控和可观测性

```csharp
var monitored = StreamProcessor
    .From(dataStream)
    .Do(item => _metrics.Increment("items_received"))
    .SelectAsync(async item =>
    {
        var sw = Stopwatch.StartNew();
        var result = await ProcessAsync(item);
        _metrics.RecordHistogram("processing_duration_ms", sw.ElapsedMilliseconds);
        return result;
    })
    .Do(result => _metrics.Increment("items_processed"));
```

---

## 对比：Catga vs 其他框架

| 特性 | Catga | Rx.NET | Akka.Streams |
|------|-------|--------|--------------|
| **学习曲线** | 🟢 简单 | 🟡 中等 | 🔴 复杂 |
| **API 风格** | LINQ 风格 | Reactive | 图（Graph） |
| **异步** | ✅ 原生 | ✅ 支持 | ✅ 支持 |
| **零分配** | ✅ | ❌ | ❌ |
| **依赖** | 零依赖 | Rx.NET | Akka |
| **适用场景** | 通用流处理 | 响应式 | Actor 模型 |

---

## 总结

- ✅ **简洁** - LINQ 风格 API
- ✅ **高效** - 零分配流式处理
- ✅ **灵活** - 操作符自由组合
- ✅ **实用** - 解决实际问题

**适用场景**:
- 实时数据分析
- ETL 数据管道
- 消息队列消费
- 日志聚合
- 异常检测

