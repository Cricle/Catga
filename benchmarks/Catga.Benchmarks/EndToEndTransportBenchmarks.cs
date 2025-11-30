using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Jobs;
using Catga.Abstractions;
using Catga.Core;
using Catga;
using Catga.DependencyInjection;
using Catga.Observability;
using Catga.Transport;
using Catga.Transport.Nats;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using StackExchange.Redis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Catga.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(E2EConfig))]
[ShortRunJob]
public class EndToEndTransportBenchmarks
{
    private IServiceProvider _sp = null!;
    private IMessageTransport _transport = null!;
    private INatsConnection? _nats;
    private IConnectionMultiplexer? _redis;
    private IContainer? _container;
    private ActivityListener? _listener;
    private volatile int _received;
    private volatile int _target;
    private TaskCompletionSource<bool>? _tcs;
    private byte[] _payload = Array.Empty<byte>();
    private string _redisSubject = string.Empty;
    private bool _skip;

    private static bool Quick => string.Equals(Environment.GetEnvironmentVariable("E2E_QUICK"), "true", StringComparison.OrdinalIgnoreCase);

    [ParamsSource(nameof(BrokerCases))]
    public string Broker { get; set; } = "nats";
    public static IEnumerable<string> BrokerCases() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("E2E_ONLY_BROKER"))
            ? new[] { Environment.GetEnvironmentVariable("E2E_ONLY_BROKER")! }
            : (Quick ? new[] { "nats" } : new[] { "nats", "redis" });

    [ParamsSource(nameof(QosCases))]
    public int Qos { get; set; }
    public static IEnumerable<int> QosCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_QOS"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 0 } : new[] { 0, 1 });

    [ParamsSource(nameof(ConcurrencyCases))]
    public int Concurrency { get; set; }
    public static IEnumerable<int> ConcurrencyCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_CONCURRENCY"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 1, 16 } : new[] { 1, 16, 64 });

    [ParamsSource(nameof(MessageSizeCases))]
    public int MessageSize { get; set; }
    public static IEnumerable<int> MessageSizeCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_MSGSIZE"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 128 } : new[] { 128, 2048 });

    [ParamsSource(nameof(BoolOffThenOn))]
    public bool TracingEnabled { get; set; }

    [ParamsSource(nameof(BoolOffThenOn))]
    public bool EnableBatching { get; set; }

    [ParamsSource(nameof(BoolOffThenOn))]
    public bool NatsBatchingEnabled { get; set; }

    [ParamsSource(nameof(NatsBatchSizeCases))]
    public int NatsBatchSize { get; set; }
    public static IEnumerable<int> NatsBatchSizeCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_NATS_BATCH_SIZE"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 64 } : new[] { 16, 64 });

    [ParamsSource(nameof(NatsBatchTimeoutCases))]
    public int NatsBatchTimeoutMs { get; set; }
    public static IEnumerable<int> NatsBatchTimeoutCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_NATS_BATCH_TIMEOUT_MS"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 5 } : new[] { 5, 50 });

    public static IEnumerable<bool> BoolOffThenOn() => Quick ? new[] { false } : new[] { false, true };

    [GlobalSetup]
    public void Setup()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("E2E_SKIP"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = true;
            return;
        }
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        var builder = services.AddCatga().UseMemoryPack().UseResilience();

        if (TracingEnabled)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = _ => { }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        try
        {
            var useContainers = string.Equals(Environment.GetEnvironmentVariable("E2E_CONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);
            var startTimeoutSec = int.TryParse(Environment.GetEnvironmentVariable("E2E_CONTAINER_TIMEOUT"), out var s) ? Math.Max(s, 1) : 30;
            var connectTimeoutSec = int.TryParse(Environment.GetEnvironmentVariable("E2E_CONNECT_TIMEOUT"), out var c) ? Math.Max(c, 1) : 10;
            if (Broker == "nats")
            {
                string url;
                if (useContainers)
                {
                    _container = new ContainerBuilder()
                        .WithImage("nats:alpine")
                        .WithPortBinding(4222, true)
                        .WithPortBinding(8222, true)
                        .WithCommand("-m", "8222")
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/varz")))
                        .Build();
                    try { _container.StartAsync().WaitAsync(TimeSpan.FromSeconds(startTimeoutSec)).GetAwaiter().GetResult(); }
                    catch { _skip = true; return; }
                    var port = _container.GetMappedPublicPort(4222);
                    url = $"nats://localhost:{port}";
                }
                else
                {
                    url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
                }
                _nats = new NatsConnection(new NatsOpts { Url = url, ConnectTimeout = TimeSpan.FromSeconds(5) });
                try { _nats.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(connectTimeoutSec)).GetAwaiter().GetResult(); }
                catch { _skip = true; return; }
                services.AddSingleton<INatsConnection>(_nats);
                if (NatsBatchingEnabled)
                {
                    var opts = new NatsTransportOptions
                    {
                        SubjectPrefix = "catga.bench",
                        Batch = new BatchTransportOptions
                        {
                            EnableAutoBatching = true,
                            MaxBatchSize = NatsBatchSize,
                            BatchTimeout = TimeSpan.FromMilliseconds(NatsBatchTimeoutMs)
                        }
                    };
                    services.AddNatsTransport(opts);
                }
                else
                {
                    services.AddNatsTransport();
                }
            }
            else
            {
                string conn;
                if (useContainers)
                {
                    _container = new ContainerBuilder()
                        .WithImage("redis:latest")
                        .WithPortBinding(6379, true)
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
                        .Build();
                    try { _container.StartAsync().WaitAsync(TimeSpan.FromSeconds(startTimeoutSec)).GetAwaiter().GetResult(); }
                    catch { _skip = true; return; }
                    var port = _container.GetMappedPublicPort(6379);
                    conn = $"localhost:{port}";
                }
                else
                {
                    conn = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";
                }
                try
                {
                    var options = ConfigurationOptions.Parse(conn);
                    options.ConnectTimeout = Math.Max(connectTimeoutSec * 1000, 1000);
                    options.SyncTimeout = Math.Max(connectTimeoutSec * 1000, 1000);
                    options.AbortOnConnectFail = false;
                    _redis = ConnectionMultiplexer.Connect(options);
                    _ = _redis.GetDatabase().Ping();
                }
                catch { _skip = true; return; }
                services.AddSingleton<IConnectionMultiplexer>(_redis);
                services.AddRedisTransport(o =>
                {
                    o.ChannelPrefix = "catga.bench.";
                    o.RegistConnection = false;
                    if (EnableBatching)
                        o.Batch = new BatchTransportOptions { EnableAutoBatching = true, MaxBatchSize = 32, BatchTimeout = TimeSpan.FromMilliseconds(5) };
                });
                _redisSubject = "catga.bench." + typeof(PingMessage).Name;
            }
        }
        catch
        {
            _skip = true;
        }

        _sp = services.BuildServiceProvider();
        if (_skip) return;

        _transport = _sp.GetRequiredService<IMessageTransport>();

        _payload = new byte[MessageSize];
        Random.Shared.NextBytes(_payload);

        _transport.SubscribeAsync<PingMessage>(OnMessage).GetAwaiter().GetResult();
        Task.Delay(20).GetAwaiter().GetResult();
    }

    private Task OnMessage(PingMessage msg, TransportContext ctx)
    {
        var c = Interlocked.Increment(ref _received);
        if (c >= _target)
            _tcs?.TrySetResult(true);
        return Task.CompletedTask;
    }

    [Benchmark(Baseline = true, Description = "E2E Publish->Receive (single)")]
    public async Task EndToEnd_Single()
    {
        if (_skip) return;
        Prepare(1);
        var msg = NewMessage();
        if (Broker == "redis" && Qos == 1)
            await _transport.SendAsync(msg, _redisSubject);
        else
            await _transport.PublishAsync(msg);
        await WaitAsync();
    }

    [Benchmark(Description = "E2E Throughput (N messages)")]
    public async Task EndToEnd_Throughput()
    {
        if (_skip) return;
        var total = Math.Max(Concurrency * (Quick ? 10 : 50), 1);
        Prepare(total);
        var per = (total + Concurrency - 1) / Concurrency;
        var tasks = new Task[Concurrency];
        for (int i = 0; i < Concurrency; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < per; j++)
                {
                    var msg = NewMessage();
                    if (Broker == "redis" && Qos == 1)
                        await _transport.SendAsync(msg, _redisSubject);
                    else
                        await _transport.PublishAsync(msg);
                }
            });
        }
        await Task.WhenAll(tasks);
        await WaitAsync();
    }

    private void Prepare(int target)
    {
        Interlocked.Exchange(ref _received, 0);
        _target = target;
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private PingMessage NewMessage()
        => new PingMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Payload = _payload,
            Q = Qos == 0 ? QualityOfService.AtMostOnce : QualityOfService.AtLeastOnce
        };

    private async Task WaitAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await (_tcs?.Task ?? Task.CompletedTask).WaitAsync(cts.Token); }
        catch { }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_sp is IAsyncDisposable ad) { try { ad.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { } }
        else if (_sp is IDisposable d) { try { d.Dispose(); } catch { } }
        _listener?.Dispose();
        try { _nats?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { _redis?.Dispose(); } catch { }
        try { _container?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
    }
}

[MemoryPackable]
public partial record PingMessage : IMessage
{
    public required long MessageId { get; init; }
    public required byte[] Payload { get; init; }
    public QualityOfService Q { get; init; } = QualityOfService.AtMostOnce;
    QualityOfService IMessage.QoS => Q;
}

internal sealed class E2EConfig : ManualConfig
{
    public E2EConfig()
    {
        AddColumn(new ScenarioColumn());
    }
}

internal sealed class ScenarioColumn : IColumn
{
    public string Id => nameof(ScenarioColumn);
    public string ColumnName => "Scenario";
    public string Legend => string.Empty;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => Format(benchmarkCase);
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsHidden => false;

    private static string GetParam(BenchmarkCase c, string name)
    {
        foreach (var p in c.Parameters.Items)
        {
            if (string.Equals(p.Name, name, StringComparison.Ordinal))
                return p.Value?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string Format(BenchmarkCase c)
    {
        var broker = GetParam(c, nameof(EndToEndTransportBenchmarks.Broker));
        var qos = GetParam(c, nameof(EndToEndTransportBenchmarks.Qos));
        var conc = GetParam(c, nameof(EndToEndTransportBenchmarks.Concurrency));
        var size = GetParam(c, nameof(EndToEndTransportBenchmarks.MessageSize));
        var tracing = GetParam(c, nameof(EndToEndTransportBenchmarks.TracingEnabled));
        var batch = GetParam(c, nameof(EndToEndTransportBenchmarks.EnableBatching));
        var nb = GetParam(c, nameof(EndToEndTransportBenchmarks.NatsBatchingEnabled));
        var nbs = GetParam(c, nameof(EndToEndTransportBenchmarks.NatsBatchSize));
        var nbt = GetParam(c, nameof(EndToEndTransportBenchmarks.NatsBatchTimeoutMs));
        var suffix = string.Equals(broker, "nats", StringComparison.OrdinalIgnoreCase)
            ? $"|nb{nb}|nbs{nbs}|nbt{nbt}"
            : string.Empty;
        return $"{broker}|q{qos}|c{conc}|s{size}|t{tracing}|b{batch}{suffix}";
    }
}
