using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Messaging;
using Catga.Resilience;
using Catga.Transport;
using Catga.Transport.Nats;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Runtime.CompilerServices;
using StackExchange.Redis;
using StackExchange.Redis.MultiplexerPool;
using Xunit;

namespace Catga.Tests.Transport;

// ── Test types ────────────────────────────────────────────────────────────────

public record OrderRequest(string OrderId) : IRequest<OrderDto>
{
    public long MessageId { get; init; }
}
public record OrderDto(string OrderId, string Status);

// ── IRequestClient interface tests ────────────────────────────────────────────

public class IRequestClientTests
{
    [Fact]
    public void IRequestClient_IsGenericInterface()
    {
        typeof(IRequestClient<,>).IsInterface.Should().BeTrue();
        typeof(IRequestClient<,>).IsGenericTypeDefinition.Should().BeTrue();
    }

    [Fact]
    public void IRequestClientFactory_IsInterface()
    {
        typeof(IRequestClientFactory).IsInterface.Should().BeTrue();
    }

    [Fact]
    public async Task IRequestClient_Mock_RequestAsync_ReturnsSuccess()
    {
        var mock = Substitute.For<IRequestClient<OrderRequest, OrderDto>>();
        mock.RequestAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<OrderDto>.Success(new OrderDto("123", "Pending")));

        var result = await mock.RequestAsync(new OrderRequest("123"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderId.Should().Be("123");
    }

    [Fact]
    public async Task IRequestClient_Mock_RequestAsync_WithTimeout_ReturnsFailure()
    {
        var mock = Substitute.For<IRequestClient<OrderRequest, OrderDto>>();
        mock.RequestAsync(Arg.Any<OrderRequest>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<OrderDto>.Failure(ErrorInfo.Timeout("timed out")));

        var result = await mock.RequestAsync(new OrderRequest("x"), TimeSpan.FromSeconds(1));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Timeout);
    }
}

// ── RequestClientFactory UT ───────────────────────────────────────────────────

public class RequestClientFactoryTests
{
    private static IMessageTransport CreateMockTransport()
    {
        var transport = Substitute.For<IMessageTransport>();
        transport.Name.Returns("Mock");
        transport.BatchOptions.Returns((BatchTransportOptions?)null);
        transport.CompressionOptions.Returns((CompressionTransportOptions?)null);
        return transport;
    }

    [Fact]
    public void CreateClient_ReturnsNonNullClient()
    {
        var factory = new RequestClientFactory(CreateMockTransport());
        var client = factory.CreateClient<OrderRequest, OrderDto>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateClient_WithCustomDestination_CreatesClient()
    {
        var factory = new RequestClientFactory(CreateMockTransport());
        var client = factory.CreateClient<OrderRequest, OrderDto>("orders.get");
        client.Should().NotBeNull();
    }

    [Fact]
    public void CreateClient_WithCustomTimeout_CreatesClient()
    {
        var factory = new RequestClientFactory(CreateMockTransport(), TimeSpan.FromSeconds(10));
        var client = factory.CreateClient<OrderRequest, OrderDto>(defaultTimeout: TimeSpan.FromSeconds(5));
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateClient_WithoutExplicitTimeout_UsesTransportDefaultRequestTimeout()
    {
        var transport = Substitute.For<IMessageTransport, IRequestTimeoutDefaults>();
        transport.Name.Returns("Mock");
        transport.BatchOptions.Returns((BatchTransportOptions?)null);
        transport.CompressionOptions.Returns((CompressionTransportOptions?)null);
        ((IRequestTimeoutDefaults)transport).DefaultRequestTimeout.Returns(TimeSpan.FromSeconds(9));
        transport.RequestAsync<OrderRequest, OrderDto>(
            Arg.Any<OrderRequest>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((OrderDto?)null);

        var factory = new RequestClientFactory(transport);
        var client = factory.CreateClient<OrderRequest, OrderDto>();

        await client.RequestAsync(new OrderRequest("123"));

        await transport.Received(1).RequestAsync<OrderRequest, OrderDto>(
            Arg.Any<OrderRequest>(),
            Arg.Any<string>(),
            TimeSpan.FromSeconds(9),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NatsTransport_ShouldExposeConfiguredDefaultRequestTimeout()
    {
        var transport = (NatsMessageTransport)RuntimeHelpers.GetUninitializedObject(typeof(NatsMessageTransport));
        SetFirstFieldOfType(transport, typeof(NatsTransportOptions), new NatsTransportOptions { RequestTimeout = TimeSpan.FromSeconds(12) });

        (transport as IRequestTimeoutDefaults).Should().NotBeNull();
        ((IRequestTimeoutDefaults)transport!).DefaultRequestTimeout.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void RedisTransport_ShouldExposeConfiguredDefaultRequestTimeout()
    {
        var transport = new RedisMessageTransport(
            Substitute.For<IConnectionMultiplexerPool>(),
            Substitute.For<IMessageSerializer>(),
            Substitute.For<IResiliencePipelineProvider>(),
            new RedisTransportOptions { RequestTimeout = TimeSpan.FromSeconds(15) });

        (transport as IRequestTimeoutDefaults).Should().NotBeNull();
        ((IRequestTimeoutDefaults)transport!).DefaultRequestTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task NatsTransport_RequestAsync_WhenNotAcceptingMessages_ShouldThrow()
    {
        var transport = (NatsMessageTransport)RuntimeHelpers.GetUninitializedObject(typeof(NatsMessageTransport));

        var act = () => transport.RequestAsync<OrderRequest, OrderDto>(
            new OrderRequest("123"),
            "orders.get",
            TimeSpan.FromSeconds(1));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Transport is not accepting new messages");
    }

    [Fact]
    public async Task RedisTransport_RequestAsync_WhenNotAcceptingMessages_ShouldThrow()
    {
        var transport = (RedisMessageTransport)RuntimeHelpers.GetUninitializedObject(typeof(RedisMessageTransport));

        var act = () => transport.RequestAsync<OrderRequest, OrderDto>(
            new OrderRequest("123"),
            "orders.get",
            TimeSpan.FromSeconds(1));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Transport is not accepting new messages");
    }

    [Fact]
    public async Task RequestAsync_WhenTransportReturnsNull_ReturnsTimeoutFailure()
    {
        var transport = CreateMockTransport();
        transport.RequestAsync<OrderRequest, OrderDto>(
            Arg.Any<OrderRequest>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((OrderDto?)null);

        var factory = new RequestClientFactory(transport);
        var client = factory.CreateClient<OrderRequest, OrderDto>();

        var result = await client.RequestAsync(new OrderRequest("123"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Timeout);
    }

    [Fact]
    public async Task RequestAsync_WhenTransportReturnsResponse_ReturnsSuccess()
    {
        var transport = CreateMockTransport();
        transport.RequestAsync<OrderRequest, OrderDto>(
            Arg.Any<OrderRequest>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new OrderDto("123", "Shipped"));

        var factory = new RequestClientFactory(transport);
        var client = factory.CreateClient<OrderRequest, OrderDto>();

        var result = await client.RequestAsync(new OrderRequest("123"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Shipped");
    }

    [Fact]
    public async Task RequestAsync_WhenTransportThrows_ReturnsTransportFailure()
    {
        var transport = CreateMockTransport();
        transport.RequestAsync<OrderRequest, OrderDto>(
            Arg.Any<OrderRequest>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<OrderDto?>(_ => throw new InvalidOperationException("connection lost"));

        var factory = new RequestClientFactory(transport);
        var client = factory.CreateClient<OrderRequest, OrderDto>();

        var result = await client.RequestAsync(new OrderRequest("123"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TransportFailed);
        result.Error.Should().Contain("connection lost");
    }

    [Fact]
    public async Task RequestAsync_WhenCancelled_ReturnsCancelledFailure()
    {
        var transport = CreateMockTransport();
        transport.RequestAsync<OrderRequest, OrderDto>(
            Arg.Any<OrderRequest>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<OrderDto?>(_ => throw new OperationCanceledException());

        var factory = new RequestClientFactory(transport);
        var client = factory.CreateClient<OrderRequest, OrderDto>();

        var result = await client.RequestAsync(new OrderRequest("123"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Cancelled);
    }

    private static void SetFirstFieldOfType(object target, Type fieldType, object value)
    {
        var field = target
            .GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .FirstOrDefault(candidate => fieldType.IsAssignableFrom(candidate.FieldType));

        field.Should().NotBeNull($"a field assignable to {fieldType.Name} should exist on {target.GetType().Name}");
        field!.SetValue(target, value);
    }
}

// ── DI registration tests ─────────────────────────────────────────────────────

public class RequestClientDiTests
{
    [Fact]
    public void UseRequestClient_RegistersIRequestClientFactory()
    {
        var services = new ServiceCollection();
        services.AddCatga().UseInMemory().UseRequestClient();
        services.AddInMemoryTransport();

        var sp = services.BuildServiceProvider();
        var factory = sp.GetService<IRequestClientFactory>();
        factory.Should().NotBeNull();
    }

    [Fact]
    public void UseRequestClient_WithTimeout_RegistersFactory()
    {
        var services = new ServiceCollection();
        services.AddCatga().UseInMemory().UseRequestClient(TimeSpan.FromSeconds(60));
        services.AddInMemoryTransport();

        var sp = services.BuildServiceProvider();
        sp.GetService<IRequestClientFactory>().Should().NotBeNull();
    }

    [Fact]
    public void UseRequestClient_CanCreateClient()
    {
        var services = new ServiceCollection();
        services.AddCatga().UseInMemory().UseRequestClient();
        services.AddInMemoryTransport();

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IRequestClientFactory>();
        var client = factory.CreateClient<OrderRequest, OrderDto>();
        client.Should().NotBeNull();
    }

    [Fact]
    public async Task AddRedisTransport_WithExistingConnection_ShouldInheritGlobalEndpointNamingConvention()
    {
        var services = new ServiceCollection();
        services.AddCatga(options => options.EndpointNamingConvention = type => $"shop.{type.Name.ToLowerInvariant()}");
        services.AddSingleton(Substitute.For<IMessageSerializer>());
        services.AddSingleton(Substitute.For<IResiliencePipelineProvider>());
        services.AddRedisTransport(Substitute.For<IConnectionMultiplexer>());

        await using var serviceProvider = services.BuildServiceProvider();
        var transport = serviceProvider.GetRequiredService<IMessageTransport>().Should().BeOfType<RedisMessageTransport>().Subject;
        var namingField = typeof(MessageTransportBase).GetField("Naming", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        namingField.Should().NotBeNull();
        var naming = namingField!.GetValue(transport).Should().BeAssignableTo<Func<Type, string>>().Subject;
        naming(typeof(OrderRequest)).Should().Be("shop.orderrequest");
    }

    [Fact]
    public async Task AddRedisTransport_WithExistingConnectionAndOptions_ShouldPreserveConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.AddCatga(options => options.EndpointNamingConvention = type => $"shop.{type.Name.ToLowerInvariant()}");
        services.AddSingleton(Substitute.For<IMessageSerializer>());
        services.AddSingleton(Substitute.For<IResiliencePipelineProvider>());
        services.AddRedisTransport(
            Substitute.For<IConnectionMultiplexer>(),
            new RedisTransportOptions
            {
                ChannelPrefix = "redis-prefix",
                RequestTimeout = TimeSpan.FromSeconds(17)
            });

        await using var serviceProvider = services.BuildServiceProvider();
        var transport = serviceProvider.GetRequiredService<IMessageTransport>().Should().BeOfType<RedisMessageTransport>().Subject;
        var namingField = typeof(MessageTransportBase).GetField("Naming", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var prefixField = typeof(MessageTransportBase).GetField("Prefix", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        namingField.Should().NotBeNull();
        prefixField.Should().NotBeNull();

        var naming = namingField!.GetValue(transport).Should().BeAssignableTo<Func<Type, string>>().Subject;
        naming(typeof(OrderRequest)).Should().Be("shop.orderrequest");
        prefixField!.GetValue(transport).Should().Be("redis-prefix.");
        ((IRequestTimeoutDefaults)transport).DefaultRequestTimeout.Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public async Task AddRedisTransport_WithExistingConnectionAndOptions_ShouldUseConfiguredConsumerIdentity()
    {
        var services = new ServiceCollection();
        services.AddCatga();
        services.AddSingleton(Substitute.For<IMessageSerializer>());
        services.AddSingleton(Substitute.For<IResiliencePipelineProvider>());
        services.AddRedisTransport(
            Substitute.For<IConnectionMultiplexer>(),
            new RedisTransportOptions
            {
                ConsumerGroup = "orders-group",
                ConsumerName = "orders-consumer"
            });

        await using var serviceProvider = services.BuildServiceProvider();
        var transport = serviceProvider.GetRequiredService<IMessageTransport>().Should().BeOfType<RedisMessageTransport>().Subject;
        var groupField = typeof(RedisMessageTransport).GetField("_group", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var consumerField = typeof(RedisMessageTransport).GetField("_consumer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        groupField.Should().NotBeNull();
        consumerField.Should().NotBeNull();
        groupField!.GetValue(transport).Should().Be("orders-group");
        consumerField!.GetValue(transport).Should().Be("orders-consumer");
    }
}

// ── E2E: InMemory Request/Response ────────────────────────────────────────────

public class RequestClientE2ETests
{
    private static ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddCatga().UseInMemory().UseRequestClient();
        services.AddInMemoryTransport();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InMemoryTransport_RequestAsync_NotSupported_ReturnsError()
    {
        // InMemory transport's RequestAsync is overridden but requires a handler to publish response
        // Without a registered handler, it times out
        await using var sp = BuildServiceProvider();
        var factory = sp.GetRequiredService<IRequestClientFactory>();
        var client = factory.CreateClient<OrderRequest, OrderDto>(
            defaultTimeout: TimeSpan.FromMilliseconds(100));

        var result = await client.RequestAsync(new OrderRequest("123"));

        // Without a response handler, should timeout
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void IMessageTransport_DefaultRequestAsync_IsInterceptedBySubstitute()
    {
        // The default interface implementation throws NotSupportedException
        // NSubstitute intercepts calls so we just verify the method exists on the interface
        typeof(IMessageTransport).GetMethod("RequestAsync").Should().NotBeNull();
    }
}
