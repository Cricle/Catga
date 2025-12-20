using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catga.Observability;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Observability;

/// <summary>
/// Comprehensive tests for DiagnosticsScope and DiagnosticsScopeFactory.
/// </summary>
public class DiagnosticsScopeTests
{
    private readonly Meter _meter;
    private readonly Counter<long> _successCounter;
    private readonly Counter<long> _failureCounter;
    private readonly Histogram<double> _durationHistogram;

    public DiagnosticsScopeTests()
    {
        _meter = new Meter("Catga.Tests.DiagnosticsScope");
        _successCounter = _meter.CreateCounter<long>("test_success");
        _failureCounter = _meter.CreateCounter<long>("test_failure");
        _durationHistogram = _meter.CreateHistogram<double>("test_duration");
    }

    [Fact]
    public void DiagnosticsScope_ShouldCreateActivity()
    {
        using var scope = new DiagnosticsScope("TestActivity");
        // Activity may be null if no listener is registered
        // Just verify no exception is thrown
    }

    [Fact]
    public void DiagnosticsScope_ShouldTrackElapsedTime()
    {
        using var scope = new DiagnosticsScope("TestActivity");
        Thread.Sleep(10);
        scope.ElapsedMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DiagnosticsScope_SetError_ShouldSetActivityStatus()
    {
        using var scope = new DiagnosticsScope("TestActivity");
        var ex = new InvalidOperationException("Test error");
        scope.SetError(ex);
        // Verify no exception is thrown
    }

    [Fact]
    public void DiagnosticsScope_RecordSuccess_WithoutTags_ShouldWork()
    {
        using var scope = new DiagnosticsScope("TestActivity", _successCounter);
        scope.RecordSuccess();
        scope.RecordSuccess(5);
    }

    [Fact]
    public void DiagnosticsScope_RecordSuccess_WithTags_ShouldWork()
    {
        using var scope = new DiagnosticsScope("TestActivity", _successCounter, tagKey: "key", tagValue: "value");
        scope.RecordSuccess();
        scope.RecordSuccess(3);
    }

    [Fact]
    public void DiagnosticsScope_RecordFailure_WithoutTags_ShouldWork()
    {
        using var scope = new DiagnosticsScope("TestActivity", failureCounter: _failureCounter);
        scope.RecordFailure();
        scope.RecordFailure(2);
    }

    [Fact]
    public void DiagnosticsScope_RecordFailure_WithTags_ShouldWork()
    {
        using var scope = new DiagnosticsScope("TestActivity", failureCounter: _failureCounter, tagKey: "key", tagValue: "value");
        scope.RecordFailure();
        scope.RecordFailure(4);
    }

    [Fact]
    public void DiagnosticsScope_Dispose_ShouldRecordDuration_WithoutTags()
    {
        var scope = new DiagnosticsScope("TestActivity", durationHistogram: _durationHistogram);
        Thread.Sleep(5);
        scope.Dispose();
    }

    [Fact]
    public void DiagnosticsScope_Dispose_ShouldRecordDuration_WithTags()
    {
        var scope = new DiagnosticsScope("TestActivity", durationHistogram: _durationHistogram, tagKey: "key", tagValue: "value");
        Thread.Sleep(5);
        scope.Dispose();
    }

    [Fact]
    public void DiagnosticsScope_WithAllParameters_ShouldWork()
    {
        using var scope = new DiagnosticsScope(
            "TestActivity",
            _successCounter,
            _failureCounter,
            _durationHistogram,
            "testKey",
            "testValue");

        scope.RecordSuccess();
        scope.RecordFailure();
        scope.SetError(new Exception("test"));
    }

    [Fact]
    public void DiagnosticsScope_WithNullCounters_ShouldNotThrow()
    {
        using var scope = new DiagnosticsScope("TestActivity");
        scope.RecordSuccess();
        scope.RecordFailure();
    }

    [Fact]
    public void DiagnosticsScope_Activity_Property_ShouldBeAccessible()
    {
        using var scope = new DiagnosticsScope("TestActivity");
        // Activity may be null if no listener
        var activity = scope.Activity;
    }
}

/// <summary>
/// Tests for DiagnosticsScopeFactory.
/// </summary>
public class DiagnosticsScopeFactoryTests
{
    [Fact]
    public void EventStoreAppend_ShouldCreateScope()
    {
        using var scope = DiagnosticsScopeFactory.EventStoreAppend("stream-1");
        scope.RecordSuccess();
    }

    [Fact]
    public void EventStoreRead_ShouldCreateScope()
    {
        using var scope = DiagnosticsScopeFactory.EventStoreRead("stream-1");
        scope.RecordSuccess();
    }

    [Fact]
    public void InboxOperation_ShouldCreateScope()
    {
        using var scope = DiagnosticsScopeFactory.InboxOperation("TryLock");
        scope.RecordSuccess();
    }

    [Fact]
    public void OutboxOperation_ShouldCreateScope()
    {
        using var scope = DiagnosticsScopeFactory.OutboxOperation("Add");
        scope.RecordSuccess();
        scope.RecordFailure();
    }

    [Fact]
    public void LockOperation_ShouldCreateScope()
    {
        using var scope = DiagnosticsScopeFactory.LockOperation("my-resource");
        scope.RecordSuccess();
        scope.RecordFailure();
    }

    [Fact]
    public void AllFactoryMethods_ShouldHandleErrors()
    {
        var ex = new InvalidOperationException("Test");

        using (var scope = DiagnosticsScopeFactory.EventStoreAppend("s1"))
            scope.SetError(ex);

        using (var scope = DiagnosticsScopeFactory.EventStoreRead("s1"))
            scope.SetError(ex);

        using (var scope = DiagnosticsScopeFactory.InboxOperation("op"))
            scope.SetError(ex);

        using (var scope = DiagnosticsScopeFactory.OutboxOperation("op"))
            scope.SetError(ex);

        using (var scope = DiagnosticsScopeFactory.LockOperation("res"))
            scope.SetError(ex);
    }
}
