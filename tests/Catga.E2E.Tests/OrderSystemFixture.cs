using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Catga.E2E.Tests;

/// <summary>
/// Collection fixture for OrderSystem E2E tests.
/// Ensures all test classes share the same WebApplicationFactory instance.
/// Uses shared Redis and NATS containers for better performance.
/// </summary>
[CollectionDefinition("OrderSystem")]
public class OrderSystemCollection : ICollectionFixture<OrderSystemFixture>
{
}

/// <summary>
/// Fixture that provides a shared WebApplicationFactory for all OrderSystem tests.
/// Configures the application to use shared Redis/NATS containers.
/// Each test class gets a unique key prefix for data isolation.
/// </summary>
public class OrderSystemFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SharedTestInfrastructure _infrastructure;
    private readonly string _keyPrefix;

    public OrderSystemFixture()
    {
        _infrastructure = SharedTestInfrastructure.GetInstance();
        _keyPrefix = SharedTestInfrastructure.GenerateKeyPrefix(GetType().Name);
    }

    public async Task InitializeAsync()
    {
        await _infrastructure.InitializeAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Set environment variables BEFORE the host is built
        // This ensures Program.cs can read these values during startup
        Environment.SetEnvironmentVariable("Catga__Transport", "redis");
        Environment.SetEnvironmentVariable("Catga__Persistence", "redis");
        Environment.SetEnvironmentVariable("Catga__Redis__ConnectionString", _infrastructure.RedisConnectionString);
        Environment.SetEnvironmentVariable("Catga__Redis__KeyPrefix", _keyPrefix);

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    /// <summary>
    /// Get the key prefix for this test instance (for data isolation)
    /// </summary>
    public string GetKeyPrefix() => _keyPrefix;
}
