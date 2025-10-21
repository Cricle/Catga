using Catga.Core;
using Catga.DistributedId;
using Xunit;

namespace Catga.Tests.Core;

public class MessageExtensionsTests : IDisposable
{
    public MessageExtensionsTests()
    {
        // Reset to default before each test
        MessageExtensions.SetIdGenerator(null);
    }

    public void Dispose()
    {
        // Cleanup after each test
        MessageExtensions.SetIdGenerator(null);
    }

    [Fact]
    public void NewMessageId_ShouldGenerateUniqueIds()
    {
        // Arrange & Act
        var id1 = MessageExtensions.NewMessageId();
        var id2 = MessageExtensions.NewMessageId();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.True(id1 > 0);
        Assert.True(id2 > 0);
    }

    [Fact]
    public void NewCorrelationId_ShouldGenerateUniqueIds()
    {
        // Arrange & Act
        var id1 = MessageExtensions.NewCorrelationId();
        var id2 = MessageExtensions.NewCorrelationId();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.True(id1 > 0);
        Assert.True(id2 > 0);
    }

    [Fact]
    public void UseWorkerId_ShouldSetCustomWorkerId()
    {
        // Arrange
        const int expectedWorkerId = 42;
        MessageExtensions.UseWorkerId(expectedWorkerId);

        // Act
        var id = MessageExtensions.NewMessageId();

        // Assert
        var generator = new SnowflakeIdGenerator(expectedWorkerId);
        generator.ParseId(id, out var metadata);
        Assert.Equal(expectedWorkerId, metadata.WorkerId);
    }

    [Fact]
    public void UseWorkerId_WithInvalidWorkerId_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MessageExtensions.UseWorkerId(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MessageExtensions.UseWorkerId(256));
    }

    [Fact]
    public void SetIdGenerator_WithCustomGenerator_ShouldUseIt()
    {
        // Arrange
        const int customWorkerId = 123;
        var customGenerator = new SnowflakeIdGenerator(customWorkerId);
        MessageExtensions.SetIdGenerator(customGenerator);

        // Act
        var id = MessageExtensions.NewMessageId();

        // Assert
        customGenerator.ParseId(id, out var metadata);
        Assert.Equal(customWorkerId, metadata.WorkerId);
    }

    [Fact]
    public void SetIdGenerator_WithNull_ShouldResetToDefault()
    {
        // Arrange
        MessageExtensions.UseWorkerId(99);
        var idWithCustom = MessageExtensions.NewMessageId();

        // Act - Reset to default
        MessageExtensions.SetIdGenerator(null);
        var idWithDefault = MessageExtensions.NewMessageId();

        // Assert - Both should be valid but potentially different WorkerIds
        Assert.True(idWithCustom > 0);
        Assert.True(idWithDefault > 0);
        Assert.NotEqual(idWithCustom, idWithDefault);
    }

    [Fact]
    public void NewMessageId_WithGenerator_ShouldUseProvidedGenerator()
    {
        // Arrange
        const int specificWorkerId = 77;
        var generator = new SnowflakeIdGenerator(specificWorkerId);

        // Act
        var id = MessageExtensions.NewMessageId(generator);

        // Assert
        generator.ParseId(id, out var metadata);
        Assert.Equal(specificWorkerId, metadata.WorkerId);
    }

    [Fact]
    public void NewCorrelationId_WithGenerator_ShouldUseProvidedGenerator()
    {
        // Arrange
        const int specificWorkerId = 88;
        var generator = new SnowflakeIdGenerator(specificWorkerId);

        // Act
        var id = MessageExtensions.NewCorrelationId(generator);

        // Assert
        generator.ParseId(id, out var metadata);
        Assert.Equal(specificWorkerId, metadata.WorkerId);
    }

    [Fact]
    public void MultipleWorkerIds_ShouldGenerateNonConflictingIds()
    {
        // Arrange
        var generator1 = new SnowflakeIdGenerator(1);
        var generator2 = new SnowflakeIdGenerator(2);

        // Act
        var ids1 = Enumerable.Range(0, 100).Select(_ => MessageExtensions.NewMessageId(generator1)).ToList();
        var ids2 = Enumerable.Range(0, 100).Select(_ => MessageExtensions.NewMessageId(generator2)).ToList();

        // Assert - No conflicts between different WorkerIds
        var allIds = ids1.Concat(ids2).ToList();
        Assert.Equal(200, allIds.Count);
        Assert.Equal(200, allIds.Distinct().Count()); // All unique
    }

    [Fact]
    public void UseWorkerId_ShouldAffectBothMessageIdAndCorrelationId()
    {
        // Arrange
        const int workerId = 55;
        MessageExtensions.UseWorkerId(workerId);

        // Act
        var messageId = MessageExtensions.NewMessageId();
        var correlationId = MessageExtensions.NewCorrelationId();

        // Assert
        var generator = new SnowflakeIdGenerator(workerId);
        generator.ParseId(messageId, out var msgMetadata);
        generator.ParseId(correlationId, out var corrMetadata);

        Assert.Equal(workerId, msgMetadata.WorkerId);
        Assert.Equal(workerId, corrMetadata.WorkerId);
    }
}

