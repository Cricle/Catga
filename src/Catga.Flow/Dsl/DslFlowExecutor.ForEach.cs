using Catga.Abstractions;

namespace Catga.Flow.Dsl;

public partial class DslFlowExecutor<TState, TConfig>
    where TState : class, IFlowState, new()
    where TConfig : FlowConfig<TState>
{
    private async Task<StepResult> ExecuteForEachAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.GetCollection == null)
            return StepResult.Failed("No collection selector configured for ForEach");

        try
        {
            var enumerable = step.GetCollection(state);
            if (enumerable == null)
                return StepResult.Succeeded();

            if (step.StreamingEnabled)
            {
                return await ProcessItemsStreaming(state, step, enumerable, cancellationToken);
            }

            var items = enumerable.Cast<object>().ToList();
            var flowId = state.FlowId ?? throw new InvalidOperationException("FlowId is required for ForEach execution");
            if (items.Count == 0)
            {
                step.InvokeComplete?.Invoke(state);
                await _store.ClearForEachProgressAsync(flowId, stepIndex, cancellationToken);
                return StepResult.Succeeded();
            }

            var maxDegreeOfParallelism = step.MaxDegreeOfParallelism ?? 1;
            var result = maxDegreeOfParallelism <= 1
                ? await ProcessItemsSequentially(state, step, items, flowId, stepIndex, initialProgress: null, cancellationToken)
                : await ProcessItemsInParallel(state, step, items, maxDegreeOfParallelism, cancellationToken);

            if (result.Success)
                await _store.ClearForEachProgressAsync(flowId, stepIndex, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"ForEach execution failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessItemsSequentially(
        TState state,
        FlowStep step,
        List<object> items,
        string flowId,
        int stepIndex,
        ForEachProgress? initialProgress,
        CancellationToken cancellationToken)
    {
        var progress = CreateForEachProgress(items.Count, 0, initialProgress);

        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            var result = await ProcessSingleItemAsync(state, step, item, index, cancellationToken);
            progress = result.Success
                ? AdvanceForEachProgress(progress, index, success: true, continueFromNextItem: true)
                : AdvanceForEachProgress(
                    progress,
                    index,
                    success: false,
                    continueFromNextItem: step.FailureHandling == ForEachFailureHandling.ContinueOnFailure);

            await _store.SaveForEachProgressAsync(flowId, stepIndex, progress, cancellationToken);

            if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return result;
            }
        }

        step.InvokeComplete?.Invoke(state);
        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessItemsInParallel(
        TState state,
        FlowStep step,
        List<object> items,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
        var tasks = items.Select((item, index) =>
            ProcessSingleItemWithSemaphoreAsync(semaphore, state, step, item, index, cancellationToken)).ToList();

        var results = await Task.WhenAll(tasks);

        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Any() && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
        {
            return failures.First();
        }

        step.InvokeComplete?.Invoke(state);
        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessSingleItemWithSemaphoreAsync(
        SemaphoreSlim semaphore,
        TState state,
        FlowStep step,
        object item,
        int index,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ProcessSingleItemAsync(state, step, item, index, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<StepResult> ProcessSingleItemAsync(
        TState state,
        FlowStep step,
        object item,
        int index,
        CancellationToken cancellationToken)
    {
        try
        {
            if (step.ConfigureItemSteps != null)
            {
                var tempBuilder = new FlowBuilder<TState>();
                step.ConfigureItemSteps(item, tempBuilder);
                var unsupportedStep = FindFirstSuspendingStep(tempBuilder.Steps);
                if (unsupportedStep != null)
                {
                    return StepResult.Failed(
                        $"ForEach failed on item {index}: {BuildUnsupportedSuspendingStepMessage("ForEach", unsupportedStep.Type)}");
                }

                foreach (var configuredStep in tempBuilder.Steps)
                {
                    var stepResult = await ExecuteStepAsync(state, configuredStep, index, cancellationToken);
                    if (stepResult.IsSuspended)
                    {
                        return StepResult.Failed(
                            $"ForEach failed on item {index}: {BuildUnsupportedSuspendingStepMessage("ForEach", configuredStep.Type)}");
                    }

                    if (!stepResult.Success)
                    {
                        step.InvokeItemFail?.Invoke(state, item, stepResult.Error);
                        return StepResult.Failed($"ForEach failed on item {index}: {stepResult.Error}");
                    }

                    if (stepResult.Success && stepResult.Result != null && configuredStep.SetResult != null)
                    {
                        configuredStep.SetResult(state, stepResult.Result);
                    }

                    if (stepResult.Success)
                    {
                        step.InvokeItemSuccess?.Invoke(state, item, stepResult.Result);
                    }
                }
            }

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            step.InvokeItemFail?.Invoke(state, item, ex.Message);
            return StepResult.Failed($"ForEach failed on item {index}: {ex.Message}");
        }
    }

    private async Task<StepResult> ResumeForEachAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.GetCollection == null)
            return StepResult.Failed("No collection selector configured for ForEach");

        try
        {
            var enumerable = step.GetCollection(state);
            if (enumerable == null)
                return StepResult.Succeeded();

            var items = enumerable.ToList();
            if (items.Count == 0)
                return StepResult.Succeeded();

            var flowId = state.FlowId ?? throw new InvalidOperationException("FlowId is required for ForEach recovery");
            var progress = await _store.GetForEachProgressAsync(flowId, stepIndex, cancellationToken);
            var startIndex = progress?.CurrentIndex
                ?? (position.Path.Length > 1 ? position.Path[^1] : 0);
            if (startIndex >= items.Count)
            {
                step.InvokeComplete?.Invoke(state);
                await _store.ClearForEachProgressAsync(flowId, stepIndex, cancellationToken);
                return StepResult.Succeeded();
            }

            var maxDegreeOfParallelism = step.MaxDegreeOfParallelism ?? 1;
            var result = maxDegreeOfParallelism <= 1
                ? await ProcessItemsSequentiallyFromIndex(state, step, items, startIndex, flowId, stepIndex, progress, cancellationToken)
                : await ProcessItemsInParallelFromIndex(state, step, items, startIndex, maxDegreeOfParallelism, cancellationToken);

            if (result.Success)
                await _store.ClearForEachProgressAsync(flowId, stepIndex, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"ForEach failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessItemsSequentiallyFromIndex(
        TState state,
        FlowStep step,
        List<object> items,
        int startIndex,
        string flowId,
        int stepIndex,
        ForEachProgress? initialProgress,
        CancellationToken cancellationToken)
    {
        var progress = CreateForEachProgress(items.Count, startIndex, initialProgress);

        for (int i = startIndex; i < items.Count; i++)
        {
            var result = await ProcessSingleItemAsync(state, step, items[i], i, cancellationToken);
            progress = result.Success
                ? AdvanceForEachProgress(progress, i, success: true, continueFromNextItem: true)
                : AdvanceForEachProgress(
                    progress,
                    i,
                    success: false,
                    continueFromNextItem: step.FailureHandling == ForEachFailureHandling.ContinueOnFailure);

            await _store.SaveForEachProgressAsync(flowId, stepIndex, progress, cancellationToken);

            if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return result;
            }
        }

        step.InvokeComplete?.Invoke(state);
        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessItemsInParallelFromIndex(
        TState state,
        FlowStep step,
        List<object> items,
        int startIndex,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var remainingItems = items.Skip(startIndex).ToList();
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

        try
        {
            var tasks = remainingItems.Select((item, relativeIndex) =>
                ProcessItemWithSemaphore(state, step, item, startIndex + relativeIndex, semaphore, cancellationToken)).ToList();

            var results = await Task.WhenAll(tasks);

            var firstFailure = results.FirstOrDefault(r => !r.Success);
            if (firstFailure.Success == false && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return firstFailure;
            }

            step.InvokeComplete?.Invoke(state);
            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Parallel processing failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessItemsStreaming(
        TState state,
        FlowStep step,
        System.Collections.IEnumerable enumerable,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchSize = step.BatchSize;
            var index = 0;
            var batch = new List<object>(batchSize);

            foreach (var item in enumerable)
            {
                batch.Add(item);

                if (batch.Count >= batchSize)
                {
                    var result = await ProcessBatch(state, step, batch, index - batch.Count + 1, cancellationToken);
                    if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
                    {
                        return result;
                    }
                    batch.Clear();
                }

                index++;
            }

            if (batch.Count > 0)
            {
                var result = await ProcessBatch(state, step, batch, index - batch.Count, cancellationToken);
                if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
                {
                    return result;
                }
            }

            step.InvokeComplete?.Invoke(state);
            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Streaming ForEach failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessBatch(
        TState state,
        FlowStep step,
        List<object> batch,
        int startIndex,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < batch.Count; i++)
        {
            var result = await ProcessSingleItemAsync(state, step, batch[i], startIndex + i, cancellationToken);
            if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return result;
            }
        }

        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessItemWithSemaphore(
        TState state,
        FlowStep step,
        object item,
        int index,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ProcessSingleItemAsync(state, step, item, index, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static ForEachProgress CreateForEachProgress(
        int totalCount,
        int startIndex,
        ForEachProgress? existingProgress)
    {
        if (existingProgress != null)
        {
            return existingProgress with
            {
                TotalCount = totalCount,
                CompletedIndices = [.. existingProgress.CompletedIndices],
                FailedIndices = [.. existingProgress.FailedIndices]
            };
        }

        return new ForEachProgress
        {
            CurrentIndex = startIndex,
            TotalCount = totalCount,
            CompletedIndices = Enumerable.Range(0, startIndex).ToList(),
            FailedIndices = []
        };
    }

    private static ForEachProgress AdvanceForEachProgress(
        ForEachProgress progress,
        int itemIndex,
        bool success,
        bool continueFromNextItem)
    {
        var completed = new List<int>(progress.CompletedIndices);
        var failed = new List<int>(progress.FailedIndices);

        if (success)
        {
            if (!completed.Contains(itemIndex))
                completed.Add(itemIndex);
            failed.Remove(itemIndex);
        }
        else if (!failed.Contains(itemIndex))
        {
            failed.Add(itemIndex);
        }

        return progress with
        {
            CurrentIndex = continueFromNextItem ? itemIndex + 1 : itemIndex,
            CompletedIndices = completed,
            FailedIndices = failed
        };
    }
}
