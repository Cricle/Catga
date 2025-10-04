using CatCat.Transit.CatGa.Core;
using CatCat.Transit.CatGa.Models;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using System.Text.Json;

namespace Catga.Nats;

/// <summary>
/// NATS 实现的 CatGa 分布式传输
/// 用于跨服务的 CatGa 事务协调
/// </summary>
public sealed class NatsCatGaTransport : IDisposable
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsCatGaTransport> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _serviceId;

    public NatsCatGaTransport(
        INatsConnection connection,
        ILogger<NatsCatGaTransport> logger,
        string? serviceId = null)
    {
        _connection = connection;
        _logger = logger;
        _serviceId = serviceId ?? Guid.NewGuid().ToString("N");
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// 发布 CatGa 事务请求
    /// </summary>
    public async Task<TResponse> PublishTransactionAsync<TRequest, TResponse>(
        string subject,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default)
    {
        var message = new CatGaMessage<TRequest>
        {
            Request = request,
            Context = context,
            ServiceId = _serviceId,
            Timestamp = DateTime.UtcNow
        };

        var payload = JsonSerializer.Serialize(message, _jsonOptions);

        _logger.LogDebug(
            "Publishing CatGa transaction to {Subject}, TransactionId: {TransactionId}",
            subject, context.TransactionId);

        var response = await _connection.RequestAsync<string, string>(
            subject,
            payload,
            cancellationToken: cancellationToken);

        var result = JsonSerializer.Deserialize<CatGaResponse<TResponse>>(
            response.Data,
            _jsonOptions);

        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize response");
        }

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Transaction failed: {result.Error}");
        }

        return result.Result!;
    }

    /// <summary>
    /// 订阅 CatGa 事务请求
    /// </summary>
    public Task<IDisposable> SubscribeTransactionAsync<TRequest, TResponse>(
        string subject,
        ICatGaTransaction<TRequest, TResponse> transaction,
        ICatGaExecutor executor,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Subscribing to CatGa transactions on subject: {Subject}", subject);

        var task = Task.Run(async () =>
        {
            await foreach (var msg in _connection.SubscribeAsync<string>(subject, cancellationToken: cancellationToken))
            {
                try
                {
                    var message = JsonSerializer.Deserialize<CatGaMessage<TRequest>>(
                        msg.Data,
                        _jsonOptions);

                    if (message == null)
                    {
                        _logger.LogWarning("Failed to deserialize CatGa message");
                        continue;
                    }

                    _logger.LogDebug(
                        "Received CatGa transaction, TransactionId: {TransactionId}",
                        message.Context.TransactionId);

                    // 执行事务
                    var result = await executor.ExecuteAsync<TRequest, TResponse>(
                        message.Request,
                        message.Context,
                        cancellationToken);

                    // 返回结果
                    var response = new CatGaResponse<TResponse>
                    {
                        IsSuccess = result.IsSuccess,
                        Result = result.Value,
                        Error = result.Error,
                        IsCompensated = result.IsCompensated,
                        ServiceId = _serviceId,
                        Timestamp = DateTime.UtcNow
                    };

                    var responsePayload = JsonSerializer.Serialize(response, _jsonOptions);
                    await msg.ReplyAsync(responsePayload, cancellationToken: cancellationToken);

                    _logger.LogDebug(
                        "CatGa transaction completed, TransactionId: {TransactionId}, Success: {Success}",
                        message.Context.TransactionId, result.IsSuccess);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing CatGa transaction");

                    // 返回错误
                    var errorResponse = new CatGaResponse<TResponse>
                    {
                        IsSuccess = false,
                        Error = ex.Message,
                        ServiceId = _serviceId,
                        Timestamp = DateTime.UtcNow
                    };

                    var errorPayload = JsonSerializer.Serialize(errorResponse, _jsonOptions);
                    await msg.ReplyAsync(errorPayload, cancellationToken: cancellationToken);
                }
            }
        }, cancellationToken);

        // 返回一个空的 IDisposable，因为取消通过 cancellationToken 完成
        return Task.FromResult<IDisposable>(new DummyDisposable());
    }

    /// <summary>
    /// 发布 CatGa 事件（无需响应）
    /// </summary>
    public async Task PublishEventAsync<TRequest>(
        string subject,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default)
    {
        var message = new CatGaMessage<TRequest>
        {
            Request = request,
            Context = context,
            ServiceId = _serviceId,
            Timestamp = DateTime.UtcNow
        };

        var payload = JsonSerializer.Serialize(message, _jsonOptions);

        await _connection.PublishAsync(subject, payload, cancellationToken: cancellationToken);

        _logger.LogDebug(
            "Published CatGa event to {Subject}, TransactionId: {TransactionId}",
            subject, context.TransactionId);
    }

    /// <summary>
    /// 订阅 CatGa 事件
    /// </summary>
    public Task<IDisposable> SubscribeEventAsync<TRequest>(
        string subject,
        Func<TRequest, CatGaContext, Task> handler,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Subscribing to CatGa events on subject: {Subject}", subject);

        var task = Task.Run(async () =>
        {
            await foreach (var msg in _connection.SubscribeAsync<string>(subject, cancellationToken: cancellationToken))
            {
                try
                {
                    var message = JsonSerializer.Deserialize<CatGaMessage<TRequest>>(
                        msg.Data,
                        _jsonOptions);

                    if (message == null)
                    {
                        _logger.LogWarning("Failed to deserialize CatGa event");
                        continue;
                    }

                    await handler(message.Request, message.Context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing CatGa event");
                }
            }
        }, cancellationToken);

        // 返回一个空的 IDisposable，因为取消通过 cancellationToken 完成
        return Task.FromResult<IDisposable>(new DummyDisposable());
    }

    private class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public void Dispose()
    {
        // Connection 由外部管理
    }
}

/// <summary>
/// CatGa 消息封装
/// </summary>
internal class CatGaMessage<TRequest>
{
    public TRequest Request { get; set; } = default!;
    public CatGaContext Context { get; set; } = default!;
    public string ServiceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// CatGa 响应封装
/// </summary>
internal class CatGaResponse<TResponse>
{
    public bool IsSuccess { get; set; }
    public TResponse? Result { get; set; }
    public string? Error { get; set; }
    public bool IsCompensated { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

