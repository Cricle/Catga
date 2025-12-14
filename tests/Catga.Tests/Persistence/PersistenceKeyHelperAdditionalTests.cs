using Catga.Persistence;
using FluentAssertions;

namespace Catga.Tests.Persistence;

public class PersistenceKeyHelperAdditionalTests
{
    [Fact]
    public void BuildKey_WithPrefix_IncludesPrefix()
    {
        var key = PersistenceKeyHelper.BuildKey("prefix", "id123");
        key.Should().StartWith("prefix");
    }

    [Fact]
    public void BuildKey_WithId_IncludesId()
    {
        var key = PersistenceKeyHelper.BuildKey("prefix", "my-id");
        key.Should().Contain("my-id");
    }

    [Fact]
    public void BuildKey_EmptyPrefix_OnlyReturnsId()
    {
        var key = PersistenceKeyHelper.BuildKey("", "id123");
        key.Should().Be("id123");
    }

    [Fact]
    public void BuildKey_SpecialCharacters_HandledCorrectly()
    {
        var key = PersistenceKeyHelper.BuildKey("prefix", "id:with:colons");
        key.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BuildKey_LongId_Works()
    {
        var longId = new string('a', 1000);
        var key = PersistenceKeyHelper.BuildKey("prefix", longId);
        key.Should().Contain(longId);
    }

    [Fact]
    public void BuildKey_UnicodId_Works()
    {
        var key = PersistenceKeyHelper.BuildKey("prefix", "id-中文-日本語");
        key.Should().Contain("中文");
    }

    [Theory]
    [InlineData("flow", "order-123")]
    [InlineData("snapshot", "state-1")]
    [InlineData("event", "evt-abc")]
    public void BuildKey_VariousCombinations_ContainsBothParts(string prefix, string id)
    {
        var key = PersistenceKeyHelper.BuildKey(prefix, id);
        key.Should().Contain(prefix);
        key.Should().Contain(id);
    }
}
