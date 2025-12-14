using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Main DSL builder for flow configuration.
/// Step methods (Send, Query, Publish, etc.) are provided via extension methods.
/// </summary>
internal class FlowBuilder<TState> : IFlowBuilder<TState> where TState : class, IFlowState
{
    public string? FlowName { get; private set; }
    public List<FlowStep> Steps { get; } = [];
    public TimeSpan DefaultTimeout { get; private set; } = TimeSpan.FromMinutes(10);
    public int DefaultRetries { get; private set; }
    public Dictionary<string, TimeSpan> TaggedTimeouts { get; } = [];
    public Dictionary<string, int> TaggedRetries { get; } = [];
    public HashSet<string> TaggedPersist { get; } = [];

    public Func<TState, IEvent>? OnFlowCompletedFactory { get; private set; }
    public Func<TState, string?, IEvent>? OnFlowFailedFactory { get; private set; }
    public Func<TState, int, IEvent>? OnStepCompletedFactory { get; private set; }
    public Func<TState, int, string?, IEvent>? OnStepFailedFactory { get; private set; }

    public IFlowBuilder<TState> Name(string name)
    {
        FlowName = name;
        return this;
    }

    public ITaggedSetting Timeout(TimeSpan timeout)
    {
        return new TaggedTimeoutSetting(this, timeout, setDefault: true);
    }

    public ITaggedSetting Retry(int maxRetries)
    {
        DefaultRetries = maxRetries;
        return new TaggedRetrySetting(this, maxRetries);
    }

    public ITaggedSetting Persist()
    {
        return new TaggedPersistSetting(this);
    }

    public IFlowBuilder<TState> OnStepCompleted<TEvent>(Func<TState, int, TEvent> factory) where TEvent : IEvent
    {
        OnStepCompletedFactory = (s, step) => factory(s, step);
        return this;
    }

    public IFlowBuilder<TState> OnStepFailed<TEvent>(Func<TState, int, string?, TEvent> factory) where TEvent : IEvent
    {
        OnStepFailedFactory = (s, step, error) => factory(s, step, error);
        return this;
    }

    public IFlowBuilder<TState> OnFlowCompleted<TEvent>(Func<TState, TEvent> factory) where TEvent : IEvent
    {
        OnFlowCompletedFactory = s => factory(s);
        return this;
    }

    public IFlowBuilder<TState> OnFlowFailed<TEvent>(Func<TState, string?, TEvent> factory) where TEvent : IEvent
    {
        OnFlowFailedFactory = (s, error) => factory(s, error);
        return this;
    }

    // Tagged settings
    private class TaggedTimeoutSetting : ITaggedSetting
    {
        private readonly FlowBuilder<TState> _builder;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _previousDefault;

        public TaggedTimeoutSetting(FlowBuilder<TState> builder, TimeSpan timeout, bool setDefault)
        {
            _builder = builder;
            _timeout = timeout;
            _previousDefault = builder.DefaultTimeout;

            if (setDefault)
                builder.DefaultTimeout = timeout;
        }

        public void ForTags(params string[] tags)
        {
            _builder.DefaultTimeout = _previousDefault;
            foreach (var tag in tags)
                _builder.TaggedTimeouts[tag] = _timeout;
        }
    }

    private class TaggedRetrySetting(FlowBuilder<TState> builder, int retries) : ITaggedSetting
    {
        public void ForTags(params string[] tags)
        {
            foreach (var tag in tags)
                builder.TaggedRetries[tag] = retries;
        }
    }

    private class TaggedPersistSetting(FlowBuilder<TState> builder) : ITaggedSetting
    {
        public void ForTags(params string[] tags)
        {
            foreach (var tag in tags)
                builder.TaggedPersist.Add(tag);
        }
    }
}
