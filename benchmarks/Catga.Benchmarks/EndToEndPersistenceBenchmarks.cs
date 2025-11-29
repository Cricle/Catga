using System.Diagnostics;
using BenchmarkDotNet.Attributes;
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
[SimpleJob(warmupCount: 1, iterationCount: 5)]
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

    [Params("nats", "redis")]
    public string Store { get; set; } = "nats";

    [Params(false, true)]
    public bool TracingEnabled { get; set; }


    [Params(false, true)]
    public bool ResilienceEnabled { get; set; }

    [Params(128, 2048)]
    public int PayloadBytes { get; set; }

    [Params(1, 8, 32)]
    public int Concurrency { get; set; }

    [Params(200)]
    public int BatchCount { get; set; }

    [Params(50, 200)]
    public int ReadPageSize { get; set; }

    [Params("append", "read", "append+read")]
    public string Flow { get; set; } = "append";

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
                    _container.StartAsync().GetAwaiter().GetResult();
                    var port = _container.GetMappedPublicPort(4222);
                    url = $"nats://localhost:{port}";
                }
                else
                {
                    url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
                }
                _nats = new NatsConnection(new NatsOpts { Url = url, ConnectTimeout = TimeSpan.FromSeconds(10) });
                _nats.ConnectAsync().GetAwaiter().GetResult();
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
            var seed = CreateEvents(100);
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
        var list = new List<IEvent>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(new PersistEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = _payload
            });
        }
        return list;
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
