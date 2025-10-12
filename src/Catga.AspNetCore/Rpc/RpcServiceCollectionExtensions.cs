using Catga.Rpc;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.AspNetCore.Rpc;

/// <summary>RPC DI extensions</summary>
public static class RpcServiceCollectionExtensions
{
    public static IServiceCollection AddCatgaRpcClient(this IServiceCollection services, Action<RpcOptions>? configure = null)
    {
        var options = new RpcOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton<IRpcClient, RpcClient>();
        return services;
    }

    public static IServiceCollection AddCatgaRpcServer(this IServiceCollection services, Action<RpcOptions>? configure = null)
    {
        var options = new RpcOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton<IRpcServer, RpcServer>();
        services.AddHostedService<RpcServerHostedService>();
        return services;
    }
}

