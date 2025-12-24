namespace Catga.Tests.Framework.Generators;

/// <summary>
/// Saga 步骤
/// </summary>
public record SagaStep(
    string StepId,
    string StepName,
    Func<Task<StepResult>> Execute,
    Func<Task> Compensate,
    TimeSpan Timeout);

/// <summary>
/// 步骤结果
/// </summary>
public record StepResult(
    bool Success,
    object? Data,
    Exception? Error);

/// <summary>
/// Saga 定义
/// </summary>
public record SagaDefinition(
    string SagaId,
    string SagaName,
    List<SagaStep> Steps);

/// <summary>
/// Saga 数据生成器
/// </summary>
public static class SagaGenerators
{
    private static readonly Random Random = new();

    /// <summary>
    /// 生成 Saga（指定步骤数量范围）
    /// </summary>
    public static SagaDefinition GenerateSaga(int minSteps = 3, int maxSteps = 10)
    {
        var stepCount = Random.Next(minSteps, maxSteps + 1);
        var steps = new List<SagaStep>();

        for (int i = 0; i < stepCount; i++)
        {
            steps.Add(GenerateStep(i + 1));
        }

        return new SagaDefinition(
            SagaId: Guid.NewGuid().ToString(),
            SagaName: $"Saga-{Guid.NewGuid().ToString()[..8]}",
            Steps: steps);
    }

    /// <summary>
    /// 生成单个步骤
    /// </summary>
    public static SagaStep GenerateStep(int stepNumber)
    {
        var stepId = $"Step-{stepNumber}";
        var stepName = $"Step {stepNumber}: {GenerateStepName()}";
        var timeout = TimeSpan.FromSeconds(Random.Next(5, 30));

        return new SagaStep(
            StepId: stepId,
            StepName: stepName,
            Execute: () => ExecuteStepAsync(stepId),
            Compensate: () => CompensateStepAsync(stepId),
            Timeout: timeout);
    }

    /// <summary>
    /// 生成步骤名称
    /// </summary>
    private static string GenerateStepName()
    {
        var names = new[]
        {
            "Reserve Inventory",
            "Process Payment",
            "Create Shipment",
            "Send Notification",
            "Update Analytics",
            "Log Transaction",
            "Validate Order",
            "Check Credit",
            "Allocate Resources",
            "Confirm Booking"
        };

        return names[Random.Next(names.Length)];
    }

    /// <summary>
    /// 执行步骤（模拟）
    /// </summary>
    private static async Task<StepResult> ExecuteStepAsync(string stepId)
    {
        // 模拟异步操作
        await Task.Delay(Random.Next(10, 100));

        // 随机成功或失败（90% 成功率）
        var success = Random.Next(0, 10) < 9;

        return new StepResult(
            Success: success,
            Data: success ? new { StepId = stepId, Result = "Success" } : null,
            Error: success ? null : new InvalidOperationException($"Step {stepId} failed"));
    }

    /// <summary>
    /// 补偿步骤（模拟）
    /// </summary>
    private static async Task CompensateStepAsync(string stepId)
    {
        // 模拟异步补偿操作
        await Task.Delay(Random.Next(10, 50));
    }

    /// <summary>
    /// 生成带有指定失败步骤的 Saga
    /// </summary>
    public static SagaDefinition GenerateSagaWithFailureAt(int stepCount, int failAtStep)
    {
        if (failAtStep < 1 || failAtStep > stepCount)
            throw new ArgumentException($"failAtStep must be between 1 and {stepCount}");

        var steps = new List<SagaStep>();

        for (int i = 0; i < stepCount; i++)
        {
            var stepNumber = i + 1;
            var shouldFail = stepNumber == failAtStep;
            steps.Add(GenerateStepWithResult(stepNumber, !shouldFail));
        }

        return new SagaDefinition(
            SagaId: Guid.NewGuid().ToString(),
            SagaName: $"Saga-FailAt{failAtStep}",
            Steps: steps);
    }

    /// <summary>
    /// 生成带有指定结果的步骤
    /// </summary>
    private static SagaStep GenerateStepWithResult(int stepNumber, bool shouldSucceed)
    {
        var stepId = $"Step-{stepNumber}";
        var stepName = $"Step {stepNumber}: {GenerateStepName()}";
        var timeout = TimeSpan.FromSeconds(30);

        return new SagaStep(
            StepId: stepId,
            StepName: stepName,
            Execute: () => Task.FromResult(new StepResult(
                Success: shouldSucceed,
                Data: shouldSucceed ? new { StepId = stepId, Result = "Success" } : null,
                Error: shouldSucceed ? null : new InvalidOperationException($"Step {stepId} failed"))),
            Compensate: () => Task.CompletedTask,
            Timeout: timeout);
    }

    /// <summary>
    /// 生成 Saga 执行日志
    /// </summary>
    public static List<string> CreateExecutionLog()
    {
        return new List<string>();
    }

    /// <summary>
    /// 记录步骤执行
    /// </summary>
    public static void LogStepExecution(List<string> log, int stepNumber, bool isCompensation = false)
    {
        var prefix = isCompensation ? "Compensate" : "Execute";
        log.Add($"{prefix}:{stepNumber}");
    }

    /// <summary>
    /// 生成复杂 Saga（带有嵌套和并行步骤）
    /// </summary>
    public static ComplexSagaDefinition GenerateComplexSaga()
    {
        return new ComplexSagaDefinition(
            SagaId: Guid.NewGuid().ToString(),
            SagaName: "Complex-Saga",
            SequentialSteps: GenerateSaga(3, 5).Steps,
            ParallelSteps: new List<List<SagaStep>>
            {
                GenerateSaga(2, 3).Steps,
                GenerateSaga(2, 3).Steps
            });
    }
}

/// <summary>
/// 复杂 Saga 定义（支持并行步骤）
/// </summary>
public record ComplexSagaDefinition(
    string SagaId,
    string SagaName,
    List<SagaStep> SequentialSteps,
    List<List<SagaStep>> ParallelSteps);
