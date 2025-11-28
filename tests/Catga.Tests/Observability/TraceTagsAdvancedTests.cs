using System.Diagnostics;
using System.Linq;
using Catga;
using Catga.Abstractions;
using Catga.Observability;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Observability;

public partial class TraceTagsAdvancedTests
{
    [Fact]
    public void Nullables_Should_Write_Tags_With_Null_Values()
    {
        var res = new Res_Nullables(null, null);
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var tags = new Dictionary<string, string?>();
        foreach (var kv in act.Tags)
            tags[kv.Key] = kv.Value;
        // Activity.SetTag(key, null) removes the tag; assert absence
        tags.Should().NotContainKey("catga.res.Name");
        tags.Should().NotContainKey("catga.res.Count");
    }

    [Fact]
    public void Primitives_Should_Write_Typed_Values()
    {
        var res = new Res_Primitives(7, 99L, true);
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var objs = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects())
            objs[kv.Key] = kv.Value;
        objs.Should().ContainKey("catga.res.I");
        objs["catga.res.I"].Should().Be(7);
        objs.Should().ContainKey("catga.res.L");
        objs["catga.res.L"].Should().Be(99L);
        objs.Should().ContainKey("catga.res.B");
        objs["catga.res.B"].Should().Be(true);
    }

    [Fact]
    public void RecordStruct_Should_Be_Supported()
    {
        var res = new Res_Point(3, 4);
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var objs = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects())
            objs[kv.Key] = kv.Value;
        objs.Should().ContainKey("catga.res.X");
        objs["catga.res.X"].Should().Be(3);
        objs.Should().ContainKey("catga.res.Y");
        objs["catga.res.Y"].Should().Be(4);
    }

    [Fact]
    public void TypeLevel_Exclude_Does_Not_Remove_PropertyLevel_Override()
    {
        var req = new Req_Override(5, 6, 7);
        var enricher = (IActivityTagProvider)req;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var objs = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects())
            objs[kv.Key] = kv.Value;
        objs.Should().ContainKey("x.keep");
        objs.Should().NotContainKey("catga.req.Y");
        objs.Should().ContainKey("catga.req.Z");
    }

    [Fact]
    public async Task Mediator_Without_Tracing_Should_Not_Create_Activities()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Do NOT enable tracing
        services.AddCatga();
        services.AddScoped<IRequestHandler<Ping, Pong>, PingHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        Activity? captured = null;
        using var parent = new Activity("TestScope").Start();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => { if (a.OperationName.StartsWith("Catga.Handle.") && a.ParentId == parent.Id) captured = a; }
        };
        ActivitySource.AddActivityListener(listener);

        var result = await mediator.SendAsync<Ping, Pong>(new Ping());
        result.IsSuccess.Should().BeTrue();
        captured.Should().BeNull(); // no activity created without WithTracing(true) for this scope
        parent.Stop();
    }

    // ---------- Test types ----------

    [TraceTags(Prefix = "catga.res.")]
    public partial record Res_Nullables(string? Name, int? Count);

    [TraceTags] // defaults to res prefix
    public partial record Res_Primitives(int I, long L, bool B);

    [TraceTags(Prefix = "catga.res.")]
    public partial record struct Res_Point(int X, int Y);

    [TraceTags(Prefix = "catga.req.", Exclude = new[] { nameof(Req_Override.Y) })]
    public partial record Req_Override(
        [property: TraceTag("x.keep")] int X,
        int Y,
        int Z) : IRequest<Res_Primitives>;

    public partial record Req_Override
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public long? CorrelationId { get; init; }
    }

    [TraceTags]
    public partial record Ping : IRequest<Pong>
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public long? CorrelationId { get; init; }
    }

    [TraceTags]
    public partial record Pong(int Code);

    public class PingHandler : IRequestHandler<Ping, Pong>
    {
        public Task<CatgaResult<Pong>> HandleAsync(Ping request, CancellationToken cancellationToken = default)
            => Task.FromResult(CatgaResult<Pong>.Success(new Pong(200)));
    }
}
