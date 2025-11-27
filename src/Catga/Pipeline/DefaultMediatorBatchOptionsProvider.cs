using System;

namespace Catga.Pipeline;

public sealed class DefaultMediatorBatchOptionsProvider : IMediatorBatchOptionsProvider
{
    private readonly MediatorBatchOptions _global;

    public DefaultMediatorBatchOptionsProvider(MediatorBatchOptions global)
    {
        _global = global ?? new MediatorBatchOptions();
    }

    public bool TryGet<TRequest>(out MediatorBatchOptions options)
    {
        if (MediatorBatchProfiles.TryGetOptionsTransformer(typeof(TRequest), out var transformer))
        {
            options = transformer(_global);
            return true;
        }
        options = default!;
        return false;
    }

    public bool TryGetKeySelector<TRequest>(out Func<TRequest, string?> keySelector)
    {
        return MediatorBatchProfiles.TryGetKeySelector<TRequest>(out keySelector!);
    }
}
