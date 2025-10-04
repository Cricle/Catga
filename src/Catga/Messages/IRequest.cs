namespace Catga.Messages;

/// <summary>
/// Request message that expects a response
/// </summary>
public interface IRequest<TResponse> : IMessage
{
}

/// <summary>
/// Request message without response
/// </summary>
public interface IRequest : IMessage
{
}

