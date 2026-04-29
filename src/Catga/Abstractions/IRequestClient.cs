using System.Diagnostics.CodeAnalysis;
using Catga.Core;

namespace Catga.Abstractions;

/// <summary>
/// Cross-service request/response client. Sends a request to a remote service
/// and waits for a correlated response. Equivalent to MassTransit's IRequestClient&lt;T&gt;.
/// </summary>
public interface IRequestClient<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    where TRequest : class, IRequest<TResponse>
    where TResponse : class
{
    /// <summary>Send request to remote service and await correlated response.</summary>
    Task<CatgaResult<TResponse>> RequestAsync(TRequest request, CancellationToken ct = default);

    /// <summary>Send request with explicit timeout.</summary>
    Task<CatgaResult<TResponse>> RequestAsync(TRequest request, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Factory for creating request clients. Register via DI.</summary>
public interface IRequestClientFactory
{
    IRequestClient<TRequest, TResponse> CreateClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        string? destination = null,
        TimeSpan? defaultTimeout = null)
        where TRequest : class, IRequest<TResponse>
        where TResponse : class;
}
