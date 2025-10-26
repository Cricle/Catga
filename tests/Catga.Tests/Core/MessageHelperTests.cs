using Catga.Abstractions;
using Catga.Core;
using Catga.DistributedId;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// MessageHelper单元测试
/// 目标覆盖率: 从 0% → 95%+
/// </summary>
public class MessageHelperTests
{
    #region GetOrGenerateMessageId Tests

    [Fact]
    public void GetOrGenerateMessageId_WithExistingMessageId_ShouldReturnExistingId()
    {
        // Arrange
        var request = new TestRequest { MessageId = 12345 };
        var idGenerator = Substitute.For<IDistributedIdGenerator>();

        // Act
        var result = MessageHelper.GetOrGenerateMessageId(request, idGenerator);

        // Assert
        result.Should().Be(12345);
        idGenerator.DidNotReceive().NextId(); // 不应该调用生成器
    }

    [Fact]
    public void GetOrGenerateMessageId_WithZeroMessageId_ShouldGenerateNew()
    {
        // Arrange
        var request = new TestRequest { MessageId = 0 };
        var idGenerator = Substitute.For<IDistributedIdGenerator>();
        idGenerator.NextId().Returns(99999);

        // Act
        var result = MessageHelper.GetOrGenerateMessageId(request, idGenerator);

        // Assert
        result.Should().Be(99999);
        idGenerator.Received(1).NextId();
    }

    [Fact]
    public void GetOrGenerateMessageId_WithNonIMessage_ShouldGenerateNew()
    {
        // Arrange
        var request = new NonMessageRequest();
        var idGenerator = Substitute.For<IDistributedIdGenerator>();
        idGenerator.NextId().Returns(55555);

        // Act
        var result = MessageHelper.GetOrGenerateMessageId(request, idGenerator);

        // Assert
        result.Should().Be(55555);
        idGenerator.Received(1).NextId();
    }

    [Fact]
    public void GetOrGenerateMessageId_WithNegativeMessageId_ShouldReturnNegativeId()
    {
        // Arrange - 虽然不推荐，但MessageId可以是负数
        var request = new TestRequest { MessageId = -1 };
        var idGenerator = Substitute.For<IDistributedIdGenerator>();

        // Act
        var result = MessageHelper.GetOrGenerateMessageId(request, idGenerator);

        // Assert
        result.Should().Be(-1);
        idGenerator.DidNotReceive().NextId();
    }

    [Fact]
    public void GetOrGenerateMessageId_WithMaxValueMessageId_ShouldReturnMaxValue()
    {
        // Arrange
        var request = new TestRequest { MessageId = long.MaxValue };
        var idGenerator = Substitute.For<IDistributedIdGenerator>();

        // Act
        var result = MessageHelper.GetOrGenerateMessageId(request, idGenerator);

        // Assert
        result.Should().Be(long.MaxValue);
        idGenerator.DidNotReceive().NextId();
    }

    [Fact]
    public void GetOrGenerateMessageId_CalledMultipleTimes_ShouldGenerateDifferentIds()
    {
        // Arrange
        var request = new NonMessageRequest();
        var idGenerator = Substitute.For<IDistributedIdGenerator>();
        idGenerator.NextId().Returns(1000, 2000, 3000);

        // Act
        var result1 = MessageHelper.GetOrGenerateMessageId(request, idGenerator);
        var result2 = MessageHelper.GetOrGenerateMessageId(request, idGenerator);
        var result3 = MessageHelper.GetOrGenerateMessageId(request, idGenerator);

        // Assert
        result1.Should().Be(1000);
        result2.Should().Be(2000);
        result3.Should().Be(3000);
        idGenerator.Received(3).NextId();
    }

    #endregion

    #region GetMessageType Tests

    [Fact]
    public void GetMessageType_ShouldReturnFullTypeName()
    {
        // Act
        var result = MessageHelper.GetMessageType<TestRequest>();

        // Assert
        result.Should().Contain("TestRequest");
        result.Should().Contain("Catga.Tests.Core");
    }

    [Fact]
    public void GetMessageType_WithDifferentTypes_ShouldReturnDifferentNames()
    {
        // Act
        var result1 = MessageHelper.GetMessageType<TestRequest>();
        var result2 = MessageHelper.GetMessageType<NonMessageRequest>();

        // Assert
        result1.Should().NotBe(result2);
        result1.Should().Contain("TestRequest");
        result2.Should().Contain("NonMessageRequest");
    }

    [Fact]
    public void GetMessageType_CalledMultipleTimes_ShouldReturnSameValue()
    {
        // Act - 应该使用缓存
        var result1 = MessageHelper.GetMessageType<TestRequest>();
        var result2 = MessageHelper.GetMessageType<TestRequest>();
        var result3 = MessageHelper.GetMessageType<TestRequest>();

        // Assert
        result1.Should().Be(result2);
        result2.Should().Be(result3);
    }

    [Fact]
    public void GetMessageType_WithGenericType_ShouldIncludeGenericInfo()
    {
        // Act
        var result = MessageHelper.GetMessageType<GenericRequest<string>>();

        // Assert
        result.Should().Contain("GenericRequest");
        result.Should().Contain("String");
    }

    [Fact]
    public void GetMessageType_WithNestedType_ShouldIncludeNestedInfo()
    {
        // Act
        var result = MessageHelper.GetMessageType<NestedRequest.InnerRequest>();

        // Assert
        result.Should().Contain("NestedRequest");
        result.Should().Contain("InnerRequest");
    }

    #endregion

    #region GetCorrelationId Tests

    [Fact]
    public void GetCorrelationId_WithIMessage_ShouldReturnCorrelationId()
    {
        // Arrange
        var request = new TestRequest 
        { 
            MessageId = 123, 
            CorrelationId = 456 
        };

        // Act
        var result = MessageHelper.GetCorrelationId(request);

        // Assert
        result.Should().Be(456);
    }

    [Fact]
    public void GetCorrelationId_WithNullCorrelationId_ShouldReturnNull()
    {
        // Arrange
        var request = new TestRequest 
        { 
            MessageId = 123, 
            CorrelationId = null 
        };

        // Act
        var result = MessageHelper.GetCorrelationId(request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetCorrelationId_WithNonIMessage_ShouldReturnNull()
    {
        // Arrange
        var request = new NonMessageRequest();

        // Act
        var result = MessageHelper.GetCorrelationId(request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetCorrelationId_WithZeroCorrelationId_ShouldReturnZero()
    {
        // Arrange
        var request = new TestRequest 
        { 
            MessageId = 123, 
            CorrelationId = 0 
        };

        // Act
        var result = MessageHelper.GetCorrelationId(request);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GetCorrelationId_WithMaxValueCorrelationId_ShouldReturnMaxValue()
    {
        // Arrange
        var request = new TestRequest 
        { 
            MessageId = 123, 
            CorrelationId = long.MaxValue 
        };

        // Act
        var result = MessageHelper.GetCorrelationId(request);

        // Assert
        result.Should().Be(long.MaxValue);
    }

    [Fact]
    public void GetCorrelationId_WithNegativeCorrelationId_ShouldReturnNegativeValue()
    {
        // Arrange
        var request = new TestRequest 
        { 
            MessageId = 123, 
            CorrelationId = -999 
        };

        // Act
        var result = MessageHelper.GetCorrelationId(request);

        // Assert
        result.Should().Be(-999);
    }

    #endregion

    #region Test Helper Classes

    private class TestRequest : IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
    }

    private class NonMessageRequest
    {
        public string Name { get; set; } = "Test";
    }

    private class GenericRequest<T> : IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public T? Data { get; set; }
    }

    private class NestedRequest
    {
        public class InnerRequest : IMessage
        {
            public long MessageId { get; init; }
            public long? CorrelationId { get; init; }
        }
    }

    #endregion
}

