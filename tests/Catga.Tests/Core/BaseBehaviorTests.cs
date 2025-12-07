using Catga.Abstractions;
using Catga.Core;
using Catga.DistributedId;
using Catga.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Core;

public class BaseBehaviorTests
{
    private readonly ILogger<TestBehavior> _mockLogger;
    private readonly IDistributedIdGenerator _mockIdGenerator;

    public BaseBehaviorTests()
    {
        _mockLogger = Substitute.For<ILogger<TestBehavior>>();
        _mockIdGenerator = Substitute.For<IDistributedIdGenerator>();
    }

    // ==================== GetRequestName ====================

    [Fact]
    public void GetRequestName_ShouldReturnRequestTypeName()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);

        // Act
        var name = behavior.PublicGetRequestName();

        // Assert
        name.Should().Be(nameof(TestRequest));
    }

    [Fact]
    public void GetRequestFullName_ShouldReturnFullRequestTypeName()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);

        // Act
        var fullName = behavior.PublicGetRequestFullName();

        // Assert
        fullName.Should().Contain("Catga.Tests.Core.BaseBehaviorTests+TestRequest");
    }

    [Fact]
    public void GetResponseName_ShouldReturnResponseTypeName()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);

        // Act
        var name = behavior.PublicGetResponseName();

        // Assert
        name.Should().Be(nameof(TestResponse));
    }

    // ==================== TryGetMessageId ====================

    [Fact]
    public void TryGetMessageId_WithIMessage_ShouldReturnMessageId()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 12345 };

        // Act
        var messageId = behavior.PublicTryGetMessageId(request);

        // Assert
        messageId.Should().Be(12345);
    }

    [Fact]
    public void TryGetMessageId_WithZeroMessageId_ShouldReturnNull()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 0 };

        // Act
        var messageId = behavior.PublicTryGetMessageId(request);

        // Assert
        messageId.Should().BeNull();
    }


    // ==================== TryGetCorrelationId ====================

    [Fact]
    public void TryGetCorrelationId_WithIMessage_ShouldReturnCorrelationId()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 123, CorrelationId = 99999 };

        // Act
        var correlationId = behavior.PublicTryGetCorrelationId(request);

        // Assert
        correlationId.Should().Be(99999);
    }

    [Fact]
    public void TryGetCorrelationId_WithZeroCorrelationId_ShouldReturnZero()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 123, CorrelationId = 0 };

        // Act
        var correlationId = behavior.PublicTryGetCorrelationId(request);

        // Assert
        correlationId.Should().Be(0);
    }


    // ==================== GetCorrelationId ====================

    [Fact]
    public void GetCorrelationId_WithExistingCorrelationId_ShouldReturnIt()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 123, CorrelationId = 55555 };
        _mockIdGenerator.NextId().Returns(99999);

        // Act
        var correlationId = behavior.PublicGetCorrelationId(request, _mockIdGenerator);

        // Assert
        correlationId.Should().Be(55555);
        _mockIdGenerator.DidNotReceive().NextId();
    }

    [Fact]
    public void GetCorrelationId_WithZeroCorrelationId_ShouldReturnZero()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 123, CorrelationId = 0 };
        _mockIdGenerator.NextId().Returns(88888);

        // Act
        var correlationId = behavior.PublicGetCorrelationId(request, _mockIdGenerator);

        // Assert
        correlationId.Should().Be(0);
        _mockIdGenerator.DidNotReceive().NextId();
    }

    [Fact]
    public void GetCorrelationId_WithNullCorrelationId_ShouldGenerateNew()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 123 }; // CorrelationId not set
        _mockIdGenerator.NextId().Returns(77777);

        // Act
        var correlationId = behavior.PublicGetCorrelationId(request, _mockIdGenerator);

        // Assert
        correlationId.Should().Be(77777);
        _mockIdGenerator.Received(1).NextId();
    }

    // ==================== Integration Tests ====================

    [Fact]
    public async Task HandleAsync_ShouldExecuteSuccessfully()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 123 };
        var expectedResponse = new TestResponse { Result = "success" };

        ValueTask<CatgaResult<TestResponse>> Next() =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, Next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleCalls_ShouldHandleEachIndependently()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var requests = new[]
        {
            new TestRequest { MessageId = 1 },
            new TestRequest { MessageId = 2 },
            new TestRequest { MessageId = 3 }
        };

        // Act
        var tasks = requests.Select(async r =>
        {
            ValueTask<CatgaResult<TestResponse>> Next() =>
                new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(
                    new TestResponse { Result = $"response-{r.MessageId}" }));

            return await behavior.HandleAsync(r, Next, CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public void Logger_ShouldBeAccessibleToDerivedClasses()
    {
        // Arrange & Act
        var behavior = new TestBehavior(_mockLogger);

        // Assert
        behavior.PublicLogger.Should().BeSameAs(_mockLogger);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldStoreNull()
    {
        // Arrange & Act
        var behavior = new TestBehavior(null!);

        // Assert
        behavior.PublicLogger.Should().BeNull();
    }

    // ==================== Edge Cases ====================

    [Fact]
    public void TryGetMessageId_WithNegativeMessageId_ShouldReturnIt()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = -123 };

        // Act
        var messageId = behavior.PublicTryGetMessageId(request);

        // Assert
        messageId.Should().Be(-123);
    }

    [Fact]
    public void GetCorrelationId_WithNegativeCorrelationId_ShouldReturnIt()
    {
        // Arrange
        var behavior = new TestBehavior(_mockLogger);
        var request = new TestRequest { MessageId = 123, CorrelationId = -999 };

        // Act
        var correlationId = behavior.PublicGetCorrelationId(request, _mockIdGenerator);

        // Assert
        correlationId.Should().Be(-999);
        _mockIdGenerator.DidNotReceive().NextId();
    }

    // ==================== Test Helpers ====================

    public class TestBehavior : BaseBehavior<TestRequest, TestResponse>
    {
        public TestBehavior(ILogger logger) : base(logger) { }

        public override ValueTask<CatgaResult<TestResponse>> HandleAsync(
            TestRequest request,
            PipelineDelegate<TestResponse> next,
            CancellationToken cancellationToken = default)
        {
            return next();
        }

        // Public wrappers for protected methods
        public string PublicGetRequestName() => GetRequestName();
        public string PublicGetRequestFullName() => GetRequestFullName();
        public string PublicGetResponseName() => GetResponseName();
        public long? PublicTryGetMessageId(TestRequest request) => TryGetMessageId(request);
        public long? PublicTryGetCorrelationId(TestRequest request) => TryGetCorrelationId(request);
        public long PublicGetCorrelationId(TestRequest request, IDistributedIdGenerator idGenerator) =>
            GetCorrelationId(request, idGenerator);
        public ILogger PublicLogger => Logger;
    }


    public record TestRequest : IRequest<TestResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
    }

    public record TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }
}

