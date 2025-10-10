using System.Net.Http.Json;
using System.Text.Json;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster.Remote;

/// <summary>
/// HTTP 远程调用实现
/// </summary>
public sealed class HttpRemoteInvoker : IRemoteInvoker
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpRemoteInvoker> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HttpRemoteInvoker(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpRemoteInvoker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CatgaResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ClusterNode targetNode,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 序列化请求
            var requestData = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);

            var remoteRequest = new RemoteRequest
            {
                RequestTypeName = typeof(TRequest).AssemblyQualifiedName ?? typeof(TRequest).FullName!,
                ResponseTypeName = typeof(TResponse).AssemblyQualifiedName ?? typeof(TResponse).FullName!,
                PayloadData = requestData
            };

            // 发送 HTTP 请求
            var httpClient = _httpClientFactory.CreateClient("CatgaCluster");
            var endpoint = $"{targetNode.Endpoint}/catga/cluster/invoke";

            _logger.LogDebug("Forwarding request to {Endpoint}, RequestType={RequestType}", 
                endpoint, typeof(TRequest).Name);

            var response = await httpClient.PostAsJsonAsync(
                endpoint,
                remoteRequest,
                JsonOptions,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var remoteResponse = await response.Content.ReadFromJsonAsync<RemoteResponse>(
                JsonOptions,
                cancellationToken);

            if (remoteResponse == null)
            {
                return CatgaResult<TResponse>.Failure("Empty response from remote node");
            }

            if (!remoteResponse.IsSuccess)
            {
                return CatgaResult<TResponse>.Failure(
                    remoteResponse.ErrorMessage ?? "Remote invocation failed");
            }

            if (remoteResponse.PayloadData == null || remoteResponse.PayloadData.Length == 0)
            {
                return CatgaResult<TResponse>.Failure("Empty payload in response");
            }

            // 反序列化响应
            var result = JsonSerializer.Deserialize<TResponse>(
                remoteResponse.PayloadData,
                JsonOptions);

            if (result == null)
            {
                return CatgaResult<TResponse>.Failure("Failed to deserialize response");
            }

            _logger.LogDebug("Remote invocation successful, ProcessedBy={NodeId}, Time={TimeMs}ms",
                remoteResponse.ProcessedByNodeId, remoteResponse.ProcessingTimeMs);

            return CatgaResult<TResponse>.Success(result);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed to {Endpoint}", targetNode.Endpoint);
            return CatgaResult<TResponse>.Failure($"HTTP request failed: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Serialization failed for remote request");
            return CatgaResult<TResponse>.Failure($"Serialization failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during remote invocation");
            return CatgaResult<TResponse>.Failure($"Remote invocation failed: {ex.Message}");
        }
    }
}

