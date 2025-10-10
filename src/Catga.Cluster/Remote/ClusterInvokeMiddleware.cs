using System.Diagnostics;
using System.Text.Json;
using Catga;
using Catga.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster.Remote;

/// <summary>
/// 集群调用中间件（处理远程请求）
/// </summary>
public sealed class ClusterInvokeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ClusterInvokeMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ClusterInvokeMiddleware(
        RequestDelegate next,
        ILogger<ClusterInvokeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 只处理 POST /catga/cluster/invoke
        if (!context.Request.Path.StartsWithSegments("/catga/cluster/invoke") ||
            context.Request.Method != HttpMethods.Post)
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // 读取远程请求
            RemoteRequest? remoteRequest;
            try
            {
                remoteRequest = await JsonSerializer.DeserializeAsync<RemoteRequest>(
                    context.Request.Body,
                    JsonOptions,
                    context.RequestAborted);
            }
            catch
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                var errorBytes = JsonSerializer.SerializeToUtf8Bytes(new RemoteResponse
                {
                    RequestId = "unknown",
                    IsSuccess = false,
                    ErrorMessage = "Invalid request format"
                }, JsonOptions);
                await context.Response.Body.WriteAsync(errorBytes, context.RequestAborted);
                return;
            }

            if (remoteRequest == null)
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                var errorBytes = JsonSerializer.SerializeToUtf8Bytes(new RemoteResponse
                {
                    RequestId = "unknown",
                    IsSuccess = false,
                    ErrorMessage = "Invalid request format"
                }, JsonOptions);
                await context.Response.Body.WriteAsync(errorBytes, context.RequestAborted);
                return;
            }

            _logger.LogDebug("Received remote request {RequestId}, RequestType={RequestType}",
                remoteRequest.RequestId, remoteRequest.RequestTypeName);

            // 获取 Mediator
            var mediator = context.RequestServices.GetRequiredService<ICatgaMediator>();
            var options = context.RequestServices.GetRequiredService<ClusterOptions>();

            // 反序列化请求
            var requestType = Type.GetType(remoteRequest.RequestTypeName);
            var responseType = Type.GetType(remoteRequest.ResponseTypeName);

            if (requestType == null || responseType == null)
            {
                await WriteErrorResponse(context, remoteRequest.RequestId, 
                    "Cannot resolve request or response type", stopwatch.ElapsedMilliseconds);
                return;
            }

            var request = JsonSerializer.Deserialize(remoteRequest.PayloadData, requestType, JsonOptions);
            if (request == null)
            {
                await WriteErrorResponse(context, remoteRequest.RequestId,
                    "Failed to deserialize request", stopwatch.ElapsedMilliseconds);
                return;
            }

            // 使用反射调用 SendAsync
            var sendMethod = typeof(ICatgaMediator)
                .GetMethod(nameof(ICatgaMediator.SendAsync), 1, new[] { requestType, typeof(CancellationToken) });

            if (sendMethod == null)
            {
                await WriteErrorResponse(context, remoteRequest.RequestId,
                    "Cannot find SendAsync method", stopwatch.ElapsedMilliseconds);
                return;
            }

            var genericSendMethod = sendMethod.MakeGenericMethod(requestType, responseType);
            var resultTask = (Task?)genericSendMethod.Invoke(mediator, new[] { request, context.RequestAborted });

            if (resultTask == null)
            {
                await WriteErrorResponse(context, remoteRequest.RequestId,
                    "SendAsync returned null", stopwatch.ElapsedMilliseconds);
                return;
            }

            await resultTask;

            // 获取结果
            var resultProperty = resultTask.GetType().GetProperty("Result");
            var result = resultProperty?.GetValue(resultTask);

            if (result == null)
            {
                await WriteErrorResponse(context, remoteRequest.RequestId,
                    "No result returned", stopwatch.ElapsedMilliseconds);
                return;
            }

            // 检查是否成功
            var isSuccessProperty = result.GetType().GetProperty("IsSuccess");
            var isSuccess = (bool?)isSuccessProperty?.GetValue(result) ?? false;

            if (!isSuccess)
            {
                var errorProperty = result.GetType().GetProperty("Error");
                var error = errorProperty?.GetValue(result)?.ToString() ?? "Unknown error";
                
                await WriteErrorResponse(context, remoteRequest.RequestId, error, stopwatch.ElapsedMilliseconds);
                return;
            }

            // 获取响应数据
            var valueProperty = result.GetType().GetProperty("Value");
            var value = valueProperty?.GetValue(result);

            if (value == null)
            {
                await WriteErrorResponse(context, remoteRequest.RequestId,
                    "No value in result", stopwatch.ElapsedMilliseconds);
                return;
            }

            // 序列化响应
            var responseData = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

            stopwatch.Stop();

            var response = new RemoteResponse
            {
                RequestId = remoteRequest.RequestId,
                IsSuccess = true,
                PayloadData = responseData,
                ProcessedByNodeId = options.NodeId,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };

            _logger.LogDebug("Remote request {RequestId} processed successfully in {TimeMs}ms",
                remoteRequest.RequestId, stopwatch.ElapsedMilliseconds);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
            await context.Response.Body.WriteAsync(responseBytes, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing remote request");
            
            stopwatch.Stop();
            
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var errorBytes = JsonSerializer.SerializeToUtf8Bytes(new RemoteResponse
            {
                RequestId = "unknown",
                IsSuccess = false,
                ErrorMessage = ex.Message,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            }, JsonOptions);
            await context.Response.Body.WriteAsync(errorBytes, context.RequestAborted);
        }
    }

    private static async Task WriteErrorResponse(
        HttpContext context,
        string requestId,
        string errorMessage,
        long processingTimeMs)
    {
        context.Response.StatusCode = 200; // 使用 200，错误通过 IsSuccess 表示
        context.Response.ContentType = "application/json";
        var errorBytes = JsonSerializer.SerializeToUtf8Bytes(new RemoteResponse
        {
            RequestId = requestId,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ProcessingTimeMs = processingTimeMs
        }, JsonOptions);
        await context.Response.Body.WriteAsync(errorBytes);
    }
}

