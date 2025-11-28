using System.Diagnostics;
using Catga;
using Catga.Abstractions;
using Catga.Observability;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Observability;

public class TraceTagsAdvancedTests
{
    [Fact]
    public void Nullables_Should_Write_Tags_With_Null_Values()
    {
        var res = new Res_Nullables(null, null);
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var tags = act.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("catga.res.Name");
        tags["catga.res.Name"].Should().BeNull();
        tags.Should().ContainKey("catga.res.Count");
        tags["catga.res.Count"].Should().BeNull();
    }

    [Fact]
    public void Primitives_Should_Write_Typed_Values()
    {
        var res = new Res_Primitives(7, 99L, true);
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var tags = act.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().Contain(new KeyValuePair<string, object?>("catga.res.I", 7));
        tags.Should().Contain(new KeyValuePair<string, object?>("catga.res.L", 99L));
        tags.Should().Contain(new KeyValuePair<string, object?>("catga.res.B", true));
    }

    [Fact]
    public void RecordStruct_Should_Be_Supported()
    {
        var res = new Res_Point(3, 4);
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        act.Tags.Should().Contain(t => t.Key == "catga.res.X" && t.Value?.ToString() == "3");
        act.Tags.Should().Contain(t => t.Key == "catga.res.Y" && t.Value?.ToString() == "4");
    }

    [Fact]
    public void TypeLevel_Exclude_Does_Not_Remove_PropertyLevel_Override()
    {
        var req = new Req_Override(5, 6, 7);
        var enricher = (IActivityTagProvider)req;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var tags = act.Tags.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().ContainKey("x.keep");
        tags.Should().NotContainKey("catga.req.Y");
        tags.Should().ContainKey("catga.req.Z");
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
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => { if (a.OperationName.StartsWith("Catga.Handle.")) captured = a; }
        };
        ActivitySource.AddActivityListener(listener);

        var result = await mediator.SendAsync<Ping, Pong>(new Ping());
        result.IsSuccess.Should().BeTrue();
        captured.Should().BeNull(); // no activity created without WithTracing(true)
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
