namespace Catga.Messages;

/// <summary>
/// Query message - request for data without side effects
/// </summary>
public interface IQuery<TResult> : IRequest<TResult>
{
}

