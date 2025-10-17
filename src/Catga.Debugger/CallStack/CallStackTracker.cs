using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Catga.Debugger.CallStack;

/// <summary>
/// Tracks call stacks for message processing using AsyncLocal.
/// Thread-safe and production-safe (zero overhead when disabled).
/// </summary>
public sealed class CallStackTracker
{
    private readonly AsyncLocal<Stack<CallStackFrame>> _callStack = new();
    private readonly bool _enabled;
    private readonly bool _captureVariables;

    /// <summary>
    /// Event raised when a frame is pushed
    /// </summary>
    public event Action<CallStackFrame>? FramePushed;

    /// <summary>
    /// Event raised when a frame is popped
    /// </summary>
    public event Action<CallStackFrame>? FramePopped;

    public CallStackTracker(bool enabled = false, bool captureVariables = false)
    {
        _enabled = enabled;
        _captureVariables = captureVariables;
    }

    /// <summary>
    /// Gets the current call stack depth
    /// </summary>
    public int Depth => _enabled ? (_callStack.Value?.Count ?? 0) : 0;

    /// <summary>
    /// Pushes a new frame onto the call stack.
    /// Returns an IDisposable that will automatically pop the frame when disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IDisposable PushFrame(
        string methodName,
        string typeName,
        string? messageType = null,
        string? correlationId = null,
        [CallerFilePath] string? fileName = null,
        [CallerLineNumber] int lineNumber = 0)
    {
        // Fast path: if disabled, return no-op disposable
        if (!_enabled)
            return NoOpDisposable.Instance;

        var stack = _callStack.Value ??= new Stack<CallStackFrame>();

        var frame = new CallStackFrame(
            methodName,
            typeName,
            fileName,
            lineNumber
        )
        {
            MessageType = messageType,
            CorrelationId = correlationId,
            Depth = stack.Count
        };

        stack.Push(frame);
        FramePushed?.Invoke(frame);

        return new FrameScope(this, frame);
    }

    /// <summary>
    /// Gets the current call stack (top to bottom)
    /// </summary>
    public IReadOnlyList<CallStackFrame> GetCurrentStack()
    {
        if (!_enabled || _callStack.Value == null)
            return Array.Empty<CallStackFrame>();

        return _callStack.Value.ToList();
    }

    /// <summary>
    /// Gets the current (top) frame
    /// </summary>
    public CallStackFrame? GetCurrentFrame()
    {
        if (!_enabled || _callStack.Value == null || _callStack.Value.Count == 0)
            return null;

        return _callStack.Value.Peek();
    }

    /// <summary>
    /// Adds a local variable to the current frame
    /// </summary>
    public void AddVariable(string name, object? value)
    {
        if (!_enabled || !_captureVariables)
            return;

        var frame = GetCurrentFrame();
        if (frame != null)
        {
            frame.LocalVariables[name] = value;
        }
    }

    /// <summary>
    /// Pops a frame from the call stack
    /// </summary>
    private void PopFrame(CallStackFrame frame, bool success = true, Exception? exception = null)
    {
        if (!_enabled || _callStack.Value == null)
            return;

        if (_callStack.Value.Count > 0 && _callStack.Value.Peek() == frame)
        {
            var poppedFrame = _callStack.Value.Pop();
            poppedFrame.MarkExited(success, exception);
            FramePopped?.Invoke(poppedFrame);
        }
    }

    /// <summary>
    /// Clears the current call stack (useful for testing)
    /// </summary>
    public void Clear()
    {
        _callStack.Value?.Clear();
    }

    /// <summary>
    /// Disposable scope for automatic frame popping
    /// </summary>
    private sealed class FrameScope : IDisposable
    {
        private readonly CallStackTracker _tracker;
        private readonly CallStackFrame _frame;
        private bool _disposed;

        public FrameScope(CallStackTracker tracker, CallStackFrame frame)
        {
            _tracker = tracker;
            _frame = frame;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _tracker.PopFrame(_frame);
        }
    }

    /// <summary>
    /// No-op disposable for when tracking is disabled
    /// </summary>
    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}

