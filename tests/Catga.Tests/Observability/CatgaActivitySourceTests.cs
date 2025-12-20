using System.Diagnostics;
using Catga.Observability;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Observability;

/// <summary>
/// Comprehensive tests for CatgaActivitySource.
/// </summary>
public class CatgaActivitySourceTests
{
    [Fact]
    public void SourceName_ShouldBeCatga()
    {
        CatgaActivitySource.SourceName.Should().Be("Catga");
    }

    [Fact]
    public void Source_ShouldNotBeNull()
    {
        CatgaActivitySource.Source.Should().NotBeNull();
    }

    [Fact]
    public void Tags_ShouldHaveCorrectValues()
    {
        CatgaActivitySource.Tags.CatgaType.Should().Be("catga.type");
        CatgaActivitySource.Tags.MessageId.Should().Be("catga.message.id");
        CatgaActivitySource.Tags.MessageType.Should().Be("catga.message.type");
        CatgaActivitySource.Tags.CorrelationId.Should().Be("catga.correlation_id");
        CatgaActivitySource.Tags.RequestType.Should().Be("catga.request.type");
        CatgaActivitySource.Tags.Success.Should().Be("catga.success");
        CatgaActivitySource.Tags.Error.Should().Be("catga.error");
        CatgaActivitySource.Tags.ErrorType.Should().Be("catga.error.type");
        CatgaActivitySource.Tags.Duration.Should().Be("catga.duration.ms");
        CatgaActivitySource.Tags.EventType.Should().Be("catga.event.type");
        CatgaActivitySource.Tags.HandlerType.Should().Be("catga.handler.type");
        CatgaActivitySource.Tags.HandlerCount.Should().Be("catga.handler.count");
        CatgaActivitySource.Tags.AggregateId.Should().Be("catga.aggregate.id");
        CatgaActivitySource.Tags.AggregateType.Should().Be("catga.aggregate.type");
        CatgaActivitySource.Tags.AggregateVersion.Should().Be("catga.aggregate.version");
        CatgaActivitySource.Tags.CommandResult.Should().Be("catga.command.result");
        CatgaActivitySource.Tags.StreamId.Should().Be("catga.stream_id");
        CatgaActivitySource.Tags.EventCount.Should().Be("catga.event_count");
        CatgaActivitySource.Tags.MessagingMessageId.Should().Be("messaging.message.id");
        CatgaActivitySource.Tags.MessagingDestination.Should().Be("messaging.destination.name");
        CatgaActivitySource.Tags.MessagingSystem.Should().Be("messaging.system");
        CatgaActivitySource.Tags.LockResource.Should().Be("catga.lock.resource");
        CatgaActivitySource.Tags.FlowName.Should().Be("catga.flow.name");
        CatgaActivitySource.Tags.FlowId.Should().Be("catga.flow.id");
        CatgaActivitySource.Tags.FlowStatus.Should().Be("catga.flow.status");
        CatgaActivitySource.Tags.StepIndex.Should().Be("catga.flow.step.index");
        CatgaActivitySource.Tags.StepType.Should().Be("catga.flow.step.type");
        CatgaActivitySource.Tags.StepTag.Should().Be("catga.flow.step.tag");
        CatgaActivitySource.Tags.StepStatus.Should().Be("catga.flow.step.status");
    }

    [Fact]
    public void FlowTags_ShouldMatchTags()
    {
        CatgaActivitySource.FlowTags.FlowName.Should().Be(CatgaActivitySource.Tags.FlowName);
        CatgaActivitySource.FlowTags.FlowId.Should().Be(CatgaActivitySource.Tags.FlowId);
        CatgaActivitySource.FlowTags.FlowStatus.Should().Be(CatgaActivitySource.Tags.FlowStatus);
        CatgaActivitySource.FlowTags.StepIndex.Should().Be(CatgaActivitySource.Tags.StepIndex);
        CatgaActivitySource.FlowTags.StepType.Should().Be(CatgaActivitySource.Tags.StepType);
        CatgaActivitySource.FlowTags.StepTag.Should().Be(CatgaActivitySource.Tags.StepTag);
        CatgaActivitySource.FlowTags.StepStatus.Should().Be(CatgaActivitySource.Tags.StepStatus);
        CatgaActivitySource.FlowTags.Error.Should().Be(CatgaActivitySource.Tags.Error);
        CatgaActivitySource.FlowTags.Duration.Should().Be(CatgaActivitySource.Tags.Duration);
    }

    [Fact]
    public void Events_ShouldHaveCorrectValues()
    {
        CatgaActivitySource.Events.AggregateLoaded.Should().Be("catga.aggregate.loaded");
        CatgaActivitySource.Events.AggregateCreated.Should().Be("catga.aggregate.created");
        CatgaActivitySource.Events.EventPublished.Should().Be("catga.event.published");
        CatgaActivitySource.Events.OutboxSaved.Should().Be("Outbox.Saved");
        CatgaActivitySource.Events.OutboxPublished.Should().Be("Outbox.Published");
        CatgaActivitySource.Events.InboxTryLockOk.Should().Be("Inbox.TryLock.Ok");
        CatgaActivitySource.Events.InboxMarkProcessed.Should().Be("Inbox.MarkProcessed");
        CatgaActivitySource.Events.EventStoreAppendDone.Should().Be("EventStore.Append.Done");
        CatgaActivitySource.Events.EventStoreReadDone.Should().Be("EventStore.Read.Done");
        CatgaActivitySource.Events.PipelineBehaviorStart.Should().Be("Pipeline.Behavior.Start");
        CatgaActivitySource.Events.PipelineBehaviorDone.Should().Be("Pipeline.Behavior.Done");
        CatgaActivitySource.Events.LockAcquired.Should().Be("Lock.Acquired");
        CatgaActivitySource.Events.LockReleased.Should().Be("Lock.Released");
        CatgaActivitySource.Events.FlowStarted.Should().Be("catga.flow.started");
        CatgaActivitySource.Events.FlowCompleted.Should().Be("catga.flow.completed");
        CatgaActivitySource.Events.FlowFailed.Should().Be("catga.flow.failed");
        CatgaActivitySource.Events.StepStarted.Should().Be("catga.flow.step.started");
        CatgaActivitySource.Events.StepCompleted.Should().Be("catga.flow.step.completed");
        CatgaActivitySource.Events.StepFailed.Should().Be("catga.flow.step.failed");
    }

    [Fact]
    public void FlowEvents_ShouldMatchEvents()
    {
        CatgaActivitySource.FlowEvents.FlowStarted.Should().Be(CatgaActivitySource.Events.FlowStarted);
        CatgaActivitySource.FlowEvents.FlowCompleted.Should().Be(CatgaActivitySource.Events.FlowCompleted);
        CatgaActivitySource.FlowEvents.FlowFailed.Should().Be(CatgaActivitySource.Events.FlowFailed);
        CatgaActivitySource.FlowEvents.StepStarted.Should().Be(CatgaActivitySource.Events.StepStarted);
        CatgaActivitySource.FlowEvents.StepCompleted.Should().Be(CatgaActivitySource.Events.StepCompleted);
        CatgaActivitySource.FlowEvents.StepFailed.Should().Be(CatgaActivitySource.Events.StepFailed);
    }

    [Fact]
    public void SetError_ShouldSetActivityTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CatgaActivitySource.Source.StartActivity("TestActivity");
        if (activity != null)
        {
            var ex = new InvalidOperationException("Test error message");
            activity.SetError(ex);

            activity.Status.Should().Be(ActivityStatusCode.Error);
            activity.GetTagItem(CatgaActivitySource.Tags.Success).Should().Be(false);
            activity.GetTagItem(CatgaActivitySource.Tags.Error).Should().Be("Test error message");
            activity.GetTagItem(CatgaActivitySource.Tags.ErrorType).Should().Be("InvalidOperationException");
        }
    }

    [Fact]
    public void AddActivityEvent_ShouldAddEvent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CatgaActivitySource.Source.StartActivity("TestActivity");
        if (activity != null)
        {
            activity.AddActivityEvent("TestEvent", ("key1", "value1"), ("key2", 42));
            activity.Events.Should().ContainSingle(e => e.Name == "TestEvent");
        }
    }

    [Fact]
    public void AddActivityEvent_WithEmptyTags_ShouldWork()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CatgaActivitySource.Source.StartActivity("TestActivity");
        if (activity != null)
        {
            activity.AddActivityEvent("EmptyEvent");
            activity.Events.Should().ContainSingle(e => e.Name == "EmptyEvent");
        }
    }

    [Fact]
    public void StartFlowActivity_ShouldCreateActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CatgaActivitySource.StartFlowActivity("TestFlow");
        if (activity != null)
        {
            activity.GetTagItem(CatgaActivitySource.Tags.FlowName).Should().Be("TestFlow");
        }
    }

    [Fact]
    public void StartFlowActivity_WithFlowId_ShouldSetFlowId()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CatgaActivitySource.StartFlowActivity("TestFlow", "flow-123");
        if (activity != null)
        {
            activity.GetTagItem(CatgaActivitySource.Tags.FlowName).Should().Be("TestFlow");
            activity.GetTagItem(CatgaActivitySource.Tags.FlowId).Should().Be("flow-123");
        }
    }

    [Fact]
    public void StartStepActivity_ShouldCreateActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = CatgaActivitySource.StartStepActivity("TestFlow", 0, "Execute");
        if (activity != null)
        {
            activity.GetTagItem(CatgaActivitySource.Tags.FlowName).Should().Be("TestFlow");
            activity.GetTagItem(CatgaActivitySource.Tags.StepIndex).Should().Be(0);
            activity.GetTagItem(CatgaActivitySource.Tags.StepType).Should().Be("Execute");
        }
    }
}
