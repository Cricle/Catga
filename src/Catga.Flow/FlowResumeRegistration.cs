using Catga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Flow.Dsl;

public interface IFlowResumeRegistration
{
    bool MatchesFlowType(string flowType);

    Task ResumeAsync(IServiceProvider serviceProvider, string flowId, CancellationToken ct);
}

internal sealed class FlowResumeRegistration<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TFlow> : IFlowResumeRegistration
    where TState : class, IFlowState, new()
    where TFlow : FlowConfig<TState>
{
    private static readonly string FlowTypeName = typeof(TFlow).Name;
    private static readonly string FlowTypeFullName = typeof(TFlow).FullName ?? FlowTypeName;

    public bool MatchesFlowType(string flowType)
        => string.Equals(flowType, FlowTypeFullName, StringComparison.Ordinal)
           || string.Equals(flowType, FlowTypeName, StringComparison.Ordinal);

    public async Task ResumeAsync(IServiceProvider serviceProvider, string flowId, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;
        var mediator = scopedProvider.GetRequiredService<ICatgaMediator>();
        var store = scopedProvider.GetRequiredService<IDslFlowStore>();
        var config = scopedProvider.GetRequiredService<TFlow>();
        var scheduler = scopedProvider.GetService<IFlowScheduler>();
        var requestClientFactory = scopedProvider.GetService<IRequestClientFactory>();

        var executor = new DslFlowExecutor<TState, TFlow>(mediator, store, config, scheduler, requestClientFactory);
        await executor.ResumeAsync(flowId, ct);
    }
}
