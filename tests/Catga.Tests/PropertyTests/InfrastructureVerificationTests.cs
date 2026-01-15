using Catga.Tests.PropertyTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// FsCheck 属性测试验证
/// 需要无参构造函数
/// </summary>
[Trait("Category", "Infrastructure")]
public class FsCheckPropertyVerificationTests
{
    [Property(MaxTest = 10)]
    public Property FsCheck_SimpleProperty_Works()
    {
        return Prop.ForAll<int>(x => x + 0 == x);
    }

    [Property(MaxTest = 10)]
    public Property FsCheck_StringProperty_Works()
    {
        return Prop.ForAll<NonEmptyString>(s => !string.IsNullOrEmpty(s.Get));
    }

    [Property(MaxTest = PropertyTestConfig.QuickMaxTest)]
    public Property PropertyTestConfig_QuickMaxTest_Works()
    {
        return Prop.ForAll<int>(x => true);
    }

    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property PropertyTestConfig_DefaultMaxTest_Works()
    {
        return Prop.ForAll<int>(x => true);
    }
}

/// <summary>
/// 测试基础设施验证测试
/// 确保 FsCheck 和 Testcontainers 能正常工作
/// </summary>
[Trait("Category", "Infrastructure")]
public class InfrastructureVerificationTests
{
    private readonly ITestOutputHelper _output;

    public InfrastructureVerificationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region FsCheck 基础验证

    [Fact]
    public void FsCheck_IsConfigured_Correctly()
    {
        // Arrange & Act
        PropertyTestConfig.RegisterGenerators();

        // Assert - 如果没有抛出异常，说明配置正确
        Assert.True(true);
        _output.WriteLine("FsCheck generators registered successfully");
    }

    #endregion

    #region 生成器验证

    [Fact]
    public void EventGenerators_StreamId_GeneratesValidIds()
    {
        // Arrange
        var gen = EventGenerators.StreamIdArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        Assert.All(samples, id =>
        {
            Assert.NotNull(id);
            Assert.NotEmpty(id);
        });
        _output.WriteLine($"Generated {samples.Count} stream IDs: {string.Join(", ", samples.Take(5))}...");
    }

    [Fact]
    public void EventGenerators_TestEvent_GeneratesValidEvents()
    {
        // Arrange
        var gen = EventGenerators.TestEventArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        Assert.All(samples, evt =>
        {
            Assert.NotEqual(0, evt.MessageId);
            Assert.NotNull(evt.Data);
            Assert.NotEmpty(evt.Data);
        });
        _output.WriteLine($"Generated {samples.Count} test events");
    }

    [Fact]
    public void SnapshotGenerators_AggregateId_GeneratesValidIds()
    {
        // Arrange
        var gen = SnapshotGenerators.AggregateIdArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        Assert.All(samples, id =>
        {
            Assert.NotNull(id);
            Assert.NotEmpty(id);
        });
        _output.WriteLine($"Generated {samples.Count} aggregate IDs: {string.Join(", ", samples.Take(5))}...");
    }

    [Fact]
    public void SnapshotGenerators_TestAggregateState_GeneratesValidStates()
    {
        // Arrange
        var gen = SnapshotGenerators.TestAggregateStateArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        foreach (var state in samples)
        {
            Assert.NotNull(state.Id);
            Assert.NotNull(state.Name);
            Assert.NotNull(state.Status);
        }
        _output.WriteLine($"Generated {samples.Count} aggregate states");
    }

    [Fact]
    public void MessageGenerators_Topic_GeneratesValidTopics()
    {
        // Arrange
        var gen = MessageGenerators.TopicArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        Assert.All(samples, topic =>
        {
            Assert.NotNull(topic);
            Assert.NotEmpty(topic);
        });
        _output.WriteLine($"Generated {samples.Count} topics: {string.Join(", ", samples.Take(5))}...");
    }

    [Fact]
    public void MessageGenerators_TestMessage_GeneratesValidMessages()
    {
        // Arrange
        var gen = MessageGenerators.TestMessageArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        Assert.All(samples, msg =>
        {
            Assert.NotEqual(0, msg.MessageId);
            Assert.NotNull(msg.Content);
            Assert.NotEmpty(msg.Content);
        });
        _output.WriteLine($"Generated {samples.Count} test messages");
    }

    [Fact]
    public void FlowStateGenerators_FlowId_GeneratesValidIds()
    {
        // Arrange
        var gen = FlowStateGenerators.FlowIdArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        Assert.All(samples, id =>
        {
            Assert.NotNull(id);
            Assert.NotEmpty(id);
        });
        _output.WriteLine($"Generated {samples.Count} flow IDs: {string.Join(", ", samples.Take(5))}...");
    }

    [Fact]
    public void FlowStateGenerators_TestFlowState_GeneratesValidStates()
    {
        // Arrange
        var gen = FlowStateGenerators.TestFlowStateArbitrary();

        // Act
        var samples = Gen.Sample(10, 10, gen.Generator).ToList();

        // Assert
        Assert.NotEmpty(samples);
        foreach (var state in samples)
        {
            Assert.NotNull(state.OrderId);
            Assert.NotNull(state.Status);
            Assert.NotNull(state.Items);
        }
        _output.WriteLine($"Generated {samples.Count} flow states");
    }

    #endregion

    #region Testcontainers 验证

    [Fact]
    public async Task BackendTestFixture_InMemory_InitializesSuccessfully()
    {
        // Arrange
        var fixture = new BackendTestFixture(BackendType.InMemory);

        // Act
        await fixture.InitializeAsync();

        // Assert
        Assert.Equal(BackendType.InMemory, fixture.BackendType);
        // 注意：由于使用共享容器，即使是 InMemory 后端，如果 Docker 可用，
        // Redis 和 NATS 连接字符串也可能不为 null（共享容器会被初始化）
        // 这是正常的，因为共享容器在整个测试会话中保持运行
        if (fixture.IsDockerAvailable)
        {
            _output.WriteLine($"Docker available - Redis: {fixture.RedisConnectionString}, NATS: {fixture.NatsConnectionString}");
        }
        else
        {
            Assert.Null(fixture.RedisConnectionString);
            Assert.Null(fixture.NatsConnectionString);
        }

        // Cleanup
        await fixture.DisposeAsync();
        _output.WriteLine("InMemory backend fixture initialized successfully");
    }

    [Fact]
    public async Task BackendTestFixture_Redis_ChecksDockerAvailability()
    {
        // Arrange
        var fixture = new BackendTestFixture(BackendType.Redis);

        // Act
        await fixture.InitializeAsync();

        // Assert
        _output.WriteLine($"Docker available: {fixture.IsDockerAvailable}");
        if (fixture.IsDockerAvailable)
        {
            Assert.NotNull(fixture.RedisConnectionString);
            Assert.NotEmpty(fixture.RedisConnectionString);
            _output.WriteLine($"Redis connection string: {fixture.RedisConnectionString}");
        }
        else
        {
            _output.WriteLine("Docker not available, Redis container not started (this is expected in some environments)");
        }

        // Cleanup
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task BackendTestFixture_Nats_ChecksDockerAvailability()
    {
        // Arrange
        var fixture = new BackendTestFixture(BackendType.Nats);

        // Act
        await fixture.InitializeAsync();

        // Assert
        _output.WriteLine($"Docker available: {fixture.IsDockerAvailable}");
        if (fixture.IsDockerAvailable)
        {
            Assert.NotNull(fixture.NatsConnectionString);
            Assert.NotEmpty(fixture.NatsConnectionString);
            _output.WriteLine($"NATS connection string: {fixture.NatsConnectionString}");
        }
        else
        {
            _output.WriteLine("Docker not available, NATS container not started (this is expected in some environments)");
        }

        // Cleanup
        await fixture.DisposeAsync();
    }

    #endregion

    #region StoreTestBase 验证

    [Fact]
    public async Task StoreTestBase_CanBeInherited_AndUsed()
    {
        // Arrange & Act
        var testStore = new TestStoreImplementation();
        await testStore.InitializeAsync();

        // Assert
        Assert.NotNull(testStore.GetStore());
        Assert.NotNull(testStore.GetServiceProvider());

        // Cleanup
        await testStore.DisposeAsync();
        _output.WriteLine("StoreTestBase inheritance works correctly");
    }

    /// <summary>
    /// 测试用的 StoreTestBase 实现
    /// </summary>
    private class TestStoreImplementation : StoreTestBase<TestStore>
    {
        protected override TestStore CreateStore(IServiceProvider serviceProvider)
        {
            return new TestStore();
        }

        public TestStore GetStore() => Store;
        public IServiceProvider GetServiceProvider() => ServiceProvider;
    }

    private class TestStore
    {
        public string Name { get; } = "TestStore";
    }

    #endregion
}
