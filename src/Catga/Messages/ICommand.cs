namespace Catga.Messages;

/// <summary>
/// Command message - intent to change state
/// </summary>
public interface ICommand<TResult> : IRequest<TResult>
{
}

/// <summary>
/// Command message without result
/// </summary>
public interface ICommand : IRequest
{
}

