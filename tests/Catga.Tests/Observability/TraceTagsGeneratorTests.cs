using System.Diagnostics;
using Catga.Abstractions;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Observability;

public class TraceTagsGeneratorTests
{
    [Fact]
    public void TypeLevel_DefaultPrefix_For_Request_ShouldBe_catga_req()
    {
        var req = new Req_Default();
        var enricher = (IActivityTagProvider)req;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        act.Tags.Should().Contain(t => t.Key == "catga.req.Id" && t.Value?.ToString() == req.Id.ToString());
        act.Tags.Should().Contain(t => t.Key == "catga.req.Name" && t.Value?.ToString() == req.Name);
    }

    [Fact]
    public void TypeLevel_DefaultPrefix_For_Response_ShouldBe_catga_res()
    {
        var res = new Res_Default(42, "OK");
        var enricher = (IActivityTagProvider)res;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        act.Tags.Should().Contain(t => t.Key == "catga.res.Code" && t.Value?.ToString() == res.Code.ToString());
        act.Tags.Should().Contain(t => t.Key == "catga.res.Message" && t.Value?.ToString() == res.Message);
    }

    [Fact]
    public void TypeLevel_Exclude_ShouldRemove_Specified_Properties()
    {
        var req = new Req_Exclude(7, "secret");
        var enricher = (IActivityTagProvider)req;
        using var act = new Activity("test").Start();
        enricher.Enrich(act);
        act.Stop();

        act.Tags.Should().Contain(t => t.Key == "catga.req.Visible" && t.Value?.ToString() == req.Visible.ToString());
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

        act.Tags.Should().Contain(t => t.Key == "catga.res.Code" && t.Value?.ToString() == res.Code.ToString());
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
        act.Tags.Should().Contain(t => t.Key == "custom.x" && t.Value?.ToString() == req.X.ToString());
        act.Tags.Should().NotContain(t => t.Key == "catga.req.X");
        act.Tags.Should().Contain(t => t.Key == "catga.req.Y" && t.Value?.ToString() == req.Y.ToString());
    }

    // -------- Test Types (partial so generator can emit) --------

    [TraceTags]
    public partial record Req_Default
        : IRequest<Res_Default>
    {
        public int Id { get; init; } = 123;
        public string Name { get; init; } = "abc";
    }

    [TraceTags]
    public partial record Res_Default(int Code, string Message);

    [TraceTags(Prefix = "catga.req.", Exclude = new[] { nameof(Req_Exclude.Sensitive) })]
    public partial record Req_Exclude(int Visible, string Sensitive)
        : IRequest<Res_Default>;

    [TraceTags(AllPublic = false, Include = new[] { nameof(Res_IncludeOnly.Code) })]
    public partial record Res_IncludeOnly(int Code, string Note);

    [TraceTags(Prefix = "catga.req.")]
    public partial record Req_PropertyOverride(
        [property: TraceTag("custom.x")] int X,
        int Y) : IRequest<Res_Default>;
}
