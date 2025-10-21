# ValueTask vs Task 使用指南

## 🎯 核心原则

### ✅ 使用 `ValueTask<T>` 的场景

1. **同步完成的可能性高**（>50%）
   ```csharp
   // ✅ 正确：缓存命中时同步返回
   public ValueTask<User> GetUserAsync(int id)
   {
       if (_cache.TryGetValue(id, out var user))
           return new ValueTask<User>(user); // 同步完成，零分配
       
       return LoadUserAsync(id); // 异步完成
   }
   ```

2. **性能关键路径**（热路径）
   ```csharp
   // ✅ 正确：高频调用，减少分配
   public ValueTask<bool> ValidateAsync(string input)
   {
       if (string.IsNullOrEmpty(input))
           return new ValueTask<bool>(false); // 同步返回
       
       return PerformValidationAsync(input);
   }
   ```

3. **接口设计**（允许实现者选择）
   ```csharp
   // ✅ 正确：接口允许同步和异步实现
   public interface IRepository<T>
   {
       ValueTask<T> GetByIdAsync(int id);
       ValueTask SaveAsync(T entity);
   }
   ```

---

### ❌ **禁止** 使用 `ValueTask<T>` 的场景

1. **多次 await**（ValueTask 只能 await 一次）
   ```csharp
   // ❌ 错误：ValueTask 被 await 多次
   var task = GetValueTaskAsync();
   await task; // 第一次
   await task; // 💥 未定义行为！

   // ✅ 正确：使用 Task
   var task = GetTaskAsync();
   await task; // 第一次
   await task; // 合法
   ```

2. **存储为字段/属性**
   ```csharp
   // ❌ 错误：ValueTask 不应存储
   private ValueTask<int> _pendingOperation;

   // ✅ 正确：使用 Task
   private Task<int> _pendingOperation;
   ```

3. **Task.WhenAll / Task.WhenAny**
   ```csharp
   // ❌ 错误：不能直接用于 Task.WhenAll
   ValueTask<int> task1 = GetAsync1();
   ValueTask<int> task2 = GetAsync2();
   // await Task.WhenAll(task1, task2); // 编译错误

   // ✅ 正确：转换为 Task 或直接使用 Task
   Task<int> task1 = GetAsync1().AsTask();
   Task<int> task2 = GetAsync2().AsTask();
   await Task.WhenAll(task1, task2);
   ```

4. **需要组合的场景**
   ```csharp
   // ❌ 错误：ValueTask 难以组合
   public async ValueTask<Result> ComplexOperationAsync()
   {
       var task1 = Step1Async();
       var task2 = Step2Async();
       // 需要 .AsTask() 转换才能组合
   }

   // ✅ 正确：使用 Task
   public async Task<Result> ComplexOperationAsync()
   {
       var task1 = Step1Async();
       var task2 = Step2Async();
       await Task.WhenAll(task1, task2);
   }
   ```

---

### ✅ 使用 `Task<T>` 的场景

1. **总是异步的操作**
   ```csharp
   // ✅ 正确：I/O 操作总是异步
   public async Task<string> ReadFileAsync(string path)
   {
       return await File.ReadAllTextAsync(path);
   }
   ```

2. **需要组合多个异步操作**
   ```csharp
   // ✅ 正确：需要 Task.WhenAll
   public async Task<Summary> GetSummaryAsync()
   {
       var task1 = GetDataAsync();
       var task2 = GetMetricsAsync();
       var task3 = GetStatsAsync();
       
       await Task.WhenAll(task1, task2, task3);
       
       return new Summary(task1.Result, task2.Result, task3.Result);
   }
   ```

3. **需要多次 await**
   ```csharp
   // ✅ 正确：Task 可以多次 await
   public async Task ProcessAsync()
   {
       var task = LoadDataAsync();
       
       // 做其他事情...
       
       var data = await task; // 第一次
       // 处理...
       await task; // 第二次（虽然少见，但合法）
   }
   ```

4. **需要存储/传递**
   ```csharp
   // ✅ 正确：Task 可以存储
   private Task<int> _backgroundTask;
   
   public void StartBackground()
   {
       _backgroundTask = ProcessInBackgroundAsync();
   }
   
   public async Task<int> GetResultAsync()
   {
       return await _backgroundTask;
   }
   ```

---

## 📋 快速决策树

```
是否需要组合多个异步操作（Task.WhenAll/WhenAny）？
├─ 是 → 使用 Task<T>
└─ 否 → 继续

是否需要多次 await 同一个操作？
├─ 是 → 使用 Task<T>
└─ 否 → 继续

是否需要存储为字段/属性？
├─ 是 → 使用 Task<T>
└─ 否 → 继续

操作同步完成的可能性 > 50%？
├─ 是 → 使用 ValueTask<T>
└─ 否 → 使用 Task<T>

是否为性能关键路径（热路径）？
├─ 是 → 考虑使用 ValueTask<T>
└─ 否 → 使用 Task<T>（更安全）
```

---

## 🔍 常见模式分析

### 1. Mediator SendAsync

```csharp
// ✅ 当前实现正确
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(...)
{
    // 原因：
    // 1. 单次 await
    // 2. 不需要组合
    // 3. 性能关键路径
    // 4. 可能同步完成（缓存命中、验证失败等）
}
```

### 2. Repository GetByIdAsync

```csharp
// ✅ 推荐使用 ValueTask
public ValueTask<User> GetByIdAsync(int id)
{
    // 原因：缓存命中时同步返回，零分配
    if (_cache.TryGetValue(id, out var user))
        return new ValueTask<User>(user);
    
    return LoadFromDatabaseAsync(id);
}
```

### 3. Event PublishAsync

```csharp
// ✅ 当前实现：Task（正确）
public async Task PublishAsync<TEvent>(TEvent @event, ...)
{
    // 原因：
    // 1. 需要 Task.WhenAll 组合多个处理器
    // 2. 总是异步的
    // 3. 需要等待所有处理器完成
    var handlers = GetHandlers();
    await Task.WhenAll(handlers.Select(h => h.Handle(@event)));
}
```

### 4. CircuitBreaker ExecuteAsync

```csharp
// ✅ 当前实现：Task（正确）
public async Task ExecuteAsync(Func<Task> operation)
{
    // 原因：
    // 1. 总是异步的
    // 2. 不太可能同步完成
    // 3. 需要传递 Task（不是 ValueTask）
}
```

### 5. ConcurrencyLimiter AcquireAsync

```csharp
// ✅ 当前实现：ValueTask<T>（正确）
public async ValueTask<SemaphoreReleaser> AcquireAsync(...)
{
    // 原因：
    // 1. SemaphoreSlim.WaitAsync 返回 Task，但可能同步完成
    // 2. 性能关键路径
    // 3. 单次使用，不需要组合
    // 4. 返回 struct，进一步减少分配
}
```

---

## ⚠️ 常见错误

### 错误 1: ValueTask 转 Task 转 ValueTask

```csharp
// ❌ 错误：不必要的转换
public async ValueTask<int> WrongAsync()
{
    var task = GetValueTaskAsync().AsTask(); // 分配了 Task
    return await task;
}

// ✅ 正确：直接 await
public async ValueTask<int> CorrectAsync()
{
    return await GetValueTaskAsync();
}
```

### 错误 2: 在循环中创建 ValueTask

```csharp
// ❌ 错误：每次循环创建 ValueTask（无意义）
public async Task ProcessAsync(List<int> items)
{
    foreach (var item in items)
    {
        await new ValueTask<int>(item); // 无意义
    }
}

// ✅ 正确：直接处理
public Task ProcessAsync(List<int> items)
{
    foreach (var item in items)
    {
        Process(item); // 同步处理
    }
    return Task.CompletedTask;
}
```

### 错误 3: 将 ValueTask 存储到集合

```csharp
// ❌ 错误：ValueTask 不应存储到集合
var tasks = new List<ValueTask<int>>();
tasks.Add(GetAsync1());
tasks.Add(GetAsync2());
// await Task.WhenAll(tasks); // 不支持

// ✅ 正确：使用 Task 或立即转换
var tasks = new List<Task<int>>();
tasks.Add(GetAsync1().AsTask());
tasks.Add(GetAsync2().AsTask());
await Task.WhenAll(tasks);
```

---

## 📊 性能对比

| 场景 | Task<T> | ValueTask<T> | 优势 |
|------|---------|--------------|------|
| 总是异步 | 24 字节 | 24 字节 + struct | Task 更简单 |
| 总是同步 | 24 字节 | 0 字节 | ValueTask 零分配 |
| 50% 同步 | 24 字节 | 平均 12 字节 | ValueTask 减半分配 |
| 需要组合 | 原生支持 | 需要 .AsTask() | Task 更方便 |
| 多次 await | 支持 | ❌ 未定义行为 | Task 更安全 |

---

## ✅ 审查清单

在选择 `ValueTask<T>` 之前，确认：

- [ ] 操作有高概率同步完成（>50%）
- [ ] 不需要多次 await
- [ ] 不需要存储为字段/属性
- [ ] 不需要 Task.WhenAll / Task.WhenAny
- [ ] 性能关键路径，分配成本重要
- [ ] 团队理解 ValueTask 的限制

如果有任何一项不满足，**使用 `Task<T>`** 更安全！

---

## 🎯 Catga 框架推荐规范

### 公共 API

```csharp
// ✅ Mediator（热路径，可能同步完成）
ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(...)

// ✅ Repository（缓存命中同步返回）
ValueTask<T> GetByIdAsync(int id)

// ❌ Event Publishing（总是异步，需要组合）
Task PublishAsync<TEvent>(TEvent @event)
```

### 内部实现

```csharp
// ✅ 单次 await，可能同步完成
private async ValueTask<bool> TryFromCacheAsync(...)

// ❌ 需要组合多个操作
private async Task ExecuteWithRetriesAsync(...)
```

---

**原则**: 如果不确定，使用 `Task<T>` 更安全！❌ 不要过度优化！

