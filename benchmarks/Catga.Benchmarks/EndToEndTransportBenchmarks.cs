using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
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
[SimpleJob(warmupCount: 1, iterationCount: 5)]
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

    [Params("nats", "redis")]
    public string Broker { get; set; } = "nats";

    [Params(0, 1)]
    public int Qos { get; set; }

    [Params(1, 16, 64)]
    public int Concurrency { get; set; }

    [Params(128, 2048)]
    public int MessageSize { get; set; }

    [Params(false, true)]
    public bool TracingEnabled { get; set; }

    [Params(false, true)]
    public bool EnableBatching { get; set; }

    [Params(false, true)]
    public bool NatsBatchingEnabled { get; set; }

    [Params(16, 64)]
    public int NatsBatchSize { get; set; }

    [Params(5, 50)]
    public int NatsBatchTimeoutMs { get; set; }

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
            if (Broker == "nats")
            {
                string url;
                if (useContainers)
                {
                    _container = new ContainerBuilder()
                        .WithImage("nats:latest")
                        .WithPortBinding(4222, true)
                        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4222))
                        .Build();
                    _container.StartAsync().GetAwaiter().GetResult();
                    var port = _container.GetMappedPublicPort(4222);
                    url = $"nats://localhost:{port}";
                }
                else
                {
                    url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
                }
                _nats = new NatsConnection(new NatsOpts { Url = url, ConnectTimeout = TimeSpan.FromSeconds(5) });
                _nats.ConnectAsync().GetAwaiter().GetResult();
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
                    _container.StartAsync().GetAwaiter().GetResult();
                    var port = _container.GetMappedPublicPort(6379);
                    conn = $"localhost:{port}";
                }
                else
                {
                    conn = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";
                }
                _redis = ConnectionMultiplexer.Connect(conn);
                _ = _redis.GetDatabase().Ping();
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
        Thread.Sleep(100);
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
        var total = Math.Max(Concurrency * 50, 1);
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await (_tcs?.Task ?? Task.CompletedTask).WaitAsync(cts.Token); }
        catch { }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_sp is IDisposable d) d.Dispose();
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
