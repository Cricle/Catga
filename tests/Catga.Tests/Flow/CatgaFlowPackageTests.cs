using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Flow.DependencyInjection;
using Catga.Flow.Persistence;
using Catga.Persistence;
using Catga.Persistence.InMemory;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Tests verifying Catga.Flow package independence and IFlowPersistenceProvider contract.
/// </summary>
public class CatgaFlowPackageTests
{
    // ── Package independence: Catga.Flow types exist independently ────────────

    [Fact]
    public void IDslFlowStore_IsInCatgaFlowNamespace()
    {
        typeof(IDslFlowStore).Namespace.Should().Be("Catga.Flow.Dsl");
    }

    [Fact]
    public void IFlowState_IsInCatgaFlowNamespace()
    {
        typeof(IFlowState).Namespace.Should().Be("Catga.Flow.Dsl");
    }

    [Fact]
    public void FlowConfig_IsInCatgaFlowNamespace()
    {
        typeof(FlowConfig<>).Namespace.Should().Be("Catga.Flow.Dsl");
    }

    [Fact]
    public void DslFlowExecutor_IsInCatgaFlowNamespace()
    {
        typeof(DslFlowExecutor<,>).Namespace.Should().Be("Catga.Flow.Dsl");
    }

    [Fact]
    public void IFlowPersistenceProvider_IsInCatgaFlowPersistenceNamespace()
    {
        typeof(IFlowPersistenceProvider).Namespace.Should().Be("Catga.Flow.Persistence");
    }

    [Fact]
    public void IFlowPersistenceProvider_ExtendsIPersistenceProvider()
    {
        typeof(IFlowPersistenceProvider).GetInterfaces()
            .Should().Contain(typeof(IPersistenceProvider));
    }

    // ── IFlowPersistenceProvider contract ─────────────────────────────────────

    [Fact]
    public void IFlowPersistenceProvider_HasCreateDslFlowStore()
    {
        typeof(IFlowPersistenceProvider).GetMethod("CreateDslFlowStore").Should().NotBeNull();
    }

    [Fact]
    public void IFlowPersistenceProvider_HasCreateFlowStore()
    {
        typeof(IFlowPersistenceProvider).GetMethod("CreateFlowStore").Should().NotBeNull();
    }

    [Fact]
    public void IFlowPersistenceProvider_Mock_CreateDslFlowStore_CanReturnNull()
    {
        var provider = Substitute.For<IFlowPersistenceProvider>();
        provider.CreateDslFlowStore().Returns((IDslFlowStore?)null);
        provider.CreateDslFlowStore().Should().BeNull();
    }

    [Fact]
    public void IFlowPersistenceProvider_Mock_CreateDslFlowStore_CanReturnStore()
    {
        var provider = Substitute.For<IFlowPersistenceProvider>();
        var store = Substitute.For<IDslFlowStore>();
        provider.CreateDslFlowStore().Returns(store);
        provider.CreateDslFlowStore().Should().BeSameAs(store);
    }

    [Fact]
    public void IFlowPersistenceProvider_Mock_CreateFlowStore_CanReturnNull()
    {
        var provider = Substitute.For<IFlowPersistenceProvider>();
        provider.CreateFlowStore().Returns(x => (IFlowStore?)null);
        provider.CreateFlowStore().Should().BeNull();
    }

    // ── PersistenceBuilderFlowExtensions ──────────────────────────────────────

    [Fact]
    public void AddDslFlowStore_WhenProviderIsNotFlowProvider_DoesNotRegister()
    {
        var services = new ServiceCollection();
        var provider = Substitute.For<IPersistenceProvider>();
        // Not IFlowPersistenceProvider, so AddDslFlowStore should be no-op
        services.AddPersistence(provider).AddDslFlowStore();
        services.Any(s => s.ServiceType == typeof(IDslFlowStore)).Should().BeFalse();
    }

    [Fact]
    public void AddDslFlowStore_WhenProviderIsFlowProvider_RegistersStore()
    {
        var services = new ServiceCollection();
        var store = Substitute.For<IDslFlowStore>();
        var provider = Substitute.For<IFlowPersistenceProvider>();
        provider.CreateDslFlowStore().Returns(store);

        services.AddPersistence(provider).AddDslFlowStore();

        var sp = services.BuildServiceProvider();
        sp.GetService<IDslFlowStore>().Should().BeSameAs(store);
    }

    [Fact]
    public void AddFlowStore_WhenProviderIsFlowProvider_RegistersStore()
    {
        var services = new ServiceCollection();
        var store = Substitute.For<IFlowStore>();
        var provider = Substitute.For<IFlowPersistenceProvider>();
        provider.CreateFlowStore().Returns(store);

        services.AddPersistence(provider).AddFlowStore();

        var sp = services.BuildServiceProvider();
        sp.GetService<IFlowStore>().Should().BeSameAs(store);
    }

    [Fact]
    public void AddAllWithFlow_RegistersAllStores()
    {
        var services = new ServiceCollection();
        var dslStore = Substitute.For<IDslFlowStore>();
        var flowStore = Substitute.For<IFlowStore>();
        var provider = Substitute.For<IFlowPersistenceProvider>();
        provider.CreateDslFlowStore().Returns(dslStore);
        provider.CreateFlowStore().Returns(flowStore);

        services.AddPersistence(provider).AddAllWithFlow();

        var sp = services.BuildServiceProvider();
        sp.GetService<IDslFlowStore>().Should().BeSameAs(dslStore);
        sp.GetService<IFlowStore>().Should().BeSameAs(flowStore);
    }

    // ── InMemoryPersistenceModule implements IFlowPersistenceProvider ─────────

    [Fact]
    public void InMemoryPersistenceModule_ImplementsIFlowPersistenceProvider()
    {
        var module = new InMemoryPersistenceModule();
        module.Should().BeAssignableTo<IFlowPersistenceProvider>();
    }

    [Fact]
    public void InMemoryPersistenceModule_CreateDslFlowStore_ReturnsStore()
    {
        var module = new InMemoryPersistenceModule();
        var store = module.CreateDslFlowStore();
        store.Should().NotBeNull();
        store.Should().BeOfType<InMemoryDslFlowStore>();
    }

    [Fact]
    public void InMemoryPersistenceModule_CreateFlowStore_ReturnsStore()
    {
        var module = new InMemoryPersistenceModule();
        var store = module.CreateFlowStore();
        store.Should().NotBeNull();
        store.Should().BeOfType<InMemoryFlowStore>();
    }

    [Fact]
    public void InMemoryPersistenceModule_Name_IsInMemory()
    {
        var module = new InMemoryPersistenceModule();
        module.Name.Should().Be("InMemory");
    }

    // ── CatgaServiceBuilderFlowExtensions ─────────────────────────────────────

    [Fact]
    public void AddFlows_RegistersFlowDslServices()
    {
        var services = new ServiceCollection();
        services.AddCatga().AddFlows();
        services.AddInMemoryDslFlowStore();

        var sp = services.BuildServiceProvider();
        sp.GetService<IDslFlowStore>().Should().NotBeNull();
    }
}
