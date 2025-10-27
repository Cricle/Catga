using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Core;

public class HandlerCacheTests
{
    private readonly HandlerCache _cache = new();

    // ==================== GetRequestHandler Tests ====================

    [Fact]
    public void GetRequestHandler_WithRegisteredHandler_ShouldReturnInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestRequestHandler>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var handler = _cache.GetRequestHandler<TestRequestHandler>(scope.ServiceProvider);

        // Assert
        handler.Should().NotBeNull();
        handler.Should().BeOfType<TestRequestHandler>();
    }

    [Fact]
    public void GetRequestHandler_WithUnregisteredHandler_ShouldThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var act = () => _cache.GetRequestHandler<TestRequestHandler>(scope.ServiceProvider);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestRequestHandler*");
    }

    [Fact]
    public void GetRequestHandler_WithScopedHandler_ShouldReturnNewInstancePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestRequestHandler>();
        var provider = services.BuildServiceProvider();

        // Act
        TestRequestHandler handler1, handler2, handler3;
        using (var scope1 = provider.CreateScope())
        {
            handler1 = _cache.GetRequestHandler<TestRequestHandler>(scope1.ServiceProvider);
            handler2 = _cache.GetRequestHandler<TestRequestHandler>(scope1.ServiceProvider);
        }
        using (var scope2 = provider.CreateScope())
        {
            handler3 = _cache.GetRequestHandler<TestRequestHandler>(scope2.ServiceProvider);
        }

        // Assert
        handler1.Should().BeSameAs(handler2, "same scope should return same instance");
        handler1.Should().NotBeSameAs(handler3, "different scope should return different instance");
    }

    [Fact]
    public void GetRequestHandler_WithSingletonHandler_ShouldReturnSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TestRequestHandler>();
        var provider = services.BuildServiceProvider();

        // Act
        TestRequestHandler handler1, handler2;
        using (var scope1 = provider.CreateScope())
        {
            handler1 = _cache.GetRequestHandler<TestRequestHandler>(scope1.ServiceProvider);
        }
        using (var scope2 = provider.CreateScope())
        {
            handler2 = _cache.GetRequestHandler<TestRequestHandler>(scope2.ServiceProvider);
        }

        // Assert
        handler1.Should().BeSameAs(handler2, "singleton should be shared across scopes");
    }

    [Fact]
    public void GetRequestHandler_WithTransientHandler_ShouldReturnNewInstanceEveryTime()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TestRequestHandler>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var handler1 = _cache.GetRequestHandler<TestRequestHandler>(scope.ServiceProvider);
        var handler2 = _cache.GetRequestHandler<TestRequestHandler>(scope.ServiceProvider);

        // Assert
        handler1.Should().NotBeSameAs(handler2, "transient should return new instance every time");
    }

    [Fact]
    public void GetRequestHandler_ConcurrentCalls_ShouldHandleCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestRequestHandler>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            _cache.GetRequestHandler<TestRequestHandler>(scope.ServiceProvider)
        )).ToArray();

        var handlers = Task.WhenAll(tasks).GetAwaiter().GetResult();

        // Assert
        handlers.Should().AllBeOfType<TestRequestHandler>();
        handlers.Should().OnlyContain(h => h == handlers[0], "scoped handlers should be same instance");
    }

    // ==================== GetEventHandlers Tests ====================

    [Fact]
    public void GetEventHandlers_WithNoHandlers_ShouldReturnEmptyList()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var handlers = _cache.GetEventHandlers<TestEventHandler>(scope.ServiceProvider);

        // Assert
        handlers.Should().NotBeNull();
        handlers.Should().BeEmpty();
    }

    [Fact]
    public void GetEventHandlers_WithSingleHandler_ShouldReturnList()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestEventHandler>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var handlers = _cache.GetEventHandlers<TestEventHandler>(scope.ServiceProvider);

        // Assert
        handlers.Should().NotBeNull();
        handlers.Should().HaveCount(1);
        handlers[0].Should().BeOfType<TestEventHandler>();
    }

    [Fact]
    public void GetEventHandlers_WithMultipleHandlers_ShouldReturnAllInOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<ITestEventHandler, TestEventHandler1>();
        services.AddScoped<ITestEventHandler, TestEventHandler2>();
        services.AddScoped<ITestEventHandler, TestEventHandler3>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var handlers = _cache.GetEventHandlers<ITestEventHandler>(scope.ServiceProvider);

        // Assert
        handlers.Should().NotBeNull();
        handlers.Should().HaveCount(3);
        handlers[0].Should().BeOfType<TestEventHandler1>();
        handlers[1].Should().BeOfType<TestEventHandler2>();
        handlers[2].Should().BeOfType<TestEventHandler3>();
    }

    [Fact]
    public void GetEventHandlers_ShouldReturnReadOnlyList()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestEventHandler>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var handlers = _cache.GetEventHandlers<TestEventHandler>(scope.ServiceProvider);

        // Assert
        handlers.Should().BeAssignableTo<IReadOnlyList<TestEventHandler>>();
    }

    [Fact]
    public void GetEventHandlers_WithScopedHandlers_ShouldReturnSameInstancesInScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestEventHandler>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var handlers1 = _cache.GetEventHandlers<TestEventHandler>(scope.ServiceProvider);
        var handlers2 = _cache.GetEventHandlers<TestEventHandler>(scope.ServiceProvider);

        // Assert
        handlers1[0].Should().BeSameAs(handlers2[0], "scoped handlers should be reused in same scope");
    }

    [Fact]
    public void GetEventHandlers_AcrossScopes_ShouldReturnDifferentInstances()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TestEventHandler>();
        var provider = services.BuildServiceProvider();

        // Act
        TestEventHandler handler1, handler2;
        using (var scope1 = provider.CreateScope())
        {
            var handlers1 = _cache.GetEventHandlers<TestEventHandler>(scope1.ServiceProvider);
            handler1 = handlers1[0];
        }
        using (var scope2 = provider.CreateScope())
        {
            var handlers2 = _cache.GetEventHandlers<TestEventHandler>(scope2.ServiceProvider);
            handler2 = handlers2[0];
        }

        // Assert
        handler1.Should().NotBeSameAs(handler2, "different scopes should have different instances");
    }

    [Fact]
    public void GetEventHandlers_ConcurrentCalls_ShouldHandleCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<ITestEventHandler, TestEventHandler1>();
        services.AddScoped<ITestEventHandler, TestEventHandler2>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            _cache.GetEventHandlers<ITestEventHandler>(scope.ServiceProvider)
        )).ToArray();

        var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

        // Assert
        results.Should().AllSatisfy(handlers =>
        {
            handlers.Should().HaveCount(2);
            handlers[0].Should().BeOfType<TestEventHandler1>();
            handlers[1].Should().BeOfType<TestEventHandler2>();
        });
    }

    [Fact]
    public void GetEventHandlers_WithMixedLifetimes_ShouldRespectDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestEventHandler, TestEventHandler1>(); // Singleton
        services.AddScoped<ITestEventHandler, TestEventHandler2>(); // Scoped
        var provider = services.BuildServiceProvider();

        // Act
        ITestEventHandler singleton1, singleton2, scoped1, scoped2;
        using (var scope1 = provider.CreateScope())
        {
            var handlers1 = _cache.GetEventHandlers<ITestEventHandler>(scope1.ServiceProvider);
            singleton1 = handlers1[0];
            scoped1 = handlers1[1];
        }
        using (var scope2 = provider.CreateScope())
        {
            var handlers2 = _cache.GetEventHandlers<ITestEventHandler>(scope2.ServiceProvider);
            singleton2 = handlers2[0];
            scoped2 = handlers2[1];
        }

        // Assert
        singleton1.Should().BeSameAs(singleton2, "singleton should be shared");
        scoped1.Should().NotBeSameAs(scoped2, "scoped should be per-scope");
    }

    // ==================== Test Helpers ====================

    public class TestRequestHandler : IRequestHandler<TestRequest, TestResponse>
    {
        public Task<CatgaResult<TestResponse>> HandleAsync(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));
    }

    public record TestRequest : IRequest<TestResponse>
    {
        public long MessageId { get; init; }
    }

    public record TestResponse;

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public record TestEvent : IEvent
    {
        public long MessageId { get; init; }
    }

    public interface ITestEventHandler : IEventHandler<TestEvent> { }

    public class TestEventHandler1 : ITestEventHandler
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class TestEventHandler2 : ITestEventHandler
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public class TestEventHandler3 : ITestEventHandler
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

