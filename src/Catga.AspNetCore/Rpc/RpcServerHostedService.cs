using Catga.Rpc;
using Microsoft.Extensions.Hosting;

namespace Catga.AspNetCore.Rpc;

/// <summary>RPC server background service</summary>
public sealed class RpcServerHostedService : IHostedService
{
    private readonly IRpcServer _rpcServer;

    public RpcServerHostedService(IRpcServer rpcServer)
        => _rpcServer = rpcServer;

    public Task StartAsync(CancellationToken cancellationToken)
        => _rpcServer.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => _rpcServer.StopAsync(cancellationToken);
}

