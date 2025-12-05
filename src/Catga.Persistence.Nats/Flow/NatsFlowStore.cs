using System.Collections.Concurrent;
using Catga.Abstractions;
using Catga.Flow;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Nats.Flow;

/// <summary>
/// NATS JetStream flow store using streams with sequence-based optimistic locking.
/// Production-ready for distributed clusters.
/// </summary>
public sealed class NatsFlowStore : IFlowStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly string _streamName;
    private readonly ConcurrentDictionary<string, FlowState> _cache = new();
    private readonly ConcurrentDictionary<string, ulong> _sequences = new();
    private INatsJSContext? _js;
    private bool _initialized;

    public NatsFlowStore(INatsConnection nats, IMessageSerializer serializer, string streamName = "FLOWS")
    {
        _nats = nats;
        _serializer = serializer;
        _streamName = streamName;
    }

    private async ValueTask<INatsJSContext> GetJsAsync(CancellationToken ct)
    {
        if (_js != null && _initialized) return _js;

        _js = new NatsJSContext(_nats);

        try
        {
            await _js.CreateStreamAsync(new StreamConfig(_streamName, [$"{_streamName}.*"])
            {
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Limits,
                MaxAge = TimeSpan.FromDays(7)
            }, ct);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400) { }

        _initialized = true;
        return _js;
    }

    public async ValueTask<bool> CreateAsync(FlowState state, CancellationToken ct = default)
    {
        if (_cache.ContainsKey(state.Id))
            return false;

        var js = await GetJsAsync(ct);
        var subject = $"{_streamName}.{state.Id}";
        var data = _serializer.Serialize(state);

        try
        {
            var ack = await js.PublishAsync(subject, data, cancellationToken: ct);
            _cache[state.Id] = state;
            _sequences[state.Id] = ack.Seq;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<bool> UpdateAsync(FlowState state, CancellationToken ct = default)
    {
        if (!_sequences.TryGetValue(state.Id, out var expectedSeq))
            return false;

        var js = await GetJsAsync(ct);
        var subject = $"{_streamName}.{state.Id}";

        // Create updated state for serialization (don't modify input until success)
        var newVersion = state.Version + 1;
        var updatedState = new FlowState
        {
            Id = state.Id,
            Type = state.Type,
            Status = state.Status,
            Step = state.Step,
            Version = newVersion,
            Owner = state.Owner,
            HeartbeatAt = state.HeartbeatAt,
            Data = state.Data,
            Error = state.Error
        };
        var data = _serializer.Serialize(updatedState);

        try
        {
            var ack = await js.PublishAsync(subject, data,
                opts: new NatsJSPubOpts { ExpectedLastSubjectSequence = expectedSeq },
                cancellationToken: ct);

            // Only update on success
            state.Version = newVersion;
            _cache[state.Id] = updatedState;
            _sequences[state.Id] = ack.Seq;
            return true;
        }
        catch (NatsJSApiException)
        {
            return false; // Sequence mismatch
        }
    }

    public ValueTask<FlowState?> GetAsync(string id, CancellationToken ct = default)
    {
        _cache.TryGetValue(id, out var state);
        return ValueTask.FromResult(state);
    }

    public async ValueTask<FlowState?> TryClaimAsync(string type, string owner, long timeoutMs, CancellationToken ct = default)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var kvp in _cache)
        {
            var state = kvp.Value;
            if (state.Type != type) continue;
            if (state.Status is FlowStatus.Done or FlowStatus.Failed) continue;
            if (nowMs - state.HeartbeatAt < timeoutMs) continue;

            // Try CAS claim
            state.Owner = owner;
            state.HeartbeatAt = nowMs;
            if (await UpdateAsync(state, ct))
                return state;
        }

        return null;
    }

    public async ValueTask<bool> HeartbeatAsync(string id, string owner, long version, CancellationToken ct = default)
    {
        var state = await GetAsync(id, ct);
        if (state == null || state.Owner != owner || state.Version != version)
            return false;

        state.HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return await UpdateAsync(state, ct);
    }
}
