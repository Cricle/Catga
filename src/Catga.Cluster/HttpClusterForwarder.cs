using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster;

/// <summary>
/// HTTP-based cluster forwarder for forwarding requests to leader node.
/// </summary>
public sealed class HttpClusterForwarder : IClusterForwarder
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpClusterForwarder>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public HttpClusterForwarder(
        HttpClient httpClient,
        ILogger<HttpClusterForwarder>? logger = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization and deserialization might require types that cannot be statically analyzed.")]
    public async Task<CatgaResult<TResponse>> ForwardAsync<TRequest, TResponse>(
        TRequest request,
        string leaderEndpoint,
        CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        try
        {
            var requestTypeName = typeof(TRequest).Name;
            var url = $"{leaderEndpoint}/api/catga/forward/{requestTypeName}";

            _logger?.LogDebug("Forwarding {RequestType} to leader at {Endpoint}",
                requestTypeName, leaderEndpoint);

            var response = await _httpClient.PostAsJsonAsync(url, request, _jsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger?.LogWarning("Forward failed with status {Status}: {Error}",
                    response.StatusCode, error);
                return CatgaResult<TResponse>.Failure($"Forward failed: {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, ct);
            return result != null
                ? CatgaResult<TResponse>.Success(result)
                : CatgaResult<TResponse>.Failure("Empty response from leader");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error forwarding to leader {Endpoint}", leaderEndpoint);
            return CatgaResult<TResponse>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error forwarding request to leader");
            return CatgaResult<TResponse>.Failure($"Forward error: {ex.Message}");
        }
    }
}
