using System;

namespace Catga.Pipeline;

public static class MediatorBatchOptionsProviderExtensions
{
    public static MediatorBatchOptions GetEffective<TRequest>(this IMediatorBatchOptionsProvider provider, MediatorBatchOptions global)
    {
        if (provider is null) return global;
        return provider.TryGet<TRequest>(out var typed) && typed is not null ? typed : global;
    }

    public static Func<TRequest, string?>? GetKeySelectorOrDefault<TRequest>(this IMediatorBatchOptionsProvider provider)
    {
        if (provider is not null && provider.TryGetKeySelector<TRequest>(out var sel)) return sel;
        return null;
    }
}
