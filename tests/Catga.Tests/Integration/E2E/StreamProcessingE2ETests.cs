using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// E2E tests for stream processing scenarios
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class StreamProcessingE2ETests
{
    [Fact]
    public async Task SendStreamAsync_SingleItem_ShouldProcess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<StreamCommand, StreamResponse>, StreamHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = GetStreamItems(1);
        var results = new List<CatgaResult<StreamResponse>>();
        await foreach (var result in mediator.SendStreamAsync<StreamCommand, StreamResponse>(requests))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendStreamAsync_MultipleItems_ShouldProcessAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<StreamCommand, StreamResponse>, StreamHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = GetStreamItems(10);
        var results = new List<CatgaResult<StreamResponse>>();
        await foreach (var result in mediator.SendStreamAsync<StreamCommand, StreamResponse>(requests))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(10);
        results.All(r => r.IsSuccess).Should().BeTrue();
    }

    [Fact]
    public async Task SendStreamAsync_WithCancellation_ShouldStop()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<SlowStreamCommand, SlowStreamResponse>, SlowStreamHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource(100);

        // Act
        var requests = GetSlowStreamItems(100);
        var results = new List<CatgaResult<SlowStreamResponse>>();

        try
        {
            await foreach (var result in mediator.SendStreamAsync<SlowStreamCommand, SlowStreamResponse>(requests, cts.Token))
            {
                results.Add(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Should have processed some but not all
        results.Count.Should().BeLessThan(100);
    }

    [Fact]
    public async Task SendStreamAsync_EmptyStream_ShouldReturnEmpty()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<StreamCommand, StreamResponse>, StreamHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = GetStreamItems(0);
        var results = new List<CatgaResult<StreamResponse>>();
        await foreach (var result in mediator.SendStreamAsync<StreamCommand, StreamResponse>(requests))
        {
            results.Add(result);
        }

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SendStreamAsync_LargeStream_ShouldProcessEfficiently()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<StreamCommand, StreamResponse>, StreamHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = GetStreamItems(1000);
        var count = 0;
        await foreach (var result in mediator.SendStreamAsync<StreamCommand, StreamResponse>(requests))
        {
            if (result.IsSuccess) count++;
        }

        // Assert
        count.Should().Be(1000);
    }

    private static async IAsyncEnumerable<StreamCommand> GetStreamItems(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new StreamCommand { MessageId = MessageExtensions.NewMessageId(), Value = i };
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<SlowStreamCommand> GetSlowStreamItems(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new SlowStreamCommand { MessageId = MessageExtensions.NewMessageId(), Value = i };
            await Task.Yield();
        }
    }

    #region Test Types

    [MemoryPackable]
    private partial record StreamCommand : IRequest<StreamResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record StreamResponse
    {
        public int ProcessedValue { get; init; }
    }

    private sealed class StreamHandler : IRequestHandler<StreamCommand, StreamResponse>
    {
        public Task<CatgaResult<StreamResponse>> HandleAsync(StreamCommand request, CancellationToken ct = default)
        {
            return Task.FromResult(CatgaResult<StreamResponse>.Success(new StreamResponse { ProcessedValue = request.Value * 2 }));
        }
    }

    private sealed class SlowStreamHandler : IRequestHandler<SlowStreamCommand, SlowStreamResponse>
    {
        public async Task<CatgaResult<SlowStreamResponse>> HandleAsync(SlowStreamCommand request, CancellationToken ct = default)
        {
            await Task.Delay(20, ct);
            return CatgaResult<SlowStreamResponse>.Success(new SlowStreamResponse { ProcessedValue = request.Value * 2 });
        }
    }

    [MemoryPackable]
    private partial record SlowStreamCommand : IRequest<SlowStreamResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record SlowStreamResponse
    {
        public int ProcessedValue { get; init; }
    }

    #endregion
}
