using BenchmarkDotNet.Attributes;
using Catga.Core;
using Catga.Exceptions;
using Catga.Messages;
using Catga.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catga.Benchmarks;

/// <summary>
/// SafeRequestHandler performance benchmarks - measure overhead vs direct implementation
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class SafeRequestHandlerBenchmarks
{
    private TestSafeHandler _safeHandler = null!;
    private TestDirectHandler _directHandler = null!;
    private TestRequest _request = null!;

    [GlobalSetup]
    public void Setup()
    {
        _safeHandler = new TestSafeHandler(NullLogger.Instance);
        _directHandler = new TestDirectHandler();
        _request = new TestRequest();
    }

    [Benchmark(Baseline = true)]
    public async Task DirectHandler_Success()
    {
        var result = await _directHandler.HandleAsync(_request);
    }

    [Benchmark]
    public async Task SafeHandler_Success()
    {
        var result = await _safeHandler.HandleAsync(_request);
    }

    [Benchmark]
    public async Task DirectHandler_WithError()
    {
        var result = await _directHandler.HandleAsync(new TestRequest { ShouldFail = true });
    }

    [Benchmark]
    public async Task SafeHandler_WithError()
    {
        var result = await _safeHandler.HandleAsync(new TestRequest { ShouldFail = true });
    }

    // Test types
    public record TestRequest : IRequest<TestResponse>
    {
        public string MessageId { get; init; } = MessageExtensions.NewMessageId();
        public bool ShouldFail { get; init; }
    }

    public record TestResponse(string Value);

    // SafeRequestHandler implementation
    public class TestSafeHandler : SafeRequestHandler<TestRequest, TestResponse>
    {
        public TestSafeHandler(ILogger logger) : base(logger) { }

        protected override async Task<TestResponse> HandleCoreAsync(TestRequest request, CancellationToken cancellationToken)
        {
            await Task.Yield();

            if (request.ShouldFail)
                throw new CatgaException("Test error");

            return new TestResponse("Success");
        }
    }

    // Direct implementation for comparison
    public class TestDirectHandler
    {
        public async Task<CatgaResult<TestResponse>> HandleAsync(TestRequest request)
        {
            try
            {
                await Task.Yield();

                if (request.ShouldFail)
                    return CatgaResult<TestResponse>.Failure("Test error", new CatgaException("Test error"));

                return CatgaResult<TestResponse>.Success(new TestResponse("Success"));
            }
            catch (Exception ex)
            {
                return CatgaResult<TestResponse>.Failure("Error", new CatgaException("Error", ex));
            }
        }
    }
}

