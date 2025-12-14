using Catga.Persistence;
using FluentAssertions;

namespace Catga.Tests.Persistence;

public class PersistenceKeyHelperTests
{
    [Fact]
    public void BuildKey_WithPrefixAndId_ReturnsCombinedKey()
    {
        var result = PersistenceKeyHelper.BuildKey("prefix:", "id123");
        result.Should().Be("prefix:id123");
    }

    [Fact]
    public void BuildKey_WithEmptyPrefix_ReturnsIdOnly()
    {
        var result = PersistenceKeyHelper.BuildKey("", "id123");
        result.Should().Be("id123");
    }

    [Fact]
    public void BuildKey_WithEmptyId_ReturnsPrefixOnly()
    {
        var result = PersistenceKeyHelper.BuildKey("prefix:", "");
        result.Should().Be("prefix:");
    }

    [Fact]
    public void WaitKey_ReturnsCorrectFormat()
    {
        var result = PersistenceKeyHelper.WaitKey("flow:", "corr-123");
        result.Should().Be("flow:wait:corr-123");
    }

    [Fact]
    public void ForEachKey_WithoutPrefix_ReturnsCorrectFormat()
    {
        var result = PersistenceKeyHelper.ForEachKey("flow-1", 5);
        result.Should().Be("flow-1:foreach:5");
    }

    [Fact]
    public void ForEachKey_WithPrefix_ReturnsCorrectFormat()
    {
        var result = PersistenceKeyHelper.ForEachKey("prefix:", "flow-1", 5);
        result.Should().Be("prefix:foreach:flow-1:5");
    }

    [Fact]
    public void EncodeNatsKey_ReplacesColons()
    {
        var result = PersistenceKeyHelper.EncodeNatsKey("flow:step:1");
        result.Should().Be("flow_C_step_C_1");
    }

    [Fact]
    public void EncodeNatsKey_ReplacesSlashes()
    {
        var result = PersistenceKeyHelper.EncodeNatsKey("path/to/resource");
        result.Should().Be("path_S_to_S_resource");
    }

    [Fact]
    public void EncodeNatsKey_ReplacesDots()
    {
        var result = PersistenceKeyHelper.EncodeNatsKey("name.space.type");
        result.Should().Be("name_D_space_D_type");
    }

    [Fact]
    public void EncodeNatsKey_ReplacesAllSpecialChars()
    {
        var result = PersistenceKeyHelper.EncodeNatsKey("ns.flow:id/path");
        result.Should().Be("ns_D_flow_C_id_S_path");
    }

    [Fact]
    public void DecodeNatsKey_ReversesEncoding()
    {
        var encoded = PersistenceKeyHelper.EncodeNatsKey("ns.flow:id/path");
        var decoded = PersistenceKeyHelper.DecodeNatsKey(encoded);
        decoded.Should().Be("ns.flow:id/path");
    }

    [Fact]
    public void DecodeNatsKey_HandlesPlainString()
    {
        var result = PersistenceKeyHelper.DecodeNatsKey("plainstring");
        result.Should().Be("plainstring");
    }

    [Fact]
    public void EncodeNatsForEachKey_ReturnsEncodedFormat()
    {
        var result = PersistenceKeyHelper.EncodeNatsForEachKey("flow-1", 3);
        result.Should().Be("flow-1_C_foreach_C_3");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("simple", "simple")]
    [InlineData("a:b:c", "a_C_b_C_c")]
    [InlineData("x/y/z", "x_S_y_S_z")]
    [InlineData("m.n.o", "m_D_n_D_o")]
    public void EncodeNatsKey_HandlesVariousInputs(string input, string expected)
    {
        var result = PersistenceKeyHelper.EncodeNatsKey(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void RoundTrip_EncodeAndDecode_PreservesOriginal()
    {
        var original = "complex:path/with.dots:and/slashes.mixed";
        var encoded = PersistenceKeyHelper.EncodeNatsKey(original);
        var decoded = PersistenceKeyHelper.DecodeNatsKey(encoded);
        decoded.Should().Be(original);
    }
}
