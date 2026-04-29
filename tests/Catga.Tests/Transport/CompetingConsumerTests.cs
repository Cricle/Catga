using System.Reflection;
using Catga.Messaging;
using Catga.DeadLetter;
using Catga.Transport.Nats;
using Catga.Transport.Nats.DependencyInjection;
using Catga.Transport.RabbitMQ;
using Catga.Transport.RabbitMQ.DependencyInjection;
using Catga.Transport;
using Catga.Transport.Redis;
using Catga.Transport.Redis.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.JetStream;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using Xunit;

namespace Catga.Tests.Transport;

// ── ICompetingConsumer interface tests ────────────────────────────────────────

public class ICompetingConsumerTests
{
    [Fact]
    public void ICompetingConsumer_IsGenericInterface()
    {
        typeof(ICompetingConsumer<>).IsInterface.Should().BeTrue();
        typeof(ICompetingConsumer<>).IsGenericTypeDefinition.Should().BeTrue();
    }

    [Fact]
    public void ICompetingConsumer_HasStartAsync()
    {
        typeof(ICompetingConsumer<object>).GetMethod("StartAsync").Should().NotBeNull();
    }

    [Fact]
    public void ICompetingConsumer_HasStopAsync()
    {
        typeof(ICompetingConsumer<object>).GetMethod("StopAsync").Should().NotBeNull();
    }

    [Fact]
    public void ICompetingConsumer_HasGroupName()
    {
        typeof(ICompetingConsumer<object>).GetProperty("GroupName").Should().NotBeNull();
    }

    [Fact]
    public void ICompetingConsumer_HasConsumerName()
    {
        typeof(ICompetingConsumer<object>).GetProperty("ConsumerName").Should().NotBeNull();
    }

    [Fact]
    public async Task ICompetingConsumer_Mock_StartStop()
    {
        var consumer = Substitute.For<ICompetingConsumer<string>>();
        consumer.GroupName.Returns("test-group");
        consumer.ConsumerName.Returns("consumer-1");

        var received = new List<string>();
        await consumer.StartAsync((msg, _) => { received.Add(msg); return Task.CompletedTask; });
        await consumer.StopAsync();

        consumer.GroupName.Should().Be("test-group");
        consumer.ConsumerName.Should().Be("consumer-1");
    }
}

// ── CompetingConsumerOptions tests ────────────────────────────────────────────

public class CompetingConsumerOptionsTests
{
    [Fact]
    public void DefaultOptions_HaveReasonableValues()
    {
        var opts = new CompetingConsumerOptions();
        opts.GroupName.Should().Be("default");
        opts.Concurrency.Should().Be(1);
        opts.BatchSize.Should().Be(10);
        opts.MaxDeliveryAttempts.Should().Be(3);
        opts.VisibilityTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        opts.PollInterval.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ResolvedConsumerName_WhenNotSet_GeneratesUniqueName()
    {
        var opts1 = new CompetingConsumerOptions();
        var opts2 = new CompetingConsumerOptions();
        opts1.ResolvedConsumerName.Should().NotBeNullOrEmpty();
        opts2.ResolvedConsumerName.Should().NotBeNullOrEmpty();
        // Each call generates a new name
        opts1.ResolvedConsumerName.Should().NotBe(opts2.ResolvedConsumerName);
    }

    [Fact]
    public void ResolvedConsumerName_WhenSet_UsesProvidedName()
    {
        var opts = new CompetingConsumerOptions { ConsumerName = "my-consumer" };
        opts.ResolvedConsumerName.Should().Be("my-consumer");
    }

    [Fact]
    public void Options_CanBeConfigured()
    {
        var opts = new CompetingConsumerOptions
        {
            GroupName = "orders",
            Concurrency = 5,
            BatchSize = 20,
            MaxDeliveryAttempts = 5,
            VisibilityTimeout = TimeSpan.FromMinutes(1),
            PollInterval = TimeSpan.FromMilliseconds(500)
        };

        opts.GroupName.Should().Be("orders");
        opts.Concurrency.Should().Be(5);
        opts.BatchSize.Should().Be(20);
        opts.MaxDeliveryAttempts.Should().Be(5);
        opts.VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(1));
        opts.PollInterval.Should().Be(TimeSpan.FromMilliseconds(500));
    }
}

// ── RedisCompetingConsumer unit tests ─────────────────────────────────────────

public class RedisCompetingConsumerTests
{
    [Fact]
    public void Constructor_SetsGroupAndConsumerName()
    {
        var redis = Substitute.For<StackExchange.Redis.IConnectionMultiplexer>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var opts = new CompetingConsumerOptions { GroupName = "my-group", ConsumerName = "consumer-1" };

        var consumer = new RedisCompetingConsumer<string>(redis, serializer, "stream:orders", opts);

        consumer.GroupName.Should().Be("my-group");
        consumer.ConsumerName.Should().Be("consumer-1");
    }

    [Fact]
    public void Constructor_WithDefaultOptions_GeneratesConsumerName()
    {
        var redis = Substitute.For<StackExchange.Redis.IConnectionMultiplexer>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();

        var consumer = new RedisCompetingConsumer<string>(redis, serializer, "stream:test");

        consumer.GroupName.Should().Be("default");
        consumer.ConsumerName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        var redis = Substitute.For<StackExchange.Redis.IConnectionMultiplexer>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var consumer = new RedisCompetingConsumer<string>(redis, serializer, "stream:test");

        var act = async () => await consumer.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void RedisCompetingConsumer_ImplementsICompetingConsumer()
    {
        typeof(RedisCompetingConsumer<string>)
            .GetInterfaces()
            .Should().Contain(typeof(ICompetingConsumer<string>));
    }

    [Fact]
    public async Task RedisCompetingConsumer_WhenMaxAttemptsReached_ShouldDeadLetterAndAck()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var deadLetterQueue = Substitute.For<IDeadLetterQueue>();
        var message = new TestCompetingMessage { MessageId = 101 };
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        serializer.Deserialize<TestCompetingMessage>(Arg.Any<byte[]>()).Returns(message);

        var consumer = new RedisCompetingConsumer<TestCompetingMessage>(
            redis,
            serializer,
            "stream:orders",
            new CompetingConsumerOptions { MaxDeliveryAttempts = 1 },
            deadLetterQueue);

        var entry = new StreamEntry("1-0", [new NameValueEntry("payload", new byte[] { 1, 2, 3 })]);
        Func<TestCompetingMessage, CancellationToken, Task> handler = (_, _) => throw new InvalidOperationException("redis fail");

        await CompetingConsumerTestHelpers.InvokePrivateTaskAsync(
            consumer,
            "ProcessEntryAsync",
            db,
            entry,
            1,
            handler,
            new SemaphoreSlim(1, 1),
            CancellationToken.None);

        await deadLetterQueue.Received(1).SendAsync(
            message,
            Arg.Any<Exception>(),
            0,
            Arg.Any<CancellationToken>());
        await db.Received(1).StreamAcknowledgeAsync("stream:orders", "default", entry.Id);
    }
}

// ── NatsCompetingConsumer unit tests ──────────────────────────────────────────

public class NatsCompetingConsumerTests
{
    [Fact]
    public void Constructor_SetsGroupAndConsumerName()
    {
        var nats = Substitute.For<NATS.Client.Core.INatsConnection>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var opts = new CompetingConsumerOptions { GroupName = "nats-group", ConsumerName = "nats-consumer-1" };

        var consumer = new NatsCompetingConsumer<string>(nats, serializer, "orders.created", options: opts);

        consumer.GroupName.Should().Be("nats-group");
        consumer.ConsumerName.Should().Be("nats-consumer-1");
    }

    [Fact]
    public void Constructor_WithDefaultOptions_GeneratesConsumerName()
    {
        var nats = Substitute.For<NATS.Client.Core.INatsConnection>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();

        var consumer = new NatsCompetingConsumer<string>(nats, serializer, "orders.created");

        consumer.GroupName.Should().Be("default");
        consumer.ConsumerName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        var nats = Substitute.For<NATS.Client.Core.INatsConnection>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var consumer = new NatsCompetingConsumer<string>(nats, serializer, "test.subject");

        var act = async () => await consumer.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void NatsCompetingConsumer_ImplementsICompetingConsumer()
    {
        typeof(NatsCompetingConsumer<string>)
            .GetInterfaces()
            .Should().Contain(typeof(ICompetingConsumer<string>));
    }

    [Fact]
    public async Task NatsCompetingConsumer_WhenMaxAttemptsReached_ShouldDeadLetterAndTerminate()
    {
        var nats = Substitute.For<NATS.Client.Core.INatsConnection>();
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var deadLetterQueue = Substitute.For<IDeadLetterQueue>();
        var message = new TestCompetingMessage { MessageId = 202 };
        var jsMessage = Substitute.For<INatsJSMsg<byte[]>>();
        jsMessage.Data.Returns(new byte[] { 1, 2, 3 });
        jsMessage.Metadata.Returns(new NatsJSMsgMetadata(
            new NatsJSSequencePair(1, 1),
            1,
            0,
            DateTimeOffset.UtcNow,
            "ORDERS",
            "consumer",
            string.Empty));
        serializer.Deserialize<TestCompetingMessage>(Arg.Any<byte[]>()).Returns(message);

        var consumer = new NatsCompetingConsumer<TestCompetingMessage>(
            nats,
            serializer,
            "orders.created",
            options: new CompetingConsumerOptions { MaxDeliveryAttempts = 1 },
            deadLetterQueue: deadLetterQueue);

        Func<TestCompetingMessage, CancellationToken, Task> handler = (_, _) => throw new InvalidOperationException("nats fail");
        using var semaphore = new SemaphoreSlim(1, 1);
        await semaphore.WaitAsync();

        await CompetingConsumerTestHelpers.InvokePrivateTaskAsync(
            consumer,
            "ProcessMessageAsync",
            jsMessage,
            handler,
            semaphore,
            CancellationToken.None);

        await deadLetterQueue.Received(1).SendAsync(
            message,
            Arg.Any<Exception>(),
            0,
            Arg.Any<CancellationToken>());
        await jsMessage.Received(1).AckTerminateAsync(Arg.Any<AckOpts?>(), Arg.Any<CancellationToken>());
        await jsMessage.DidNotReceive().NakAsync(Arg.Any<AckOpts?>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}

// ── RabbitMqCompetingConsumer unit tests ──────────────────────────────────────

public class RabbitMqCompetingConsumerTests
{
    [Fact]
    public void Constructor_SetsGroupAndConsumerName()
    {
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var opts = new CompetingConsumerOptions { GroupName = "rabbit-group", ConsumerName = "rabbit-consumer-1" };

        var consumer = new RabbitMqCompetingConsumer<string>(
            serializer,
            "catga.orders",
            "catga.orders",
            options: opts);

        consumer.GroupName.Should().Be("rabbit-group");
        consumer.ConsumerName.Should().Be("rabbit-consumer-1");
    }

    [Fact]
    public void Constructor_WithDefaultOptions_GeneratesConsumerName()
    {
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();

        var consumer = new RabbitMqCompetingConsumer<string>(
            serializer,
            "catga.orders",
            "catga.orders");

        consumer.GroupName.Should().Be("default");
        consumer.ConsumerName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var consumer = new RabbitMqCompetingConsumer<string>(
            serializer,
            "catga.orders",
            "catga.orders");

        var act = async () => await consumer.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void RabbitMqCompetingConsumer_ImplementsICompetingConsumer()
    {
        typeof(RabbitMqCompetingConsumer<string>)
            .GetInterfaces()
            .Should().Contain(typeof(ICompetingConsumer<string>));
    }

    [Fact]
    public async Task RabbitMqCompetingConsumer_WhenMaxAttemptsReached_ShouldDeadLetterAndRejectWithoutRequeue()
    {
        var serializer = Substitute.For<Catga.Abstractions.IMessageSerializer>();
        var deadLetterQueue = Substitute.For<IDeadLetterQueue>();
        var channel = Substitute.For<IChannel>();
        var message = new TestCompetingMessage { MessageId = 303 };
        serializer.Deserialize<TestCompetingMessage>(Arg.Any<byte[]>()).Returns(message);

        var consumer = new RabbitMqCompetingConsumer<TestCompetingMessage>(
            serializer,
            "catga.orders",
            "catga.orders",
            options: new CompetingConsumerOptions { MaxDeliveryAttempts = 1 },
            deadLetterQueue: deadLetterQueue);

        CompetingConsumerTestHelpers.SetPrivateField(consumer, "_channel", channel);

        var delivery = new BasicDeliverEventArgs(
            "consumer-tag",
            7,
            false,
            "catga",
            "catga.orders",
            new BasicProperties { MessageId = "msg-303" },
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        Func<TestCompetingMessage, CancellationToken, Task> handler = (_, _) => throw new InvalidOperationException("rabbit fail");

        await CompetingConsumerTestHelpers.InvokePrivateTaskAsync(
            consumer,
            "ProcessDeliveryAsync",
            handler,
            new SemaphoreSlim(1, 1),
            delivery,
            CancellationToken.None);

        await deadLetterQueue.Received(1).SendAsync(
            message,
            Arg.Any<Exception>(),
            0,
            Arg.Any<CancellationToken>());
        await channel.Received(1).BasicNackAsync(7, false, false, Arg.Any<CancellationToken>());
    }
}

// ── DI registration tests ─────────────────────────────────────────────────────

public class CompetingConsumerDiTests
{
    [Fact]
    public void AddRedisCompetingConsumer_RegistersConsumer()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddRedisCompetingConsumer<string>("stream:orders", opt =>
        {
            opt.GroupName = "order-processors";
            opt.Concurrency = 3;
        });

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetService<ICompetingConsumer<string>>();
        consumer.Should().NotBeNull();
        consumer!.GroupName.Should().Be("order-processors");
    }

    [Fact]
    public void AddNatsCompetingConsumer_RegistersConsumer()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<NATS.Client.Core.INatsConnection>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddNatsCompetingConsumer<string>("orders.created", configure: opt =>
        {
            opt.GroupName = "nats-processors";
        });

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetService<ICompetingConsumer<string>>();
        consumer.Should().NotBeNull();
        consumer!.GroupName.Should().Be("nats-processors");
    }

    [Fact]
    public void AddRedisCompetingConsumer_IsSingleton()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddRedisCompetingConsumer<string>("stream:test");

        var sp = services.BuildServiceProvider();
        var c1 = sp.GetService<ICompetingConsumer<string>>();
        var c2 = sp.GetService<ICompetingConsumer<string>>();
        c1.Should().BeSameAs(c2);
    }

    [Fact]
    public void AddRedisCompetingConsumer_WithTransportChannelPrefix_UsesResolvedStreamKey()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddSingleton(new RedisTransportOptions { ChannelPrefix = "catga." });
        services.AddRedisCompetingConsumer<string>("stream:orders.created");

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetRequiredService<ICompetingConsumer<string>>();

        CompetingConsumerTestHelpers
            .GetPrivateField<string>(consumer, "_streamKey")
            .Should()
            .Be("stream:catga.orders.created");
    }

    [Fact]
    public void AddRedisCompetingConsumer_WithPrefixMissingTrailingDot_NormalizesResolvedStreamKey()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddSingleton(new RedisTransportOptions { ChannelPrefix = "redis-prefix" });
        services.AddRedisCompetingConsumer<string>("orders.created");

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetRequiredService<ICompetingConsumer<string>>();

        CompetingConsumerTestHelpers
            .GetPrivateField<string>(consumer, "_streamKey")
            .Should()
            .Be("stream:redis-prefix.orders.created");
    }

    [Fact]
    public void AddNatsCompetingConsumer_WithStreamName_RegistersConsumer()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<NATS.Client.Core.INatsConnection>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddNatsCompetingConsumer<string>("orders.>", streamName: "ORDERS");

        var sp = services.BuildServiceProvider();
        sp.GetService<ICompetingConsumer<string>>().Should().NotBeNull();
    }

    [Fact]
    public void AddNatsCompetingConsumer_WithTransportSubjectPrefix_UsesResolvedSubject()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<NATS.Client.Core.INatsConnection>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddSingleton(new NatsTransportOptions { SubjectPrefix = "catga." });
        services.AddNatsCompetingConsumer<string>("orders.created");

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetRequiredService<ICompetingConsumer<string>>();

        CompetingConsumerTestHelpers
            .GetPrivateField<string>(consumer, "_subject")
            .Should()
            .Be("catga.orders.created");
    }

    [Fact]
    public void AddNatsCompetingConsumer_WithPrefixMissingTrailingDot_NormalizesResolvedSubject()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<NATS.Client.Core.INatsConnection>());
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddSingleton(new NatsTransportOptions { SubjectPrefix = "nats-prefix" });
        services.AddNatsCompetingConsumer<string>("orders.created");

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetRequiredService<ICompetingConsumer<string>>();

        CompetingConsumerTestHelpers
            .GetPrivateField<string>(consumer, "_subject")
            .Should()
            .Be("nats-prefix.orders.created");
    }

    [Fact]
    public void AddRabbitMqCompetingConsumer_RegistersConsumer()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddSingleton(new RabbitMqTransportOptions { Prefix = "catga." });
        services.AddRabbitMqCompetingConsumer<string>("orders.queue", configure: opt =>
        {
            opt.GroupName = "rabbit-processors";
        });

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetService<ICompetingConsumer<string>>();
        consumer.Should().NotBeNull();
        consumer!.GroupName.Should().Be("rabbit-processors");
    }

    [Fact]
    public void AddRabbitMqCompetingConsumer_WithDefaultGroup_UsesResolvedQueueName()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddSingleton(new RabbitMqTransportOptions { Prefix = "catga." });
        services.AddRabbitMqCompetingConsumer<string>("orders.queue");

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetRequiredService<ICompetingConsumer<string>>();
        consumer.GroupName.Should().Be("catga.orders.queue");
    }

    [Fact]
    public void AddRabbitMqCompetingConsumer_WithPrefixMissingTrailingDot_NormalizesResolvedQueueName()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(Substitute.For<Catga.Abstractions.IMessageSerializer>());
        services.AddSingleton(new RabbitMqTransportOptions { Prefix = "rabbit-prefix" });
        services.AddRabbitMqCompetingConsumer<string>("orders.queue");

        var sp = services.BuildServiceProvider();
        var consumer = sp.GetRequiredService<ICompetingConsumer<string>>();
        consumer.GroupName.Should().Be("rabbit-prefix.orders.queue");
    }
}

// ── Competing consumer pattern E2E (in-memory simulation) ────────────────────

public class CompetingConsumerPatternTests
{
    /// <summary>
    /// Simulates competing consumers: multiple consumers, each message processed exactly once.
    /// Uses a simple in-memory queue to verify the pattern without real Redis/NATS.
    /// </summary>
    [Fact]
    public async Task CompetingConsumers_EachMessageProcessedOnce()
    {
        var queue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var processed = new System.Collections.Concurrent.ConcurrentBag<string>();
        var messages = Enumerable.Range(1, 20).Select(i => $"msg-{i}").ToList();
        foreach (var m in messages) queue.Enqueue(m);

        // Simulate 3 competing consumers
        async Task ConsumerLoop(string name, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && queue.TryDequeue(out var msg))
            {
                processed.Add($"{name}:{msg}");
                await Task.Delay(1, ct);
            }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(
            ConsumerLoop("c1", cts.Token),
            ConsumerLoop("c2", cts.Token),
            ConsumerLoop("c3", cts.Token));

        // Each message processed exactly once
        processed.Should().HaveCount(20);
        var processedMessages = processed.Select(p => p.Split(':')[1]).ToHashSet();
        processedMessages.Should().HaveCount(20);
        foreach (var msg in messages)
            processedMessages.Should().Contain(msg);
    }

    [Fact]
    public async Task CompetingConsumers_ConcurrentProcessing_NoDataRace()
    {
        var counter = 0;
        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            await Task.Delay(Random.Shared.Next(0, 5));
            Interlocked.Increment(ref counter);
        });

        await Task.WhenAll(tasks);
        counter.Should().Be(100);
    }
}

public sealed class TestCompetingMessage : Catga.Abstractions.IMessage
{
    public long MessageId { get; init; }
    public long? CorrelationId { get; init; }
}

internal static class CompetingConsumerTestHelpers
{
    public static async Task InvokePrivateTaskAsync(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"private method {methodName} should exist");
        var task = method!.Invoke(target, args) as Task;
        task.Should().NotBeNull($"{methodName} should return Task");
        await task!;
    }

    public static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"private field {fieldName} should exist");
        field!.SetValue(target, value);
    }

    public static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"private field {fieldName} should exist");
        return (T)field!.GetValue(target)!;
    }
}
