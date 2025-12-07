using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for ImmutabilityVerifier.
/// </summary>
public class ImmutabilityVerifierTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly ImmutabilityVerifier _verifier;

    public ImmutabilityVerifierTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _verifier = new ImmutabilityVerifier(_eventStore);
    }

    [Fact]
    public async Task VerifyStreamAsync_ValidStream_ReturnsValid()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [
            new TestEvent { Data = "event-1" },
            new TestEvent { Data = "event-2" }
        ]);

        // Act
        var result = await _verifier.VerifyStreamAsync("stream-1");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Hash.Should().NotBeNullOrEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task VerifyStreamAsync_EmptyStream_ReturnsInvalid()
    {
        // Act
        var result = await _verifier.VerifyStreamAsync("non-existent");

        // Assert - empty stream is invalid
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyStreamAsync_SameStream_ProducesSameHash()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent { Data = "test" }]);

        // Act - verify same stream twice
        var result1 = await _verifier.VerifyStreamAsync("stream-1");
        var result2 = await _verifier.VerifyStreamAsync("stream-1");

        // Assert - same stream should produce same hash
        result1.Hash.Should().Be(result2.Hash);
    }

    [Fact]
    public async Task VerifyStreamAsync_DifferentEvents_ProduceDifferentHash()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent { Data = "test-1" }]);
        await _eventStore.AppendAsync("stream-2", [new TestEvent { Data = "test-2" }]);

        // Act
        var result1 = await _verifier.VerifyStreamAsync("stream-1");
        var result2 = await _verifier.VerifyStreamAsync("stream-2");

        // Assert
        result1.Hash.Should().NotBe(result2.Hash);
    }

    [Fact]
    public async Task VerifyStreamAsync_HashIsConsistent()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [
            new TestEvent { Data = "a" },
            new TestEvent { Data = "b" },
            new TestEvent { Data = "c" }
        ]);

        // Act - verify multiple times
        var result1 = await _verifier.VerifyStreamAsync("stream-1");
        var result2 = await _verifier.VerifyStreamAsync("stream-1");
        var result3 = await _verifier.VerifyStreamAsync("stream-1");

        // Assert - hash should be consistent
        result1.Hash.Should().Be(result2.Hash);
        result2.Hash.Should().Be(result3.Hash);
    }

    #region Test helpers

    private record TestEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string Data { get; init; } = "";
    }

    #endregion
}
