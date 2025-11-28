using System.Diagnostics;
using System.Linq;
using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Observability;

public partial class TraceTagsGeneratorTests
{
    [Fact]
    public void TypeLevel_DefaultPrefix_For_Request_ShouldBe_catga_req()
    {
        var req = new Req_Default();
        var enricher = (IActivityTagProvider)req;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var objs1 = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects()) objs1[kv.Key] = kv.Value;
        objs1.Should().ContainKey("catga.req.Id");
        objs1["catga.req.Id"].Should().Be(req.Id);
        act.Tags.Should().Contain(t => t.Key == "catga.req.Name" && t.Value == req.Name);
    }

    [Fact]
    public void TypeLevel_DefaultPrefix_For_Response_ShouldBe_catga_res()
    {
        var res = new Res_Default(42, "OK");
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var objs2 = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects()) objs2[kv.Key] = kv.Value;
        objs2.Should().ContainKey("catga.res.Code");
        objs2["catga.res.Code"].Should().Be(res.Code);
        act.Tags.Should().Contain(t => t.Key == "catga.res.Message" && t.Value == res.Message);
    }

    [Fact]
    public void TypeLevel_Exclude_ShouldRemove_Specified_Properties()
    {
        var req = new Req_Exclude(7, "secret");
        var enricher = (IActivityTagProvider)req;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var objs3 = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects()) objs3[kv.Key] = kv.Value;
        objs3.Should().ContainKey("catga.req.Visible");
        objs3["catga.req.Visible"].Should().Be(req.Visible);
        act.Tags.Should().NotContain(t => t.Key == "catga.req.Sensitive");
    }

    [Fact]
    public void TypeLevel_Include_With_AllPublic_False_ShouldOnlyInclude_Listed()
    {
        var res = new Res_IncludeOnly(99, "skip");
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        var objs4 = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects()) objs4[kv.Key] = kv.Value;
        objs4.Should().ContainKey("catga.res.Code");
        objs4["catga.res.Code"].Should().Be(res.Code);
        act.Tags.Should().NotContain(t => t.Key == "catga.res.Note");
    }

    [Fact]
    public void PropertyLevel_ShouldOverride_TypeLevel_For_Same_Property()
    {
        var req = new Req_PropertyOverride(5, 10);
        var enricher = (IActivityTagProvider)req;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        // X uses custom name, Y uses prefix
        var objs5 = new Dictionary<string, object?>();
        foreach (var kv in act.EnumerateTagObjects()) objs5[kv.Key] = kv.Value;
        objs5.Should().ContainKey("custom.x");
        objs5["custom.x"].Should().Be(req.X);
        objs5.Should().NotContainKey("catga.req.X");
        objs5.Should().ContainKey("catga.req.Y");
        objs5["catga.req.Y"].Should().Be(req.Y);
    }

    // -------- Test Types (partial so generator can emit) --------

    [TraceTags]
    public partial record Req_Default
        : IRequest<Res_Default>
    {
        public int Id { get; init; } = 123;
        public string Name { get; init; } = "abc";
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public long? CorrelationId { get; init; }
    }

    [TraceTags]
    public partial record Res_Default(int Code, string Message);

    [TraceTags(Prefix = "catga.req.", Exclude = new[] { nameof(Req_Exclude.Sensitive) })]
    public partial record Req_Exclude(int Visible, string Sensitive)
        : IRequest<Res_Default>;
    public partial record Req_Exclude
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public long? CorrelationId { get; init; }
    }

    [TraceTags(AllPublic = false, Include = new[] { nameof(Res_IncludeOnly.Code) })]
    public partial record Res_IncludeOnly(int Code, string Note);

    [TraceTags(Prefix = "catga.req.")]
    public partial record Req_PropertyOverride(
        [property: TraceTag("custom.x")] int X,
        int Y) : IRequest<Res_Default>;
    public partial record Req_PropertyOverride
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public long? CorrelationId { get; init; }
    }
}
