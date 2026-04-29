using System.Collections.Immutable;
using System.Reflection;
using Catga.SourceGenerator.Analyzers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.DependencyInjection;

public class ScopedLifetimeMismatchAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_ForSingletonHandlerDependingOnMediator()
    {
        const string source = """
            using Catga;
            using Catga.Abstractions;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public record Ping(long MessageId) : IRequest<string>;

            public sealed class BadHandler : IRequestHandler<Ping, string>
            {
                public BadHandler(ICatgaMediator mediator) { }

                public ValueTask<Catga.Core.CatgaResult<string>> HandleAsync(Ping request, CancellationToken cancellationToken = default)
                    => new(Catga.Core.CatgaResult<string>.Success("ok"));
            }

            public static class Registration
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IRequestHandler<Ping, string>, BadHandler>();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(d =>
            d.Id == "CAT2004" &&
            d.GetMessage().Contains("BadHandler", StringComparison.Ordinal) &&
            d.GetMessage().Contains("ICatgaMediator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsDiagnostic_ForSingletonFlowExecutorDependingOnMediator()
    {
        const string source = """
            using Catga;
            using Catga.Abstractions;
            using Catga.Flow.Dsl;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public sealed class BadFlowExecutor : IFlowExecutor
            {
                public BadFlowExecutor(ICatgaMediator mediator) { }

                public Task<DslFlowResult<TState>> ExecuteAsync<TFlow, TState>(TState initialState, CancellationToken cancellationToken = default)
                    where TFlow : FlowConfig<TState>, new()
                    where TState : class, IFlowState, new()
                    => throw new NotSupportedException();

                public Task<DslFlowResult<TState>> ResumeAsync<TFlow, TState>(string flowId, CancellationToken cancellationToken = default)
                    where TFlow : FlowConfig<TState>, new()
                    where TState : class, IFlowState, new()
                    => throw new NotSupportedException();

                public Task<FlowSnapshot<TState>?> GetSnapshotAsync<TState>(string flowId, CancellationToken cancellationToken = default)
                    where TState : class, IFlowState, new()
                    => throw new NotSupportedException();

                public Task<bool> CancelAsync(string flowId, CancellationToken cancellationToken = default)
                    => throw new NotSupportedException();
            }

            public static class Registration
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IFlowExecutor, BadFlowExecutor>();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(d =>
            d.Id == "CAT2004" &&
            d.GetMessage().Contains("BadFlowExecutor", StringComparison.Ordinal) &&
            d.GetMessage().Contains("ICatgaMediator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotReportDiagnostic_ForScopedHandlerDependingOnMediator()
    {
        const string source = """
            using Catga.Abstractions;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo;

            public record Ping(long MessageId) : IRequest<string>;

            public sealed class GoodHandler : IRequestHandler<Ping, string>
            {
                public GoodHandler(ICatgaMediator mediator) { }

                public ValueTask<Catga.Core.CatgaResult<string>> HandleAsync(Ping request, CancellationToken cancellationToken = default)
                    => new(Catga.Core.CatgaResult<string>.Success("ok"));
            }

            public static class Registration
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IRequestHandler<Ping, string>, GoodHandler>();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().NotContain(d => d.Id == "CAT2004");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new ScopedLifetimeMismatchAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!.Split(Path.PathSeparator))
        {
            if (seen.Add(path))
                yield return MetadataReference.CreateFromFile(path);
        }

        foreach (var assembly in new[]
                 {
                     typeof(ICatgaMediator).Assembly,
                     typeof(Catga.Flow.Dsl.IFlowExecutor).Assembly,
                     typeof(ServiceCollection).Assembly,
                     typeof(Enumerable).Assembly
                 })
        {
            if (seen.Add(assembly.Location))
                yield return MetadataReference.CreateFromFile(assembly.Location);
        }
    }
}
