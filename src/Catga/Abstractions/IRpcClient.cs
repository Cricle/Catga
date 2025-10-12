using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Results;

namespace Catga.Rpc;

/// <summary>RPC client for inter-service communication</summary>
public interface IRpcClient
{
    Task<CatgaResult<TResponse>> CallAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(string serviceName, TRequest request, CancellationToken cancellationToken = default) where TRequest : class, IRequest<TResponse>;

    Task<CatgaResult> CallAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(string serviceName, TRequest request, CancellationToken cancellationToken = default) where TRequest : class, IRequest;

    Task<CatgaResult<TResponse>> CallAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(string serviceName, string methodName, TRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where TRequest : class;
}

