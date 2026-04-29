namespace Catga.Abstractions;

/// <summary>
/// Optional transport capability for exposing a transport-specific default request timeout.
/// </summary>
public interface IRequestTimeoutDefaults
{
    /// <summary>
    /// Default timeout to use for request/response when the caller does not specify one.
    /// </summary>
    TimeSpan DefaultRequestTimeout { get; }
}
