using System;
using System.Collections.Concurrent;

namespace Catga.Pipeline;

public static partial class MediatorBatchProfiles
{
    private static readonly ConcurrentDictionary<Type, Func<MediatorBatchOptions, MediatorBatchOptions>> _optionsTransformers = new();
    private static readonly ConcurrentDictionary<Type, Delegate> _keySelectors = new();

    public static bool TryGetOptionsTransformer(Type requestType, out Func<MediatorBatchOptions, MediatorBatchOptions> transformer)
        => _optionsTransformers.TryGetValue(requestType, out transformer!);

    public static void RegisterOptionsTransformer<TRequest>(Func<MediatorBatchOptions, MediatorBatchOptions> transformer)
        => _optionsTransformers[typeof(TRequest)] = transformer;

    public static bool TryGetKeySelector<TRequest>(out Func<TRequest, string?> selector)
    {
        if (_keySelectors.TryGetValue(typeof(TRequest), out var del) && del is Func<TRequest, string?> typed)
        {
            selector = typed;
            return true;
        }
        selector = default!;
        return false;
    }

    public static void RegisterKeySelector<TRequest>(Func<TRequest, string?> selector)
        => _keySelectors[typeof(TRequest)] = selector;
}
