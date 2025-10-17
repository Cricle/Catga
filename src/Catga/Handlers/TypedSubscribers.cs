namespace Catga;

/// <summary>Typed subscriber cache (避免 Type 作为字典键)</summary>
public static class TypedSubscribers<TMessage> where TMessage : class
{
    public static readonly List<Delegate> Handlers = new();
    public static readonly object Lock = new();
}

