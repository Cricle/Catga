namespace Catga.Messages;

/// <summary>Provides compile-time message metadata without reflection</summary>
public interface IMessageMetadata<TSelf> where TSelf : IMessageMetadata<TSelf>
{
    /// <summary>Gets the message type name (compile-time constant)</summary>
    static abstract string TypeName { get; }

    /// <summary>Gets the message full type name (compile-time constant)</summary>
    static abstract string FullTypeName { get; }
}

