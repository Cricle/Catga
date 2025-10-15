namespace Catga.Debugger.Core;

/// <summary>AOT-friendly interface for custom variable capture</summary>
/// <remarks>
/// Implement this interface on your messages/aggregates to provide
/// AOT-compatible variable capture without reflection.
/// </remarks>
public interface IDebugCapture
{
    /// <summary>Capture variables for debugging (AOT-compatible)</summary>
    Dictionary<string, object?> CaptureVariables();
}

