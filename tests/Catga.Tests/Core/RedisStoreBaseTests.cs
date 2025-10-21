using Catga.Abstractions;
using Catga.Persistence.Redis;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for RedisStoreBase - DRY pattern base class
/// </summary>
public class RedisStoreBaseTests
{
    [Fact]
    public void Constructor_ValidParameters_ShouldSucceed()
    {
        // Arrange
        var redis = Mock.Of<IConnectionMultiplexer>();
        var serializer = Mock.Of<IMessageSerializer>();
        var keyPrefix = "test:";

        // Act
        var store = new TestRedisStore(redis, serializer, keyPrefix);

        // Assert
        store.Should().NotBeNull();
        store.GetRedis().Should().BeSameAs(redis);
        store.GetSerializer().Should().BeSameAs(serializer);
        store.GetKeyPrefix().Should().Be(keyPrefix);
    }

    [Fact]
    public void Constructor_NullRedis_ShouldThrow()
    {
        // Arrange
        IConnectionMultiplexer redis = null!;
        var serializer = Mock.Of<IMessageSerializer>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestRedisStore(redis, serializer, "test:"));
    }

    [Fact]
    public void Constructor_NullSerializer_ShouldThrow()
    {
        // Arrange
        var redis = Mock.Of<IConnectionMultiplexer>();
        IMessageSerializer serializer = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestRedisStore(redis, serializer, "test:"));
    }

    [Fact]
    public void Constructor_NullKeyPrefix_ShouldThrow()
    {
        // Arrange
        var redis = Mock.Of<IConnectionMultiplexer>();
        var serializer = Mock.Of<IMessageSerializer>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TestRedisStore(redis, serializer, null!));
    }

    [Fact]
    public void GetDatabase_ShouldReturnDatabaseFromRedis()
    {
        // Arrange
        var mockDatabase = Mock.Of<IDatabase>();
        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDatabase);

        var store = new TestRedisStore(mockRedis.Object, Mock.Of<IMessageSerializer>(), "test:");

        // Act
        var db = store.TestGetDatabase();

        // Assert
        db.Should().BeSameAs(mockDatabase);
        mockRedis.Verify(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Once);
    }

    [Theory]
    [InlineData("test:", "suffix", "test:suffix")]
    [InlineData("prefix:", "key", "prefix:key")]
    [InlineData("catga:", "msg", "catga:msg")]
    public void BuildKey_WithStringSuffix_ShouldConcatenate(string prefix, string suffix, string expected)
    {
        // Arrange
        var store = new TestRedisStore(
            Mock.Of<IConnectionMultiplexer>(),
            Mock.Of<IMessageSerializer>(),
            prefix);

        // Act
        var key = store.TestBuildKey(suffix);

        // Assert
        key.Should().Be(expected);
    }

    [Theory]
    [InlineData("test:", 123, "test:123")]
    [InlineData("msg:", 456789, "msg:456789")]
    [InlineData("id:", -1, "id:-1")]
    public void BuildKey_WithLongId_ShouldConvertAndConcatenate(string prefix, long id, string expected)
    {
        // Arrange
        var store = new TestRedisStore(
            Mock.Of<IConnectionMultiplexer>(),
            Mock.Of<IMessageSerializer>(),
            prefix);

        // Act
        var key = store.TestBuildKey(id);

        // Assert
        key.Should().Be(expected);
    }

    [Fact]
    public void BuildKey_WithGuid_ShouldConvertToNFormat()
    {
        // Arrange
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var expectedFormat = "12345678123412341234123456789abc"; // N format (32 chars, no dashes)
        var store = new TestRedisStore(
            Mock.Of<IConnectionMultiplexer>(),
            Mock.Of<IMessageSerializer>(),
            "test:");

        // Act
        var key = store.TestBuildKey(guid);

        // Assert
        key.Should().Be($"test:{expectedFormat}");
    }

    [Fact]
    public void BuildKey_EmptyStringSuffix_ShouldReturnPrefix()
    {
        // Arrange
        var store = new TestRedisStore(
            Mock.Of<IConnectionMultiplexer>(),
            Mock.Of<IMessageSerializer>(),
            "test:");

        // Act
        var key = store.TestBuildKey("");

        // Assert
        key.Should().Be("test:");
    }

    [Fact]
    public void BuildKey_LargeString_ShouldUseFallbackPath()
    {
        // Arrange
        var largeSuffix = new string('x', 300); // Larger than 256 char buffer
        var store = new TestRedisStore(
            Mock.Of<IConnectionMultiplexer>(),
            Mock.Of<IMessageSerializer>(),
            "test:");

        // Act
        var key = store.TestBuildKey(largeSuffix);

        // Assert
        key.Should().Be($"test:{largeSuffix}");
        key.Length.Should().Be(5 + 300); // prefix + suffix
    }

    /// <summary>
    /// Test implementation of RedisStoreBase to expose protected members
    /// </summary>
    private class TestRedisStore : RedisStoreBase
    {
        public TestRedisStore(
            IConnectionMultiplexer redis,
            IMessageSerializer serializer,
            string keyPrefix)
            : base(redis, serializer, keyPrefix)
        {
        }

        // Expose protected members for testing
        public IConnectionMultiplexer GetRedis() => Redis;
        public IMessageSerializer GetSerializer() => Serializer;
        public string GetKeyPrefix() => KeyPrefix;
        public IDatabase TestGetDatabase() => GetDatabase();
        public string TestBuildKey(string suffix) => BuildKey(suffix);
        public string TestBuildKey(long id) => BuildKey(id);
        public string TestBuildKey(Guid id) => BuildKey(id);
    }
}

