using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Persistence.Redis.Scheduling;
using Catga.Scheduling;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed partial class MessageSchedulerE2ETests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
        _redisContainer = new RedisBuilder()
            .WithImage(redisImage)
            .Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();
        if (_redisContainer is not null)
            await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Redis_MessageScheduler_ScheduleAndCancel()
    {
        if (_redis is null) return;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_redis);
        services.AddSingleton(_serializer);
        services.AddCatga();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var options = Options.Create(new MessageSchedulerOptions { PollingInterval = TimeSpan.FromHours(1) }); // Don't poll
        var logger = NullLogger<RedisMessageScheduler>.Instance;

        var scheduler = new RedisMessageScheduler(_redis, _serializer, mediator, options, logger);

        // Schedule a message
        var message = new ScheduledTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "scheduled" };
        var handle = await scheduler.ScheduleAsync(message, TimeSpan.FromMinutes(30));

        handle.ScheduleId.Should().NotBeNullOrEmpty();
        handle.MessageType.Should().Contain("ScheduledTestEvent");

        // Verify it's pending
        var info = await scheduler.GetAsync(handle.ScheduleId);
        info.Should().NotBeNull();
        info!.Value.Status.Should().Be(ScheduledMessageStatus.Pending);

        // Cancel it
        var cancelled = await scheduler.CancelAsync(handle.ScheduleId);
        cancelled.Should().BeTrue();

        // Verify cancelled
        var infoAfter = await scheduler.GetAsync(handle.ScheduleId);
        infoAfter.Should().BeNull();
    }

    [Fact]
    public async Task Redis_MessageScheduler_ListPending()
    {
        if (_redis is null) return;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_redis);
        services.AddSingleton(_serializer);
        services.AddCatga();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var options = Options.Create(new MessageSchedulerOptions { PollingInterval = TimeSpan.FromHours(1) });
        var logger = NullLogger<RedisMessageScheduler>.Instance;

        var scheduler = new RedisMessageScheduler(_redis, _serializer, mediator, options, logger);

        // Schedule multiple messages
        var handles = new List<ScheduledMessageHandle>();
        for (int i = 0; i < 5; i++)
        {
            var message = new ScheduledTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = $"msg-{i}" };
            var handle = await scheduler.ScheduleAsync(message, TimeSpan.FromMinutes(i + 1));
            handles.Add(handle);
        }

        // List pending
        var pending = new List<ScheduledMessageInfo>();
        await foreach (var info in scheduler.ListPendingAsync(10))
        {
            pending.Add(info);
        }

        pending.Count.Should().BeGreaterOrEqualTo(5);

        // Cleanup
        foreach (var handle in handles)
        {
            await scheduler.CancelAsync(handle.ScheduleId);
        }
    }

    [MemoryPackable]
    private partial record ScheduledTestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Data { get; init; }
    }
}
