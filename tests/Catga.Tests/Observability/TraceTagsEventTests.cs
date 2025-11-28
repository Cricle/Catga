using System.Diagnostics;
using Catga;
using Catga.Abstractions;
using Catga.Observability;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Observability;

public class TraceTagsEventTests
{
    [Fact]
    public async Task Publish_Event_Should_Enrich_Handler_Activity_With_Tags()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga().WithTracing(true);
        services.AddScoped<IEventHandler<MyTaggedEvent>, MyTaggedEventHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.StartsWith("Handle: ") && a.Tags.Any())
                    captured = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var evt = new MyTaggedEvent(12345, "test-reason") { CorrelationId = MessageExtensions.NewMessageId() };
        await mediator.PublishAsync(evt);

        captured.Should().NotBeNull();
        captured!.Tags.Should().Contain(t => t.Key == "catga.evt.OrderId" && t.Value?.ToString() == "12345");
        captured!.Tags.Should().Contain(t => t.Key == "catga.evt.Reason" && (string?)t.Value == "test-reason");
    }

    [TraceTags(Prefix = "catga.evt.")]
    public partial record MyTaggedEvent(int OrderId, string Reason) : IEvent
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public long? CorrelationId { get; init; }
    }

    public class MyTaggedEventHandler : IEventHandler<MyTaggedEvent>
    {
        public Task HandleAsync(MyTaggedEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
