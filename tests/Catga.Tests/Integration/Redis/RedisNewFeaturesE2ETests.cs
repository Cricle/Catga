using System.Security.Claims;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Flow.StateMachine;
using Catga.Messaging;
using Catga.Outbox;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Flow;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Resilience;
using Catga.Security;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.Redis;

/// <summary>
/// E2E tests for all new features on Redis backend.
/// Covers: StateMachine, Outbox scheduling, MessageVersioning,
///         Authorization, CorrelationId, Fault, HMAC signing.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class RedisNewFeaturesE2ETests : IAsyncLifetime
{
    private RedisContainer? _container;
    private IConnectionMultiplexer? _redis;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IMessageSerializer _jsonSerializer = new Catga.Tests.Helpers.TestMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;
        _container = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis != null) await _redis.CloseAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    // ── 1. StateMachine on Redis ──────────────────────────────────────────────

    [Fact]
    public async Task Redis_StateMachine_FullLifecycle()
    {
        if (_redis == null) return;

        var store = new RedisDslFlowStore(_redis, _jsonSerializer);
        var executor = new StateMachineExecutor<RedisPaymentState, RedisPaymentStatus, RedisPaymentMachine>(store);

        await executor.InitializeAsync("pay-redis-1", RedisPaymentStatus.Pending);
        var r1 = await executor.HandleAsync("pay-redis-1", new RedisPaymentAuthorized("AUTH-R1"));
        var r2 = await executor.HandleAsync("pay-redis-1", new RedisPaymentCaptured());

        r1.Transitioned.Should().BeTrue();
        r1.CurrentState.Should().Be(RedisPaymentStatus.Authorized);
        r2.CurrentState.Should().Be(RedisPaymentStatus.Captured);

        var state = await executor.GetStateAsync("pay-redis-1");
        state!.AuthCode.Should().Be("AUTH-R1");
        state.CurrentState.Should().Be(RedisPaymentStatus.Captured);
    }

    [Fact]
    public async Task Redis_StateMachine_InvalidTransition_NotHandled()
    {
        if (_redis == null) return;

        var store = new RedisDslFlowStore(_redis, _jsonSerializer);
        var executor = new StateMachineExecutor<RedisPaymentState, RedisPaymentStatus, RedisPaymentMachine>(store);

        await executor.InitializeAsync("pay-redis-inv", RedisPaymentStatus.Pending);
        var result = await executor.HandleAsync("pay-redis-inv", new RedisPaymentCaptured());

        result.Handled.Should().BeFalse();
        result.CurrentState.Should().Be(RedisPaymentStatus.Pending);
    }

    [Fact]
    public async Task Redis_StateMachine_MultipleInstances_Independent()
    {
        if (_redis == null) return;

        var store = new RedisDslFlowStore(_redis, _jsonSerializer);
        var executor = new StateMachineExecutor<RedisPaymentState, RedisPaymentStatus, RedisPaymentMachine>(store);

        await executor.InitializeAsync("pay-A", RedisPaymentStatus.Pending);
        await executor.InitializeAsync("pay-B", RedisPaymentStatus.Pending);

        await executor.HandleAsync("pay-A", new RedisPaymentAuthorized("AUTH-A"));

        var sA = await executor.GetStateAsync("pay-A");
        var sB = await executor.GetStateAsync("pay-B");

        sA!.CurrentState.Should().Be(RedisPaymentStatus.Authorized);
        sB!.CurrentState.Should().Be(RedisPaymentStatus.Pending);
    }

    // ── 2. Outbox scheduling on Redis ─────────────────────────────────────────

    [Fact]
    public async Task Redis_Outbox_ImmediateMessage_IsReadyToDeliver()
    {
        if (_redis == null) return;

        var outbox = new RedisOutboxStore(_redis, _jsonSerializer, _provider);
        var msgId = DateTime.UtcNow.Ticks; // unique per test run

        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = msgId, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
        });

        var pending = await outbox.GetPendingMessagesAsync(100);
        var mine = pending.FirstOrDefault(m => m.MessageId == msgId);

        mine.Should().NotBeNull();
        mine!.IsReadyToDeliver.Should().BeTrue();
    }

    [Fact]
    public async Task Redis_Outbox_FutureScheduled_NotReadyToDeliver()
    {
        if (_redis == null) return;

        var outbox = new RedisOutboxStore(_redis, _jsonSerializer, _provider);
        var msgId = DateTime.UtcNow.Ticks + 1;

        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = msgId, MessageType = "T", Payload = [], Status = OutboxStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1)
        });

        var pending = await outbox.GetPendingMessagesAsync(100);
        var mine = pending.FirstOrDefault(m => m.MessageId == msgId);

        mine.Should().NotBeNull();
        mine!.IsReadyToDeliver.Should().BeFalse();
    }

    // ── 3. Message Versioning ─────────────────────────────────────────────────

    [Fact]
    public void Redis_MessageVersioning_TypeAlias_Resolves()
    {
        var mapper = new MessageVersionMapperBuilder()
            .MapType("OldPaymentAuth", typeof(RedisPaymentAuthorized))
            .Build();

        mapper.ResolveType("OldPaymentAuth").Should().Be(typeof(RedisPaymentAuthorized));
        mapper.ResolveType("Unknown").Should().BeNull();
    }

    [Fact]
    public void Redis_MessageVersioning_ContentUpgrade()
    {
        var mapper = new MessageVersionMapperBuilder()
            .Upgrade<RedisPaymentAuthorized, RedisPaymentCaptured>(_ => new RedisPaymentCaptured())
            .Build();

        IEvent v1 = new RedisPaymentAuthorized("AUTH");
        mapper.Upgrade(v1).Should().BeOfType<RedisPaymentCaptured>();
    }

    // ── 4. Authorization ──────────────────────────────────────────────────────

    [Fact]
    public async Task Redis_Authorization_EnforcesRoles()
    {
        if (_redis == null) return;

        var services = BuildAuthServices();
        await using var sp = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<ISecurityContext>();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var r1 = await mediator.SendAsync<RedisAdminCmd, string>(new RedisAdminCmd());
        r1.ErrorCode.Should().Be(ErrorCodes.Unauthorized);

        var id = new ClaimsIdentity("test");
        id.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        ctx.SetUser(new ClaimsPrincipal(id));

        var r2 = await mediator.SendAsync<RedisAdminCmd, string>(new RedisAdminCmd());
        r2.IsSuccess.Should().BeTrue();
    }

    // ── 5. HMAC Signing ───────────────────────────────────────────────────────

    [Fact]
    public void Redis_MessageSigning_SignAndVerify()
    {
        var signer = new HmacMessageSigner("redis-secret");
        var payload = _jsonSerializer.Serialize(new RedisPaymentAuthorized("AUTH"));

        var sig = signer.Sign(payload);
        signer.Verify(payload, sig).Should().BeTrue();
        signer.Verify(new byte[] { 0 }, sig).Should().BeFalse();
    }

    // ── 6. CorrelationId ──────────────────────────────────────────────────────

    [Fact]
    public async Task Redis_CorrelationId_PropagatedToHandler()
    {
        if (_redis == null) return;

        long? captured = null;
        var services = BuildCorrelationServices(id => captured = id);
        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        await mediator.SendAsync<RedisCorrelatedCmd, string>(
            new RedisCorrelatedCmd { MessageId = 77L });

        captured.Should().Be(77L);
    }

    // ── 7. Fault publishing ───────────────────────────────────────────────────

    [Fact]
    public async Task Redis_FaultPublishing_FailedHandler_PublishesFault()
    {
        if (_redis == null) return;

        var faults = new System.Collections.Concurrent.ConcurrentBag<Fault<RedisFailableCmd>>();
        var services = BuildFaultServices(faults);
        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<RedisFailableCmd, string>(new RedisFailableCmd());

        result.IsSuccess.Should().BeFalse();
        faults.Should().HaveCount(1);
        faults.First().ErrorCode.Should().Be(ErrorCodes.HandlerFailed);
    }

    // ── 8. MessageRouter + Priority ───────────────────────────────────────────

    [Fact]
    public void Redis_MessageRouter_HeaderRouting()
    {
        var router = new MessageRouter("queue.default");
        router.AddRoute("region", "eu", "queue.eu");
        router.AddRoute("region", "us", "queue.us");

        router.Resolve(new Catga.Transport.TransportContext
        {
            Metadata = new Dictionary<string, string> { ["region"] = "eu" }
        }).Should().Be("queue.eu");

        router.Resolve(new Catga.Transport.TransportContext
        {
            Metadata = new Dictionary<string, string> { ["region"] = "au" }
        }).Should().Be("queue.default");
    }

    [Fact]
    public void Redis_PriorityTransportContext_AllLevels()
    {
        PriorityTransportContext.Critical.Priority.Should().Be(MessagePriority.Critical);
        PriorityTransportContext.High.Priority.Should().Be(MessagePriority.High);
        PriorityTransportContext.Normal.Priority.Should().Be(MessagePriority.Normal);

        var ctx = new PriorityTransportContext(MessagePriority.Critical);
        ctx.Context.Metadata!["x-priority"].Should().Be("3");
    }

    [Fact]
    public void Redis_MessageRouter_WithPriorityContext()
    {
        var router = new MessageRouter("queue.normal");
        router.AddRoute("x-priority", "3", "queue.critical");

        var dest = router.Resolve(PriorityTransportContext.Critical.Context);
        dest.Should().Be("queue.critical");
    }

    // ── 9. OutboxCompetingProcessor ───────────────────────────────────────────

    [Fact]
    public async Task Redis_OutboxCompetingProcessor_ProcessesDueMessages()
    {
        if (_redis == null) return;

        var outbox = new RedisOutboxStore(_redis, _jsonSerializer, _provider);
        var published = new System.Collections.Concurrent.ConcurrentBag<long>();
        var transport = new CountingTransport(id => published.Add(id));

        var msgId = DateTime.UtcNow.Ticks + 100;
        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = msgId, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
        });

        var processor = new OutboxCompetingProcessor(outbox, transport);
        await processor.ProcessBatchAsync();

        published.Should().Contain(msgId);
    }

    [Fact]
    public async Task Redis_OutboxCompetingProcessor_SkipsFutureScheduled()
    {
        if (_redis == null) return;

        var outbox = new RedisOutboxStore(_redis, _jsonSerializer, _provider);
        var published = new System.Collections.Concurrent.ConcurrentBag<long>();
        var transport = new CountingTransport(id => published.Add(id));

        var futureId = DateTime.UtcNow.Ticks + 200;
        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = futureId, MessageType = "T", Payload = [], Status = OutboxStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1)
        });

        var processor = new OutboxCompetingProcessor(outbox, transport);
        await processor.ProcessBatchAsync();

        published.Should().NotContain(futureId);
    }

    // ── 10. SendLater via Outbox ──────────────────────────────────────────────

    [Fact]
    public async Task Redis_SendLater_StoresInOutbox_WithScheduledAt()
    {
        if (_redis == null) return;

        var outbox = new RedisOutboxStore(_redis, _jsonSerializer, _provider);
        var msgId = DateTime.UtcNow.Ticks + 300;

        // Simulate SendLater by adding directly to outbox with ScheduledAt
        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = msgId, MessageType = "SendLaterCmd", Payload = [],
            Status = OutboxStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        var pending = await outbox.GetPendingMessagesAsync(100);
        var mine = pending.FirstOrDefault(m => m.MessageId == msgId);

        mine.Should().NotBeNull();
        mine!.ScheduledAt.Should().NotBeNull();
        mine.ScheduledAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
        mine.IsReadyToDeliver.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ClearCaches()
    {
        var t = typeof(CatgaMediator);
        (t.GetField("_handlerCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
        (t.GetField("_behaviorCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
    }

    private static ServiceCollection BuildBase()
    {
        ClearCaches();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<CatgaOptions>();
        s.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        s.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        s.AddSingleton<ICatgaMediator, CatgaMediator>();
        return s;
    }

    private static ServiceCollection BuildAuthServices()
    {
        var s = BuildBase();
        s.AddSingleton<ISecurityContext, SecurityContext>();
        s.AddSingleton<IAuthorizationPolicyRegistry, AuthorizationPolicyRegistry>();
        s.AddSingleton(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        s.AddSingleton<IRequestHandler<RedisAdminCmd, string>>(_ => new EchoHandler<RedisAdminCmd>());
        return s;
    }

    private static ServiceCollection BuildCorrelationServices(Action<long?> capture)
    {
        var s = BuildBase();
        s.AddSingleton<ICorrelationContext, CorrelationContext>();
        s.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CorrelationPropagationBehavior<,>));
        s.AddSingleton<IRequestHandler<RedisCorrelatedCmd, string>>(sp =>
            new CapturingHandler(sp.GetRequiredService<ICorrelationContext>(), capture));
        return s;
    }

    private static ServiceCollection BuildFaultServices(
        System.Collections.Concurrent.ConcurrentBag<Fault<RedisFailableCmd>> faults)
    {
        var s = BuildBase();
        s.AddSingleton(typeof(IPipelineBehavior<,>), typeof(FaultPublishingBehavior<,>));
        s.AddSingleton<IRequestHandler<RedisFailableCmd, string>>(_ => new FailingHandler());
        s.AddSingleton<IEventHandler<Fault<RedisFailableCmd>>>(_ => new FaultCapture(faults));
        return s;
    }

    [Authorize("admin")]
    private record RedisAdminCmd : IRequest<string> { public long MessageId { get; init; } }
    private record RedisCorrelatedCmd : IRequest<string> { public long MessageId { get; init; } }
    private record RedisFailableCmd : IRequest<string> { public long MessageId { get; init; } }

    private sealed class EchoHandler<T> : IRequestHandler<T, string> where T : IRequest<string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(T r, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<string>.Success("ok"));
    }

    private sealed class CapturingHandler : IRequestHandler<RedisCorrelatedCmd, string>
    {
        private readonly ICorrelationContext _ctx;
        private readonly Action<long?> _capture;
        public CapturingHandler(ICorrelationContext ctx, Action<long?> capture)
        { _ctx = ctx; _capture = capture; }
        public ValueTask<CatgaResult<string>> HandleAsync(RedisCorrelatedCmd r, CancellationToken ct = default)
        {
            _capture(_ctx.Current);
            return ValueTask.FromResult(CatgaResult<string>.Success("ok"));
        }
    }

    private sealed class FailingHandler : IRequestHandler<RedisFailableCmd, string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(RedisFailableCmd r, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<string>.Failure(
                ErrorInfo.FromException(new Exception("fail"), ErrorCodes.HandlerFailed)));
    }

    private sealed class FaultCapture : IEventHandler<Fault<RedisFailableCmd>>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<Fault<RedisFailableCmd>> _bag;
        public FaultCapture(System.Collections.Concurrent.ConcurrentBag<Fault<RedisFailableCmd>> bag) => _bag = bag;
        public ValueTask HandleAsync(Fault<RedisFailableCmd> e, CancellationToken ct = default)
        { _bag.Add(e); return ValueTask.CompletedTask; }
    }

    private static bool IsDockerRunning()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker", Arguments = "info",
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private sealed class CountingTransport : IMessageTransport
    {
        private readonly Action<long> _onPublish;
        public CountingTransport(Action<long> onPublish) => _onPublish = onPublish;
        public string Name => "Counting";
        public BatchTransportOptions? BatchOptions => null;
        public CompressionTransportOptions? CompressionOptions => null;
        public Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken ct = default) where TMessage : class
        {
            if (message is OutboxCompetingProcessor.EnvelopeMessage env)
                _onPublish(env.Inner.MessageId);
            return Task.CompletedTask;
        }
        public Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SubscribeAsync<TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
        public Task SendBatchAsync<TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken ct = default) where TMessage : class => Task.CompletedTask;
    }
}
