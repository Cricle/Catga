using Catga.DependencyInjection;
using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Flow.StateMachine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Flow.DependencyInjection;

public static class StateMachineServiceCollectionExtensions
{
    /// <summary>
    /// Register a state machine executor.
    /// Requires IDslFlowStore to be registered.
    /// </summary>
    public static IServiceCollection AddStateMachine<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
        TStateEnum,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig>(
        this IServiceCollection services)
        where TState : class, IStateMachineState<TStateEnum>, new()
        where TStateEnum : struct, Enum
        where TConfig : StateMachineConfig<TState, TStateEnum>, new()
    {
        services.TryAddSingleton<TConfig>();
        services.TryAddSingleton<IStateMachineExecutor<TState, TStateEnum>>(sp =>
            sp.GetRequiredService<StateMachineExecutor<TState, TStateEnum, TConfig>>());
        services.TryAddSingleton<StateMachineExecutor<TState, TStateEnum, TConfig>>(sp =>
            new StateMachineExecutor<TState, TStateEnum, TConfig>(
                sp.GetRequiredService<IDslFlowStore>(),
                sp.GetService<TConfig>()));
        RegisterConfiguredStateMachineEvents<TState, TStateEnum, TConfig>(services);
        return services;
    }

    /// <summary>
    /// Registers an event bridge so published events can drive the state machine.
    /// </summary>
    public static IServiceCollection AddStateMachineEvent<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
        TStateEnum,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        this IServiceCollection services,
        Func<TEvent, string> instanceIdSelector)
        where TState : class, IStateMachineState<TStateEnum>, new()
        where TStateEnum : struct, Enum
        where TConfig : StateMachineConfig<TState, TStateEnum>, new()
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(instanceIdSelector);

        services.AddStateMachine<TState, TStateEnum, TConfig>();
        services.AddSingleton<IEventHandler<TEvent>>(sp =>
            new StateMachineEventHandler<TState, TStateEnum, TEvent>(
                sp.GetRequiredService<IStateMachineExecutor<TState, TStateEnum>>(),
                instanceIdSelector));

        return services;
    }

    /// <summary>
    /// Register a state machine executor via CatgaServiceBuilder.
    /// </summary>
    public static CatgaServiceBuilder AddStateMachine<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
        TStateEnum,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig>(
        this CatgaServiceBuilder builder)
        where TState : class, IStateMachineState<TStateEnum>, new()
        where TStateEnum : struct, Enum
        where TConfig : StateMachineConfig<TState, TStateEnum>, new()
    {
        builder.Services.AddStateMachine<TState, TStateEnum, TConfig>();
        return builder;
    }

    /// <summary>
    /// Registers an event bridge via CatgaServiceBuilder.
    /// </summary>
    public static CatgaServiceBuilder AddStateMachineEvent<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
        TStateEnum,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        this CatgaServiceBuilder builder,
        Func<TEvent, string> instanceIdSelector)
        where TState : class, IStateMachineState<TStateEnum>, new()
        where TStateEnum : struct, Enum
        where TConfig : StateMachineConfig<TState, TStateEnum>, new()
        where TEvent : class, IEvent
    {
        builder.Services.AddStateMachineEvent<TState, TStateEnum, TConfig, TEvent>(instanceIdSelector);
        return builder;
    }

    private static void RegisterConfiguredStateMachineEvents<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
        TStateEnum,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig>(
        IServiceCollection services)
        where TState : class, IStateMachineState<TStateEnum>, new()
        where TStateEnum : struct, Enum
        where TConfig : StateMachineConfig<TState, TStateEnum>, new()
    {
        var config = new TConfig();
        foreach (var registration in config.EventCorrelationRegistrations)
            registration(services);
    }

    /// <summary>
    /// Register a state machine executor + IStateMachineEventRouter.
    /// Use configure to set up correlation ID resolvers per event type.
    /// </summary>
    public static IServiceCollection AddStateMachineWithRouter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
        TStateEnum,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig>(
        this IServiceCollection services,
        Action<StateMachineEventRouter<TState, TStateEnum, TConfig>>? configure = null)
        where TState : class, IStateMachineState<TStateEnum>, new()
        where TStateEnum : struct, Enum
        where TConfig : StateMachineConfig<TState, TStateEnum>, new()
    {
        services.AddStateMachine<TState, TStateEnum, TConfig>();
        services.TryAddSingleton<IStateMachineEventRouter<TState, TStateEnum>>(sp =>
        {
            var executor = sp.GetRequiredService<StateMachineExecutor<TState, TStateEnum, TConfig>>();
            var router = new StateMachineEventRouter<TState, TStateEnum, TConfig>(executor);
            configure?.Invoke(router);
            return router;
        });
        return services;
    }
}
