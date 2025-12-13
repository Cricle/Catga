using Microsoft.AspNetCore.Http;

namespace Catga.AspNetCore.Caching;

/// <summary>
/// Cache policy attribute for endpoint caching configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CachePolicyAttribute : Attribute
{
    public int DurationSeconds { get; set; }
    public string? VaryByHeader { get; set; }
    public string? VaryByQueryParam { get; set; }

    public CachePolicyAttribute(int durationSeconds = 300)
    {
        DurationSeconds = durationSeconds;
    }
}

/// <summary>
/// Cache control header builder.
/// </summary>
public static class CacheControlExtensions
{
    public static void SetCacheControl(this HttpResponse response, int maxAgeSeconds, bool isPublic = false)
    {
        var cacheControl = isPublic ? "public" : "private";
        response.Headers.CacheControl = $"{cacheControl}, max-age={maxAgeSeconds}";
    }

    public static void SetNoCacheControl(this HttpResponse response)
    {
        response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

    public static void SetETag(this HttpResponse response, string etag)
    {
        response.Headers.ETag = $"\"{etag}\"";
    }

    public static string? GetIfNoneMatch(this HttpRequest request)
    {
        return request.Headers.IfNoneMatch.FirstOrDefault()?.ToString().Trim('"');
    }
}

/// <summary>
/// In-memory cache implementation for responses.
/// </summary>
public class ResponseCache
{
    private readonly Dictionary<string, CachedResponse> _cache = new();
    private readonly object _lock = new();

    public bool TryGetValue(string key, out CachedResponse? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached) && !cached.IsExpired)
            {
                value = cached;
                return true;
            }
            _cache.Remove(key);
            value = null;
            return false;
        }
    }

    public void Set(string key, CachedResponse response)
    {
        lock (_lock)
        {
            _cache[key] = response;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }
}

public class CachedResponse
{
    public required string Content { get; set; }
    public required string ContentType { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public string? ETag { get; set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

/// <summary>
/// Cache key builder for consistent cache key generation.
/// </summary>
public static class CacheKeyBuilder
{
    public static string BuildKey(string path, string? queryString = null, string? varyByHeader = null)
    {
        var parts = new List<string> { path };
        if (!string.IsNullOrEmpty(queryString))
            parts.Add(queryString);
        if (!string.IsNullOrEmpty(varyByHeader))
            parts.Add(varyByHeader);

        return string.Join("|", parts);
    }

    public static string GenerateETag(string content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }
}
