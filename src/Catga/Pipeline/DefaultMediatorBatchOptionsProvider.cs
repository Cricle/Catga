namespace Catga.Pipeline;

public sealed class DefaultMediatorBatchOptionsProvider : IMediatorBatchOptionsProvider
{
    private readonly MediatorBatchOptions _global;

    public DefaultMediatorBatchOptionsProvider(MediatorBatchOptions global)
    {
        _global = global ?? new MediatorBatchOptions();
    }

    public bool TryGet<TRequest>(out MediatorBatchOptions? options)
    {
        options = MediatorBatchProfiles<TRequest>.OptionsTransformers?.Invoke(_global);
        return options != null;
    }

    public bool TryGetKeySelector<TRequest>(out Func<TRequest, string?> keySelector)
    {
        var selector = MediatorBatchProfiles<TRequest>.KeySelector;
        if (selector is null)
        {
            keySelector = default!;
            return false;
        }
        keySelector = selector;
        return true;
    }
}
