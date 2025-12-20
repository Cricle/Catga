using System.Diagnostics;
using Catga.Observability;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Observability;

/// <summary>
/// Comprehensive tests for MetricsHelper.
/// </summary>
public class MetricsHelperTests
{
    #region Stopwatch Helpers

    [Fact]
    public void StartTimestamp_ShouldReturnPositiveValue()
    {
        var timestamp = MetricsHelper.StartTimestamp();
        timestamp.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetElapsedMs_ShouldReturnPositiveValue()
    {
        var start = MetricsHelper.StartTimestamp();
        Thread.Sleep(10);
        var elapsed = MetricsHelper.GetElapsedMs(start);
        elapsed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetElapsedMs_ShouldBeAccurate()
    {
        var start = MetricsHelper.StartTimestamp();
        Thread.Sleep(50);
        var elapsed = MetricsHelper.GetElapsedMs(start);
        elapsed.Should().BeGreaterThan(40).And.BeLessThan(200);
    }

    #endregion

    #region Activity Helpers

    [Fact]
    public void StartActivity_ShouldNotThrow()
    {
        var activity = MetricsHelper.StartActivity("TestActivity");
        activity?.Dispose();
    }

    [Fact]
    public void StartActivity_WithKind_ShouldNotThrow()
    {
        var activity = MetricsHelper.StartActivity("TestActivity", ActivityKind.Producer);
        activity?.Dispose();
    }

    [Fact]
    public void StartPersistenceActivity_ShouldNotThrow()
    {
        var activity = MetricsHelper.StartPersistenceActivity("EventStore", "Append");
        activity?.Dispose();
    }

    [Fact]
    public void SetActivitySuccess_WithNullActivity_ShouldNotThrow()
    {
        MetricsHelper.SetActivitySuccess(null, MetricsHelper.StartTimestamp());
    }

    [Fact]
    public void SetActivitySuccess_WithActivity_ShouldSetTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var activity = MetricsHelper.StartActivity("TestActivity");
        if (activity != null)
        {
            var start = MetricsHelper.StartTimestamp();
            Thread.Sleep(5);
            MetricsHelper.SetActivitySuccess(activity, start);
            activity.Status.Should().Be(ActivityStatusCode.Ok);
            activity.Dispose();
        }
    }

    [Fact]
    public void SetActivityError_WithNullActivity_ShouldNotThrow()
    {
        MetricsHelper.SetActivityError(null, new Exception("test"));
    }

    [Fact]
    public void SetActivityError_WithActivity_ShouldSetTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var activity = MetricsHelper.StartActivity("TestActivity");
        if (activity != null)
        {
            MetricsHelper.SetActivityError(activity, new InvalidOperationException("Test error"));
            activity.Status.Should().Be(ActivityStatusCode.Error);
            activity.Dispose();
        }
    }

    #endregion

    #region EventStore Metrics

    [Fact]
    public void RecordEventStoreAppend_ShouldNotThrow()
    {
        var start = MetricsHelper.StartTimestamp();
        MetricsHelper.RecordEventStoreAppend(5, start);
    }

    [Fact]
    public void RecordEventStoreRead_ShouldNotThrow()
    {
        var start = MetricsHelper.StartTimestamp();
        MetricsHelper.RecordEventStoreRead(start);
    }

    [Fact]
    public void RecordEventStoreFailure_ShouldNotThrow()
    {
        MetricsHelper.RecordEventStoreFailure();
    }

    #endregion

    #region Inbox/Outbox Metrics

    [Fact]
    public void RecordInboxProcessed_ShouldNotThrow()
    {
        MetricsHelper.RecordInboxProcessed();
    }

    [Fact]
    public void RecordInboxLockAcquired_ShouldNotThrow()
    {
        MetricsHelper.RecordInboxLockAcquired();
    }

    [Fact]
    public void RecordInboxLockReleased_ShouldNotThrow()
    {
        MetricsHelper.RecordInboxLockReleased();
    }

    [Fact]
    public void RecordOutboxAdded_ShouldNotThrow()
    {
        MetricsHelper.RecordOutboxAdded();
    }

    [Fact]
    public void RecordOutboxPublished_ShouldNotThrow()
    {
        MetricsHelper.RecordOutboxPublished();
    }

    [Fact]
    public void RecordOutboxFailed_ShouldNotThrow()
    {
        MetricsHelper.RecordOutboxFailed();
    }

    #endregion

    #region Idempotency Metrics

    [Fact]
    public void RecordIdempotencyHit_ShouldNotThrow()
    {
        MetricsHelper.RecordIdempotencyHit();
    }

    [Fact]
    public void RecordIdempotencyMiss_ShouldNotThrow()
    {
        MetricsHelper.RecordIdempotencyMiss();
    }

    [Fact]
    public void RecordIdempotency_Hit_ShouldNotThrow()
    {
        MetricsHelper.RecordIdempotency(true);
    }

    [Fact]
    public void RecordIdempotency_Miss_ShouldNotThrow()
    {
        MetricsHelper.RecordIdempotency(false);
    }

    #endregion

    #region Lock Metrics

    [Fact]
    public void RecordLockAcquired_ShouldNotThrow()
    {
        var start = MetricsHelper.StartTimestamp();
        MetricsHelper.RecordLockAcquired(start);
    }

    [Fact]
    public void RecordLockFailed_ShouldNotThrow()
    {
        MetricsHelper.RecordLockFailed();
    }

    #endregion

    #region Dead Letter Metrics

    [Fact]
    public void RecordDeadLetter_ShouldNotThrow()
    {
        MetricsHelper.RecordDeadLetter();
    }

    #endregion

    #region Flow Metrics

    [Fact]
    public void RecordFlowStarted_ShouldNotThrow()
    {
        MetricsHelper.RecordFlowStarted();
    }

    [Fact]
    public void RecordFlowCompleted_ShouldNotThrow()
    {
        var start = MetricsHelper.StartTimestamp();
        MetricsHelper.RecordFlowCompleted(start);
    }

    [Fact]
    public void RecordFlowFailed_ShouldNotThrow()
    {
        MetricsHelper.RecordFlowFailed();
    }

    [Fact]
    public void RecordStepExecuted_Success_ShouldNotThrow()
    {
        var start = MetricsHelper.StartTimestamp();
        MetricsHelper.RecordStepExecuted(true, start);
    }

    [Fact]
    public void RecordStepExecuted_Failure_ShouldNotThrow()
    {
        var start = MetricsHelper.StartTimestamp();
        MetricsHelper.RecordStepExecuted(false, start);
    }

    #endregion

    #region Resilience Metrics

    [Fact]
    public void RecordResilienceRetry_ShouldNotThrow()
    {
        MetricsHelper.RecordResilienceRetry();
    }

    [Fact]
    public void RecordCircuitOpened_ShouldNotThrow()
    {
        MetricsHelper.RecordCircuitOpened();
    }

    #endregion

    #region Message Metrics

    [Fact]
    public void RecordMessagePublished_ShouldNotThrow()
    {
        MetricsHelper.RecordMessagePublished("Transport.InMemory", "TestMessage", "test-destination");
    }

    [Fact]
    public void RecordMessageFailed_ShouldNotThrow()
    {
        MetricsHelper.RecordMessageFailed("Transport.InMemory", "test-destination");
    }

    [Fact]
    public void RecordMessageFailed_WithReason_ShouldNotThrow()
    {
        MetricsHelper.RecordMessageFailed("Transport.InMemory", "test-destination", "connection_lost");
    }

    #endregion
}
