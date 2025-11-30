using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Resilience;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using StackExchange.Redis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Catga.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(PersistConfig))]
[ShortRunJob]
public class EndToEndPersistenceBenchmarks
{
    private IServiceProvider _sp = null!;
    private IEventStore? _eventStore;
    private IOutboxStore? _outbox;
    private IInboxStore? _inbox;
    private INatsConnection? _nats;
    private IConnectionMultiplexer? _redis;
    private IContainer? _container;
    private ActivityListener? _listener;
    private string _streamId = string.Empty;
    private byte[] _payload = Array.Empty<byte>();
    private bool _skip;
    private List<IEvent>? _eventsCache;

    private static bool Quick => string.Equals(Environment.GetEnvironmentVariable("E2E_QUICK"), "true", StringComparison.OrdinalIgnoreCase);

    [ParamsSource(nameof(StoreCases))]
    public string Store { get; set; } = "nats";
    public static IEnumerable<string> StoreCases() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("E2E_ONLY_STORE"))
            ? new[] { Environment.GetEnvironmentVariable("E2E_ONLY_STORE")! }
            : (Quick ? new[] { "nats" } : new[] { "nats", "redis" });

    [ParamsSource(nameof(BoolOffThenOn))]
    public bool TracingEnabled { get; set; }


    [ParamsSource(nameof(BoolOffThenOn))]
    public bool ResilienceEnabled { get; set; }

    [ParamsSource(nameof(PayloadCases))]
    public int PayloadBytes { get; set; }

    [ParamsSource(nameof(ConcurrencyCases))]
    public int Concurrency { get; set; }

    [ParamsSource(nameof(BatchCountCases))]
    public int BatchCount { get; set; }

    [ParamsSource(nameof(ReadPageCases))]
    public int ReadPageSize { get; set; }

    [ParamsSource(nameof(FlowCases))]
    public string Flow { get; set; } = "append";

    public static IEnumerable<bool> BoolOffThenOn() => Quick ? new[] { false } : new[] { false, true };
    public static IEnumerable<int> PayloadCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_PAYLOAD"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 128 } : new[] { 128, 2048 });
    public static IEnumerable<int> ConcurrencyCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_CONCURRENCY"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 1 } : new[] { 1, 8, 32 });
    public static IEnumerable<int> BatchCountCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_BATCHCOUNT"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 100 } : new[] { 200 });
    public static IEnumerable<int> ReadPageCases() =>
        int.TryParse(Environment.GetEnvironmentVariable("E2E_ONLY_READPAGE"), out var n)
            ? new[] { n }
            : (Quick ? new[] { 50 } : new[] { 50, 200 });
    public static IEnumerable<string> FlowCases() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("E2E_ONLY_FLOW"))
            ? new[] { Environment.GetEnvironmentVariable("E2E_ONLY_FLOW")! }
            : (Quick ? new[] { "append" } : new[] { "append", "read", "append+read" });

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
        var builder = services.AddCatga().UseMemoryPack();
        if (ResilienceEnabled) builder.UseResilience(); else services.AddSingleton<IResiliencePipelineProvider, NoopResiliencePipelineProvider>();

        if (TracingEnabled)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
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
            if (Store == "nats")
            {
                string url;
                if (useContainers)
                {
                    _container = new ContainerBuilder()
                        .WithImage("nats:latest")
                        .WithPortBinding(4222, true)
                        .WithPortBinding(8222, true)
                        .WithCommand("-js", "-m", "8222")
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
                services.AddSingleton(_nats);
                services.AddNatsEventStore();
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
                services.AddRedisPersistence();
            }
        }
        catch
        {
            _skip = true;
        }

        _sp = services.BuildServiceProvider();
        if (_skip) return;

        _eventStore = _sp.GetService<IEventStore>();
        _outbox = _sp.GetService<IOutboxStore>();
        _inbox = _sp.GetService<IInboxStore>();

        _payload = new byte[PayloadBytes];
        Random.Shared.NextBytes(_payload);

        if (Store == "nats" && _eventStore != null)
        {
            _streamId = $"bench-{Guid.NewGuid():N}";
            var initial = Quick ? Math.Max(ReadPageSize, 20) : Math.Max(ReadPageSize, 100);
            var seed = CreateEvents(initial);
            _eventStore.AppendAsync(_streamId, seed).GetAwaiter().GetResult();
        }
    }

    private async Task AppendWorkAsync()
    {
        if (Store == "nats" && _eventStore != null)
        {
            var msgs = CreateEvents(BatchCount);
            await _eventStore.AppendAsync(_streamId, msgs, -1);
        }
        else if (Store == "redis" && _outbox != null)
        {
            var per = Math.Max(BatchCount / Concurrency, 1);
            var tasks = new Task[Concurrency];
            for (int i = 0; i < Concurrency; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < per; j++)
                    {
                        var msg = new OutboxMessage
                        {
                            MessageId = MessageExtensions.NewMessageId(),
                            MessageType = typeof(PersistEvent).FullName!,
                            Payload = _payload
                        };
                        await _outbox.AddAsync(msg);
                    }
                });
            }
            await Task.WhenAll(tasks);
        }
    }

    private async Task ReadOrProcessWorkAsync()
    {
        if (Store == "nats" && _eventStore != null)
        {
            await _eventStore.ReadAsync(_streamId, 0, ReadPageSize);
        }
        else if (Store == "redis" && _inbox != null)
        {
            var per = Math.Max(BatchCount / Concurrency, 1);
            var tasks = new Task[Concurrency];
            for (int i = 0; i < Concurrency; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < per; j++)
                    {
                        var id = MessageExtensions.NewMessageId();
                        var ok = await _inbox.TryLockMessageAsync(id, TimeSpan.FromSeconds(5));
                        if (ok)
                        {
                            var im = new InboxMessage
                            {
                                MessageId = id,
                                MessageType = typeof(PersistEvent).FullName!,
                                Payload = _payload
                            };
                            await _inbox.MarkAsProcessedAsync(im);
                        }
                    }
                });
            }
            await Task.WhenAll(tasks);
        }
    }

    [Benchmark(Baseline = true, Description = "Persist.Append")]
    public async Task Persist_Append()
    {
        if (_skip) return;
        await AppendWorkAsync();
    }

    [Benchmark(Description = "Persist.ReadOrProcess")]
    public async Task Persist_ReadOrProcess()
    {
        if (_skip) return;
        await ReadOrProcessWorkAsync();
    }

    [Benchmark(Description = "Persist.Flow")]
    public async Task Persist_Flow()
    {
        if (_skip) return;
        if (Flow == "append" || Flow == "append+read")
            await AppendWorkAsync();
        if (Flow == "read" || Flow == "append+read")
            await ReadOrProcessWorkAsync();
    }

    private List<IEvent> CreateEvents(int n)
    {
        if (_eventsCache == null || _eventsCache.Capacity < n)
            _eventsCache = new List<IEvent>(n);
        else
            _eventsCache.Clear();

        for (int i = 0; i < n; i++)
        {
            _eventsCache.Add(new PersistEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = _payload
            });
        }
        return _eventsCache;
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
public partial record PersistEvent : IEvent
{
    public required long MessageId { get; init; }
    public required byte[] Data { get; init; }
}

internal sealed class PersistConfig : ManualConfig
{
    public PersistConfig()
    {
        AddColumn(new PersistScenarioColumn());
    }
}

internal sealed class PersistScenarioColumn : IColumn
{
    public string Id => nameof(PersistScenarioColumn);
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
        var store = GetParam(c, nameof(EndToEndPersistenceBenchmarks.Store));
        var conc = GetParam(c, nameof(EndToEndPersistenceBenchmarks.Concurrency));
        var size = GetParam(c, nameof(EndToEndPersistenceBenchmarks.PayloadBytes));
        var tracing = GetParam(c, nameof(EndToEndPersistenceBenchmarks.TracingEnabled));
        var resil = GetParam(c, nameof(EndToEndPersistenceBenchmarks.ResilienceEnabled));
        var batch = GetParam(c, nameof(EndToEndPersistenceBenchmarks.BatchCount));
        var page = GetParam(c, nameof(EndToEndPersistenceBenchmarks.ReadPageSize));
        var flow = GetParam(c, nameof(EndToEndPersistenceBenchmarks.Flow));
        return $"{store}|c{conc}|s{size}|t{tracing}|r{resil}|b{batch}|p{page}|f{flow}";
    }
}
