using System.Diagnostics.CodeAnalysis;
using Catga.Messages;

namespace Catga.Rpc;

/// <summary>RPC server for handling inter-service requests</summary>
public interface IRpcServer
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void RegisterHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(string methodName, Func<TRequest, CancellationToken, Task<TResponse>> handler) where TRequest : class;
}

