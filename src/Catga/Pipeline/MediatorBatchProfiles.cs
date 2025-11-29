namespace Catga.Pipeline;

public static class MediatorBatchProfiles<TRequest>
{
    public static Func<MediatorBatchOptions, MediatorBatchOptions>? OptionsTransformers { get; set; }
    public static Func<TRequest, string?>? KeySelector { get; set; }
}

public static class MediatorBatchProfiles
{
    public static void RegisterOptionsTransformer<TRequest>(Func<MediatorBatchOptions, MediatorBatchOptions> transformer)
    {
        var prev = MediatorBatchProfiles<TRequest>.OptionsTransformers;
        if (prev is null)
            MediatorBatchProfiles<TRequest>.OptionsTransformers = transformer;
        else
            MediatorBatchProfiles<TRequest>.OptionsTransformers = g => transformer(prev(g));
    }

    public static void RegisterKeySelector<TRequest>(Func<TRequest, string?> selector)
        => MediatorBatchProfiles<TRequest>.KeySelector = selector;
}
