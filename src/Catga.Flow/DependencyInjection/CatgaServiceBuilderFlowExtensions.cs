using Catga.DependencyInjection;
using Catga.Flow.Extensions;

namespace Catga.Flow.DependencyInjection;

/// <summary>
/// CatgaServiceBuilder extensions for Flow DSL.
/// </summary>
public static class CatgaServiceBuilderFlowExtensions
{
    /// <summary>
    /// Enable Flow DSL support for distributed workflow orchestration.
    /// </summary>
    public static CatgaServiceBuilder AddFlows(this CatgaServiceBuilder builder, Action<FlowDslOptions>? configure = null)
    {
        builder.Services.AddFlowDsl(configure);
        return builder;
    }
}
