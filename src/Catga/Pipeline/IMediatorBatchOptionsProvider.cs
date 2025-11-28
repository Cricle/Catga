using System;

namespace Catga.Pipeline;

public interface IMediatorBatchOptionsProvider
{
    bool TryGet<TRequest>(out MediatorBatchOptions? options);
    bool TryGetKeySelector<TRequest>(out Func<TRequest, string?>? keySelector);
}
