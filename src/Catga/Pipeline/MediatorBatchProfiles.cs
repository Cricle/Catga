namespace Catga.Pipeline;

public static partial class MediatorBatchProfiles<TRequest>
{
    public static Func<MediatorBatchOptions, MediatorBatchOptions>? OptionsTransformers { get; set; }
    public static Func<TRequest, string?>? KeySelector { get; set; }
}

public static class MediatorBatchProfiles
{
    public static void RegisterOptionsTransformer<TRequest>(Func<MediatorBatchOptions, MediatorBatchOptions> transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        MediatorBatchProfiles<TRequest>.OptionsTransformers = transformer;
    }

    public static void RegisterKeySelector<TRequest>(Func<TRequest, string?> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        MediatorBatchProfiles<TRequest>.KeySelector = selector;
    }
}
