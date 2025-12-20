using Catga.Abstractions;
using Catga.DistributedId;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Abstractions;

/// <summary>
/// Comprehensive tests for MessageId and CorrelationId
/// </summary>
public class MessageIdentifiersComprehensiveTests
{
    #region MessageId Tests

    [Fact]
    public void MessageId_Constructor_ShouldSetValue()
    {
        var id = new MessageId(12345);
        id.Value.Should().Be(12345);
    }

    [Fact]
    public void MessageId_ImplicitConversionToLong_ShouldWork()
    {
        var id = new MessageId(12345);
        long value = id;
        value.Should().Be(12345);
    }

    [Fact]
    public void MessageId_ExplicitConversionFromLong_ShouldWork()
    {
        var id = (MessageId)12345L;
        id.Value.Should().Be(12345);
    }

    [Fact]
    public void MessageId_Parse_ShouldParseValidString()
    {
        var id = MessageId.Parse("12345");
        id.Value.Should().Be(12345);
    }

    [Fact]
    public void MessageId_Parse_ShouldThrowOnInvalidString()
    {
        var act = () => MessageId.Parse("invalid");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void MessageId_NewId_ShouldUseGenerator()
    {
        var generator = Substitute.For<IDistributedIdGenerator>();
        generator.NextId().Returns(99999L);
        
        var id = MessageId.NewId(generator);
        
        id.Value.Should().Be(99999);
        generator.Received(1).NextId();
    }

    [Fact]
    public void MessageId_Equality_ShouldWorkCorrectly()
    {
        var id1 = new MessageId(123);
        var id2 = new MessageId(123);
        var id3 = new MessageId(456);
        
        id1.Should().Be(id2);
        id1.Should().NotBe(id3);
    }

    [Fact]
    public void MessageId_GetHashCode_ShouldBeConsistent()
    {
        var id1 = new MessageId(123);
        var id2 = new MessageId(123);
        
        id1.GetHashCode().Should().Be(id2.GetHashCode());
    }

    #endregion

    #region CorrelationId Tests

    [Fact]
    public void CorrelationId_Constructor_ShouldSetValue()
    {
        var id = new CorrelationId(12345);
        id.Value.Should().Be(12345);
    }

    [Fact]
    public void CorrelationId_ImplicitConversionToLong_ShouldWork()
    {
        var id = new CorrelationId(12345);
        long value = id;
        value.Should().Be(12345);
    }

    [Fact]
    public void CorrelationId_ExplicitConversionFromLong_ShouldWork()
    {
        var id = (CorrelationId)12345L;
        id.Value.Should().Be(12345);
    }

    [Fact]
    public void CorrelationId_Parse_ShouldParseValidString()
    {
        var id = CorrelationId.Parse("12345");
        id.Value.Should().Be(12345);
    }

    [Fact]
    public void CorrelationId_Parse_ShouldThrowOnInvalidString()
    {
        var act = () => CorrelationId.Parse("invalid");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void CorrelationId_NewId_ShouldUseGenerator()
    {
        var generator = Substitute.For<IDistributedIdGenerator>();
        generator.NextId().Returns(88888L);
        
        var id = CorrelationId.NewId(generator);
        
        id.Value.Should().Be(88888);
        generator.Received(1).NextId();
    }

    [Fact]
    public void CorrelationId_ToString_ShouldReturnValueAsString()
    {
        var id = new CorrelationId(12345);
        id.ToString().Should().Be("12345");
    }

    [Fact]
    public void CorrelationId_Equality_ShouldWorkCorrectly()
    {
        var id1 = new CorrelationId(123);
        var id2 = new CorrelationId(123);
        var id3 = new CorrelationId(456);
        
        id1.Should().Be(id2);
        id1.Should().NotBe(id3);
    }

    [Fact]
    public void CorrelationId_GetHashCode_ShouldBeConsistent()
    {
        var id1 = new CorrelationId(123);
        var id2 = new CorrelationId(123);
        
        id1.GetHashCode().Should().Be(id2.GetHashCode());
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void MessageId_WithZero_ShouldWork()
    {
        var id = new MessageId(0);
        id.Value.Should().Be(0);
    }

    [Fact]
    public void MessageId_WithNegative_ShouldWork()
    {
        var id = new MessageId(-1);
        id.Value.Should().Be(-1);
    }

    [Fact]
    public void MessageId_WithMaxValue_ShouldWork()
    {
        var id = new MessageId(long.MaxValue);
        id.Value.Should().Be(long.MaxValue);
    }

    [Fact]
    public void CorrelationId_WithMinValue_ShouldWork()
    {
        var id = new CorrelationId(long.MinValue);
        id.Value.Should().Be(long.MinValue);
    }

    #endregion
}
