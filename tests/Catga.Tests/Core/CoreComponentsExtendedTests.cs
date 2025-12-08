using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using FluentAssertions;
using MemoryPack;

namespace Catga.Tests.Core;

/// <summary>
/// Extended tests for core components to improve coverage.
/// Target: 80% coverage for Catga core module
/// </summary>
public class CoreComponentsExtendedTests
{
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    #region MessageExtensions Tests

    [Fact]
    public void NewMessageId_ShouldGenerateUniqueIds()
    {
        var ids = new HashSet<long>();

        for (int i = 0; i < 1000; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }

        ids.Count.Should().Be(1000);
    }

    [Fact]
    public void NewCorrelationId_ShouldGenerateUniqueIds()
    {
        var ids = new HashSet<long>();

        for (int i = 0; i < 1000; i++)
        {
            ids.Add(MessageExtensions.NewCorrelationId());
        }

        ids.Count.Should().Be(1000);
    }

    #endregion

    #region TransportContext Tests

    [Fact]
    public void TransportContext_Create_ShouldSetProperties()
    {
        var ctx = new TransportContext
        {
            MessageId = 12345,
            MessageType = "TestMessage",
            CorrelationId = 67890,
            SentAt = DateTime.UtcNow
        };

        ctx.MessageId.Should().Be(12345);
        ctx.MessageType.Should().Be("TestMessage");
        ctx.CorrelationId.Should().Be(67890);
    }

    #endregion

    #region Observability Tests

    [Fact]
    public void CatgaDiagnostics_ActivitySourceName_ShouldBeCorrect()
    {
        CatgaDiagnostics.ActivitySourceName.Should().Be("Catga");
    }

    [Fact]
    public void CatgaDiagnostics_IncrementActiveMessages_ShouldNotThrow()
    {
        CatgaDiagnostics.IncrementActiveMessages();
        CatgaDiagnostics.DecrementActiveMessages();
    }

    #endregion

    #region Resilience Tests

    [Fact]
    public void CatgaResilienceOptions_Defaults_ShouldBeReasonable()
    {
        var options = new CatgaResilienceOptions();

        options.TransportRetryCount.Should().BeGreaterThan(0);
        options.PersistenceRetryCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DefaultResiliencePipelineProvider_ExecuteTransportPublish_ShouldExecute()
    {
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        await provider.ExecuteTransportPublishAsync(async ct =>
        {
            executed = true;
            await ValueTask.CompletedTask;
        }, CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task DefaultResiliencePipelineProvider_ExecutePersistence_ShouldExecute()
    {
        var provider = new DefaultResiliencePipelineProvider();
        var executed = false;

        await provider.ExecutePersistenceAsync(async ct =>
        {
            executed = true;
            await ValueTask.CompletedTask;
        }, CancellationToken.None);

        executed.Should().BeTrue();
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public void MemoryPackSerializer_SerializeDeserialize_ShouldRoundTrip()
    {
        var original = new CoreTestMessage { MessageId = 123, Data = "test" };

        var bytes = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<CoreTestMessage>(bytes);

        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be(123);
        deserialized.Data.Should().Be("test");
    }

    #endregion

    #region PooledBufferWriter Tests

    [Fact]
    public void PooledBufferWriter_Write_ShouldStoreData()
    {
        using var writer = new PooledBufferWriter<byte>();

        var span = writer.GetSpan(10);
        span[0] = 1;
        span[1] = 2;
        writer.Advance(2);

        writer.WrittenCount.Should().Be(2);
    }

    #endregion

    #region TypeNameCache Tests

    [Fact]
    public void TypeNameCache_Name_ShouldReturnTypeName()
    {
        var name = TypeNameCache<CoreTestMessage>.Name;

        name.Should().Be("CoreTestMessage");
    }

    [Fact]
    public void TypeNameCache_FullName_ShouldReturnFullTypeName()
    {
        var fullName = TypeNameCache<CoreTestMessage>.FullName;

        fullName.Should().Contain("CoreTestMessage");
    }

    [Fact]
    public void TypeNameCache_MultipleTypes_ShouldCacheIndependently()
    {
        var name1 = TypeNameCache<CoreTestMessage>.Name;
        var name2 = TypeNameCache<string>.Name;

        name1.Should().Be("CoreTestMessage");
        name2.Should().Be("String");
    }

    #endregion

    #region PooledBufferWriter Extended Tests

    [Fact]
    public void PooledBufferWriter_GetMemory_ShouldReturnMemory()
    {
        using var writer = new PooledBufferWriter<byte>();

        var memory = writer.GetMemory(10);

        memory.Length.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public void PooledBufferWriter_MultipleWrites_ShouldAccumulate()
    {
        using var writer = new PooledBufferWriter<byte>();

        var span1 = writer.GetSpan(5);
        span1[0] = 1;
        writer.Advance(1);

        var span2 = writer.GetSpan(5);
        span2[0] = 2;
        writer.Advance(1);

        writer.WrittenCount.Should().Be(2);
    }

    [Fact]
    public void PooledBufferWriter_WrittenSpan_ShouldReturnData()
    {
        using var writer = new PooledBufferWriter<byte>();

        var span = writer.GetSpan(3);
        span[0] = 10;
        span[1] = 20;
        span[2] = 30;
        writer.Advance(3);

        var written = writer.WrittenSpan;
        written.Length.Should().Be(3);
        written[0].Should().Be(10);
        written[1].Should().Be(20);
        written[2].Should().Be(30);
    }

    #endregion

    #region MessageExtensions Extended Tests

    [Fact]
    public void NewMessageId_MultipleCalls_ShouldBeUnique()
    {
        var ids = new HashSet<long>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(MessageExtensions.NewMessageId());
        }

        ids.Count.Should().Be(100);
    }

    [Fact]
    public void NewCorrelationId_MultipleCalls_ShouldBeUnique()
    {
        var ids = new HashSet<long>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(MessageExtensions.NewCorrelationId());
        }

        ids.Count.Should().Be(100);
    }

    #endregion
}

#region Test Types

[MemoryPackable]
public partial class CoreTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

#endregion






