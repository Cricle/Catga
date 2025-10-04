using System.Diagnostics;
using Catga.CatGa.Models;
using Catga.CatGa.Policies;
using Catga.CatGa.Repository;
using Catga.CatGa.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.CatGa.Core;

/// <summary>
/// CatGa 执行器 - 模块化重构版
/// 职责：协调 Repository、Transport、Policies 完成分布式事务
/// </summary>
public sealed class CatGaExecutor : ICatGaExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatGaExecutor> _logger;
    private readonly ICatGaRepository _repository;
    private readonly ICatGaTransport _transport;
    private readonly IRetryPolicy _retryPolicy;
    private readonly ICompensationPolicy _compensationPolicy;
    private readonly CatGaOptions _options;

    public CatGaExecutor(
        IServiceProvider serviceProvider,
        ILogger<CatGaExecutor> logger,
        ICatGaRepository repository,
        ICatGaTransport transport,
        IRetryPolicy retryPolicy,
        ICompensationPolicy compensationPolicy,
        CatGaOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _repository = repository;
        _transport = transport;
        _retryPolicy = retryPolicy;
        _compensationPolicy = compensationPolicy;
        _options = options;
    }

    public async Task<CatGaResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        CatGaContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context ??= new CatGaContext();

        // ═══════════════════════════════════════════════════════════
        // 1️⃣ 仓储层：幂等性检查
        // ═══════════════════════════════════════════════════════════
        if (_options.IdempotencyEnabled && !string.IsNullOrEmpty(context.IdempotencyKey))
        {
            if (_repository.TryGetCachedResult<TResponse>(context.IdempotencyKey, out var cachedResult))
            {
                _logger.LogDebug(
                    "[Repository] Idempotency hit: {Key}",
                    context.IdempotencyKey);
                return CatGaResult<TResponse>.Success(cachedResult!, context);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 2️⃣ 核心层：获取事务处理器
        // ═══════════════════════════════════════════════════════════
        var transaction = _serviceProvider.GetRequiredService<ICatGaTransaction<TRequest, TResponse>>();

        // ═══════════════════════════════════════════════════════════
        // 3️⃣ 策略层：执行（带重试）
        // ═══════════════════════════════════════════════════════════
        var sw = Stopwatch.StartNew();
        var executeResult = await ExecuteWithRetryAsync(
            transaction,
            request,
            context,
            cancellationToken);
        sw.Stop();

        // ═══════════════════════════════════════════════════════════
        // 4️⃣ 处理结果
        // ═══════════════════════════════════════════════════════════
        if (executeResult.IsSuccess)
        {
            // 仓储层：缓存成功结果
            if (_options.IdempotencyEnabled && !string.IsNullOrEmpty(context.IdempotencyKey))
            {
                _repository.CacheResult(context.IdempotencyKey, executeResult.Value);
            }

            // 仓储层：保存上下文（可选）
            if (_options.EnableContextPersistence)
            {
                await _repository.SaveContextAsync<TRequest, TResponse>(
                    context.TransactionId,
                    request,
                    context,
                    cancellationToken);
            }

            _logger.LogInformation(
                "[Core] Transaction {TransactionId} completed successfully in {Elapsed}ms",
                context.TransactionId, sw.ElapsedMilliseconds);

            return executeResult;
        }
        else
        {
            // 策略层：执行补偿
            _logger.LogWarning(
                "[Core] Transaction {TransactionId} failed: {Error}, attempting compensation",
                context.TransactionId, executeResult.Error);

            var compensated = await CompensateAsync(
                transaction,
                request,
                context,
                cancellationToken);

            if (compensated)
            {
                return CatGaResult<TResponse>.Compensated(
                    executeResult.Error ?? "Unknown error",
                    context);
            }
            else
            {
                _logger.LogError(
                    "[Core] Transaction {TransactionId} compensation failed",
                    context.TransactionId);

                return CatGaResult<TResponse>.Failure(
                    $"{executeResult.Error} (Compensation failed)",
                    context);
            }
        }
    }

    public async Task<CatGaResult> ExecuteAsync<TRequest>(
        TRequest request,
        CatGaContext? context = null,
        CancellationToken cancellationToken = default)
    {
        context ??= new CatGaContext();

        // 幂等性检查
        if (_options.IdempotencyEnabled && !string.IsNullOrEmpty(context.IdempotencyKey))
        {
            if (_repository.IsProcessed(context.IdempotencyKey))
            {
                _logger.LogDebug(
                    "[Repository] Idempotency hit: {Key}",
                    context.IdempotencyKey);
                return CatGaResult.Success(context);
            }
        }

        var transaction = _serviceProvider.GetRequiredService<ICatGaTransaction<TRequest>>();

        var sw = Stopwatch.StartNew();
        var success = await ExecuteWithRetryAsync(
            transaction,
            request,
            context,
            cancellationToken);
        sw.Stop();

        if (success)
        {
            if (_options.IdempotencyEnabled && !string.IsNullOrEmpty(context.IdempotencyKey))
            {
                _repository.MarkProcessed(context.IdempotencyKey);
            }

            _logger.LogInformation(
                "[Core] Transaction {TransactionId} completed in {Elapsed}ms",
                context.TransactionId, sw.ElapsedMilliseconds);

            return CatGaResult.Success(context);
        }
        else
        {
            var compensated = await CompensateAsync(
                transaction,
                request,
                context,
                cancellationToken);

            if (compensated)
            {
                return CatGaResult.Compensated("Transaction failed but compensated", context);
            }
            else
            {
                return CatGaResult.Failure("Transaction and compensation failed", context);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // 策略层：重试逻辑
    // ═══════════════════════════════════════════════════════════
    private async Task<CatGaResult<TResponse>> ExecuteWithRetryAsync<TRequest, TResponse>(
        ICatGaTransaction<TRequest, TResponse> transaction,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        CancellationTokenSource? cts = null;

        try
        {
            for (int attempt = 0; attempt <= _retryPolicy.MaxAttempts; attempt++)
            {
                try
                {
                    context.SetAttemptCount(attempt + 1);

                    // Reuse CTS if timeout is needed
                    if (_options.GlobalTimeout != Timeout.InfiniteTimeSpan)
                    {
                        cts ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(_options.GlobalTimeout);
                        var result = await transaction.ExecuteAsync(request, cts.Token);
                        _logger.LogDebug("[Policy] Transaction {TransactionId} succeeded on attempt {Attempt}",
                            context.TransactionId, attempt + 1);
                        return CatGaResult<TResponse>.Success(result, context);
                    }

                    var resultNoTimeout = await transaction.ExecuteAsync(request, cancellationToken);
                    _logger.LogDebug("[Policy] Transaction {TransactionId} succeeded on attempt {Attempt}",
                        context.TransactionId, attempt + 1);
                    return CatGaResult<TResponse>.Success(resultNoTimeout, context);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "[Policy] Transaction {TransactionId} failed on attempt {Attempt}/{MaxAttempts}",
                        context.TransactionId, attempt + 1, _retryPolicy.MaxAttempts + 1);

                    if (!_retryPolicy.ShouldRetry(attempt + 1, ex))
                        break;

                    var delay = _retryPolicy.CalculateDelay(attempt + 1);
                    _logger.LogDebug("[Policy] Retrying in {Delay}ms...", delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);

                    cts?.Dispose();
                    cts = null;
                }
            }
        }
        finally
        {
            cts?.Dispose();
        }

        return CatGaResult<TResponse>.Failure(lastException?.Message ?? "Unknown error", context);
    }

    private async Task<bool> ExecuteWithRetryAsync<TRequest>(
        ICatGaTransaction<TRequest> transaction,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts = null;

        try
        {
            for (int attempt = 0; attempt <= _retryPolicy.MaxAttempts; attempt++)
            {
                try
                {
                    context.SetAttemptCount(attempt + 1);

                    if (_options.GlobalTimeout != Timeout.InfiniteTimeSpan)
                    {
                        cts ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(_options.GlobalTimeout);
                        await transaction.ExecuteAsync(request, cts.Token);
                    }
                    else
                    {
                        await transaction.ExecuteAsync(request, cancellationToken);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    if (!_retryPolicy.ShouldRetry(attempt + 1, ex))
                        break;

                    await Task.Delay(_retryPolicy.CalculateDelay(attempt + 1), cancellationToken);
                    cts?.Dispose();
                    cts = null;
                }
            }
        }
        finally
        {
            cts?.Dispose();
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════
    // 策略层：补偿逻辑
    // ═══════════════════════════════════════════════════════════
    private async Task<bool> CompensateAsync<TRequest, TResponse>(
        ICatGaTransaction<TRequest, TResponse> transaction,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken)
    {
        if (!_options.AutoCompensate)
        {
            _logger.LogWarning("[Policy] Auto-compensation is disabled for transaction {TransactionId}", context.TransactionId);
            return false;
        }

        CancellationTokenSource? cts = null;
        try
        {
            if (_compensationPolicy.CompensationTimeout != Timeout.InfiniteTimeSpan)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_compensationPolicy.CompensationTimeout);
                await transaction.CompensateAsync(request, cts.Token);
            }
            else
            {
                await transaction.CompensateAsync(request, cancellationToken);
            }

            context.MarkCompensated();
            _logger.LogInformation("[Policy] Compensation successful for transaction {TransactionId}", context.TransactionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Policy] Compensation failed for transaction {TransactionId}", context.TransactionId);
            if (_compensationPolicy.ThrowOnCompensationFailure)
                throw;
            return false;
        }
        finally
        {
            cts?.Dispose();
        }
    }

    private async Task<bool> CompensateAsync<TRequest>(
        ICatGaTransaction<TRequest> transaction,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken)
    {
        if (!_options.AutoCompensate)
            return false;

        CancellationTokenSource? cts = null;
        try
        {
            if (_compensationPolicy.CompensationTimeout != Timeout.InfiniteTimeSpan)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_compensationPolicy.CompensationTimeout);
                await transaction.CompensateAsync(request, cts.Token);
            }
            else
            {
                await transaction.CompensateAsync(request, cancellationToken);
            }
            context.MarkCompensated();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Policy] Compensation failed for transaction {TransactionId}", context.TransactionId);
            return false;
        }
        finally
        {
            cts?.Dispose();
        }
    }
}

