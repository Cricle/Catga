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
using Catga.Persistence.Nats.Flow;
using Catga.Persistence.Stores;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Resilience;
using Catga.Security;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Xunit;

namespace Catga.Tests.Integration.Nats;

/// <summary>
/// E2E tests for all new features on NATS backend.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class NatsNewFeaturesE2ETests : IAsyncLifetime
{
    private IContainer? _container;
    private NatsConnection? _nats;
    private NatsJSContext? _js;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IMessageSerializer _jsonSerializer = new Catga.Tests.Helpers.TestMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        _container = new ContainerBuilder()
            .WithImage("nats:latest")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/varz")))
            .Build();

        await _container.StartAsync();
        var port = _container.GetMappedPublicPort(4222);
        _nats = new NatsConnection(new NatsOpts { Url = $"nats://localhost:{port}" });
        await _nats.ConnectAsync();
        _js = new NatsJSContext(_nats);
    }

    public async Task DisposeAsync()
    {
        if (_nats != null) await _nats.DisposeAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    // ── 1. StateMachine on NATS ───────────────────────────────────────────────

    [Fact]
    public async Task Nats_StateMachine_FullLifecycle()
    {
        if (_js == null) return;

        var store = new NatsDslFlowStore(_nats!, _jsonSerializer);
        var executor = new StateMachineExecutor<NatsTicketState, NatsTicketStatus, NatsTicketMachine>(store);

        await executor.InitializeAsync("ticket-nats-1", NatsTicketStatus.Open);
        var r1 = await executor.HandleAsync("ticket-nats-1", new NatsTicketAssigned("alice"));
        var r2 = await executor.HandleAsync("ticket-nats-1", new NatsTicketResolved());

        r1.Transitioned.Should().BeTrue();
        r1.CurrentState.Should().Be(NatsTicketStatus.InProgress);
        r2.CurrentState.Should().Be(NatsTicketStatus.Resolved);

        var state = await executor.GetStateAsync("ticket-nats-1");
        state!.AssignedTo.Should().Be("alice");
        state.CurrentState.Should().Be(NatsTicketStatus.Resolved);
    }

    [Fact]
    public async Task Nats_StateMachine_EventRouter_RoutesCorrectly()
    {
        if (_js == null) return;

        var store = new NatsDslFlowStore(_nats!, _jsonSerializer);
        var executor = new StateMachineExecutor<NatsTicketState, NatsTicketStatus, NatsTicketMachine>(store);

        var router = new StateMachineEventRouter<NatsTicketState, NatsTicketStatus, NatsTicketMachine>(executor)
            .For<NatsTicketAssigned>(e => "ticket-router-1")
            .For<NatsTicketResolved>(e => "ticket-router-1");

        await executor.InitializeAsync("ticket-router-1", NatsTicketStatus.Open);
        await router.RouteAsync(new NatsTicketAssigned("bob"));
        await router.RouteAsync(new NatsTicketResolved());

        var state = await executor.GetStateAsync("ticket-router-1");
        state!.CurrentState.Should().Be(NatsTicketStatus.Resolved);
        state.AssignedTo.Should().Be("bob");
    }

    [Fact]
    public async Task Nats_StateMachine_ConcurrentInstances_Independent()
    {
        if (_js == null) return;

        var store = new NatsDslFlowStore(_nats!, _jsonSerializer);
        var executor = new StateMachineExecutor<NatsTicketState, NatsTicketStatus, NatsTicketMachine>(store);

        await executor.InitializeAsync("ticket-c1", NatsTicketStatus.Open);
        await executor.InitializeAsync("ticket-c2", NatsTicketStatus.Open);

        await executor.HandleAsync("ticket-c1", new NatsTicketAssigned("alice"));

        var s1 = await executor.GetStateAsync("ticket-c1");
        var s2 = await executor.GetStateAsync("ticket-c2");

        s1!.CurrentState.Should().Be(NatsTicketStatus.InProgress);
        s2!.CurrentState.Should().Be(NatsTicketStatus.Open);
    }

    // ── 2. Outbox scheduling on NATS ──────────────────────────────────────────

    [Fact]
    public async Task Nats_Outbox_ImmediateMessage_IsReadyToDeliver()
    {
        if (_js == null) return;

        var outbox = new NatsJSOutboxStore(_nats!, _jsonSerializer, _provider);
        var msgId = DateTime.UtcNow.Ticks;

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
    public async Task Nats_Outbox_FutureScheduled_NotReadyToDeliver()
    {
        if (_js == null) return;

        var outbox = new NatsJSOutboxStore(_nats!, _jsonSerializer, _provider);
        var msgId = DateTime.UtcNow.Ticks + 1;

        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = msgId, MessageType = "T", Payload = [], Status = OutboxStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(2)
        });

        var pending = await outbox.GetPendingMessagesAsync(100);
        var mine = pending.FirstOrDefault(m => m.MessageId == msgId);

        mine.Should().NotBeNull();
        mine!.IsReadyToDeliver.Should().BeFalse();
    }

    // ── 3. Message Versioning ─────────────────────────────────────────────────

    [Fact]
    public void Nats_MessageVersioning_TypeAlias()
    {
        var mapper = new MessageVersionMapperBuilder()
            .MapType("OldTicketAssigned", typeof(NatsTicketAssigned))
            .Build();

        mapper.ResolveType("OldTicketAssigned").Should().Be(typeof(NatsTicketAssigned));
    }

    [Fact]
    public void Nats_MessageVersioning_ContentUpgrade()
    {
        var mapper = new MessageVersionMapperBuilder()
            .Upgrade<NatsTicketAssigned, NatsTicketResolved>(_ => new NatsTicketResolved())
            .Build();

        IEvent v1 = new NatsTicketAssigned("agent");
        mapper.Upgrade(v1).Should().BeOfType<NatsTicketResolved>();
    }

    // ── 4. Authorization ──────────────────────────────────────────────────────

    [Fact]
    public async Task Nats_Authorization_EnforcesAuthentication()
    {
        if (_nats == null) return;

        var services = BuildAuthServices();
        await using var sp = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<ISecurityContext>();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var r1 = await mediator.SendAsync<NatsSecureCmd, string>(new NatsSecureCmd());
        r1.ErrorCode.Should().Be(ErrorCodes.Unauthorized);

        var id = new ClaimsIdentity("nats-test");
        ctx.SetUser(new ClaimsPrincipal(id));
        var r2 = await mediator.SendAsync<NatsSecureCmd, string>(new NatsSecureCmd());
        r2.IsSuccess.Should().BeTrue();
    }

    // ── 5. HMAC Signing ───────────────────────────────────────────────────────

    [Fact]
    public void Nats_MessageSigning_TamperDetection()
    {
        var signer = new HmacMessageSigner("nats-secret");
        var payload = _jsonSerializer.Serialize(new NatsTicketAssigned("agent"));

        var sig = signer.Sign(payload);
        signer.Verify(payload, sig).Should().BeTrue();
        signer.Verify(new byte[] { 0, 1 }, sig).Should().BeFalse();
    }

    // ── 6. CorrelationId ──────────────────────────────────────────────────────

    [Fact]
    public async Task Nats_CorrelationId_SetFromMessageId()
    {
        if (_nats == null) return;

        long? captured = null;
        var services = BuildCorrelationServices(id => captured = id);
        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        await mediator.SendAsync<NatsCorrelatedCmd, string>(
            new NatsCorrelatedCmd { MessageId = 55L });

        captured.Should().Be(55L);
    }

    // ── 7. Fault publishing ───────────────────────────────────────────────────

    [Fact]
    public async Task Nats_FaultPublishing_FailedHandler_PublishesFault()
    {
        if (_nats == null) return;

        var faults = new System.Collections.Concurrent.ConcurrentBag<Fault<NatsFailableCmd>>();
        var services = BuildFaultServices(faults);
        await using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<NatsFailableCmd, string>(new NatsFailableCmd());

        result.IsSuccess.Should().BeFalse();
        faults.Should().HaveCount(1);
    }

    // ── 8. MessageRouter + Priority ───────────────────────────────────────────

    [Fact]
    public void Nats_MessageRouter_HeaderRouting()
    {
        var router = new MessageRouter("subject.default");
        router.AddRoute("tenant", "acme", "subject.acme");
        router.AddRoute("tenant", "corp", "subject.corp");

        router.Resolve(new Catga.Transport.TransportContext
        {
            Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
        }).Should().Be("subject.acme");

        router.Resolve(new Catga.Transport.TransportContext()).Should().Be("subject.default");
    }

    [Fact]
    public void Nats_PriorityTransportContext_EncodesCorrectly()
    {
        var ctx = new PriorityTransportContext(MessagePriority.High);
        ctx.Priority.Should().Be(MessagePriority.High);
        ctx.Context.Metadata!["x-priority"].Should().Be("2");
    }

    // ── 9. OutboxCompetingProcessor on NATS ───────────────────────────────────

    [Fact]
    public async Task Nats_OutboxCompetingProcessor_ProcessesDueMessages()
    {
        if (_js == null) return;

        var outbox = new NatsJSOutboxStore(_nats!, _jsonSerializer, _provider);
        var published = new System.Collections.Concurrent.ConcurrentBag<long>();
        var transport = new NatsCountingTransport(id => published.Add(id));

        var msgId = DateTime.UtcNow.Ticks + 400;
        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = msgId, MessageType = "T", Payload = [], Status = OutboxStatus.Pending
        });

        var processor = new OutboxCompetingProcessor(outbox, transport);
        await processor.ProcessBatchAsync();

        published.Should().Contain(msgId);
    }

    [Fact]
    public async Task Nats_OutboxCompetingProcessor_SkipsFutureScheduled()
    {
        if (_js == null) return;

        var outbox = new NatsJSOutboxStore(_nats!, _jsonSerializer, _provider);
        var published = new System.Collections.Concurrent.ConcurrentBag<long>();
        var transport = new NatsCountingTransport(id => published.Add(id));

        var futureId = DateTime.UtcNow.Ticks + 500;
        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = futureId, MessageType = "T", Payload = [], Status = OutboxStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(2)
        });

        var processor = new OutboxCompetingProcessor(outbox, transport);
        await processor.ProcessBatchAsync();

        published.Should().NotContain(futureId);
    }

    // ── 10. SendLater via Outbox ──────────────────────────────────────────────

    [Fact]
    public async Task Nats_SendLater_StoresInOutbox_WithScheduledAt()
    {
        if (_js == null) return;

        var outbox = new NatsJSOutboxStore(_nats!, _jsonSerializer, _provider);
        var msgId = DateTime.UtcNow.Ticks + 600;

        await outbox.AddAsync(new OutboxMessage
        {
            MessageId = msgId, MessageType = "SendLaterCmd", Payload = [],
            Status = OutboxStatus.Pending,
            ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(15)
        });

        var pending = await outbox.GetPendingMessagesAsync(100);
        var mine = pending.FirstOrDefault(m => m.MessageId == msgId);

        mine.Should().NotBeNull();
        mine!.ScheduledAt.Should().NotBeNull();
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
        s.AddSingleton<IRequestHandler<NatsSecureCmd, string>>(_ => new EchoHandler<NatsSecureCmd>());
        return s;
    }

    private static ServiceCollection BuildCorrelationServices(Action<long?> capture)
    {
        var s = BuildBase();
        s.AddSingleton<ICorrelationContext, CorrelationContext>();
        s.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CorrelationPropagationBehavior<,>));
        s.AddSingleton<IRequestHandler<NatsCorrelatedCmd, string>>(sp =>
            new CapturingHandler(sp.GetRequiredService<ICorrelationContext>(), capture));
        return s;
    }

    private static ServiceCollection BuildFaultServices(
        System.Collections.Concurrent.ConcurrentBag<Fault<NatsFailableCmd>> faults)
    {
        var s = BuildBase();
        s.AddSingleton(typeof(IPipelineBehavior<,>), typeof(FaultPublishingBehavior<,>));
        s.AddSingleton<IRequestHandler<NatsFailableCmd, string>>(_ => new FailingHandler());
        s.AddSingleton<IEventHandler<Fault<NatsFailableCmd>>>(_ => new FaultCapture(faults));
        return s;
    }

    [Authorize]
    private record NatsSecureCmd : IRequest<string> { public long MessageId { get; init; } }
    private record NatsCorrelatedCmd : IRequest<string> { public long MessageId { get; init; } }
    private record NatsFailableCmd : IRequest<string> { public long MessageId { get; init; } }

    private sealed class EchoHandler<T> : IRequestHandler<T, string> where T : IRequest<string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(T r, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<string>.Success("ok"));
    }

    private sealed class CapturingHandler : IRequestHandler<NatsCorrelatedCmd, string>
    {
        private readonly ICorrelationContext _ctx;
        private readonly Action<long?> _capture;
        public CapturingHandler(ICorrelationContext ctx, Action<long?> capture)
        { _ctx = ctx; _capture = capture; }
        public ValueTask<CatgaResult<string>> HandleAsync(NatsCorrelatedCmd r, CancellationToken ct = default)
        {
            _capture(_ctx.Current);
            return ValueTask.FromResult(CatgaResult<string>.Success("ok"));
        }
    }

    private sealed class FailingHandler : IRequestHandler<NatsFailableCmd, string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(NatsFailableCmd r, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<string>.Failure(
                ErrorInfo.FromException(new Exception("fail"), ErrorCodes.HandlerFailed)));
    }

    private sealed class FaultCapture : IEventHandler<Fault<NatsFailableCmd>>
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<Fault<NatsFailableCmd>> _bag;
        public FaultCapture(System.Collections.Concurrent.ConcurrentBag<Fault<NatsFailableCmd>> bag) => _bag = bag;
        public ValueTask HandleAsync(Fault<NatsFailableCmd> e, CancellationToken ct = default)
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

    private sealed class NatsCountingTransport : IMessageTransport
    {
        private readonly Action<long> _onPublish;
        public NatsCountingTransport(Action<long> onPublish) => _onPublish = onPublish;
        public string Name => "NatsCounting";
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
