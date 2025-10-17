namespace Catga.Messages;

/// <summary>Provides compile-time message metadata without reflection</summary>
#if NET7_0_OR_GREATER
public interface IMessageMetadata<TSelf> where TSelf : IMessageMetadata<TSelf>
{
    /// <summary>Gets the message type name (compile-time constant)</summary>
    static abstract string TypeName { get; }

    /// <summary>Gets the message full type name (compile-time constant)</summary>
    static abstract string FullTypeName { get; }
}
#else
#pragma warning disable CA2252 // Preview feature
public interface IMessageMetadata<TSelf> where TSelf : IMessageMetadata<TSelf>
{
    /// <summary>Gets the message type name (compile-time constant)</summary>
    static abstract string TypeName { get; }

    /// <summary>Gets the message full type name (compile-time constant)</summary>
    static abstract string FullTypeName { get; }
}
#pragma warning restore CA2252
#endif

