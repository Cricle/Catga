using Catga.Flow.Dsl;
using Catga.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Flow.Persistence;

/// <summary>
/// Extends IPersistenceProvider with Flow DSL store creation.
/// Implement this alongside IPersistenceProvider to support Flow DSL.
/// </summary>
public interface IFlowPersistenceProvider : IPersistenceProvider
{
    /// <summary>Create DSL flow store (workflow state).</summary>
    IDslFlowStore? CreateDslFlowStore();

    /// <summary>Create flow store (saga/simple flow).</summary>
    IFlowStore? CreateFlowStore();
}

/// <summary>
/// Extension methods for PersistenceBuilder to add Flow DSL stores.
/// </summary>
public static class PersistenceBuilderFlowExtensions
{
    /// <summary>Add DSL flow store if provider supports it.</summary>
    public static PersistenceBuilder AddDslFlowStore(this PersistenceBuilder builder)
    {
        if (builder.Provider is IFlowPersistenceProvider flowProvider)
        {
            var store = flowProvider.CreateDslFlowStore();
            if (store != null) builder.Services.TryAddSingleton(store);
        }
        return builder;
    }

    /// <summary>Add flow store (saga) if provider supports it.</summary>
    public static PersistenceBuilder AddFlowStore(this PersistenceBuilder builder)
    {
        if (builder.Provider is IFlowPersistenceProvider flowProvider)
        {
            var store = flowProvider.CreateFlowStore();
            if (store != null) builder.Services.TryAddSingleton(store);
        }
        return builder;
    }

    /// <summary>Add all stores including Flow DSL stores.</summary>
    public static PersistenceBuilder AddAllWithFlow(this PersistenceBuilder builder)
        => builder.AddAll().AddDslFlowStore().AddFlowStore();
}
