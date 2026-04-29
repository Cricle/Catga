using System.Collections.Immutable;
using Catga.SourceGenerator;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Catga.Tests.DependencyInjection;

public class UnifiedRegistrationGeneratorDiagnosticsTests
{
    [Fact]
    public void ReportsDiagnostic_ForCatgaLifetimeSingletonHandlerDependingOnMediator()
    {
        const string source = """
            using Catga;
            using Catga.Abstractions;

            namespace Demo;

            [CatgaLifetime(ServiceLifetime.Singleton)]
            public sealed class BadAttributedHandler : IRequestHandler<Ping, string>
            {
                public BadAttributedHandler(ICatgaMediator mediator) { }

                public ValueTask<Catga.Core.CatgaResult<string>> HandleAsync(Ping request, CancellationToken cancellationToken = default)
                    => new(Catga.Core.CatgaResult<string>.Success("ok"));
            }

            public record Ping(long MessageId) : IRequest<string>;
            """;

        var diagnostics = GetGeneratorDiagnostics(source);

        diagnostics.Should().ContainSingle(d =>
            d.Id == "CAT2004" &&
            d.GetMessage().Contains("BadAttributedHandler", StringComparison.Ordinal) &&
            d.GetMessage().Contains("ICatgaMediator", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsDiagnostic_ForCatgaServiceSingletonDependingOnMediator()
    {
        const string source = """
            using Catga;

            namespace Demo;

            public interface IWorker { }

            [CatgaService(Lifetime = ServiceLifetime.Singleton, ServiceType = typeof(IWorker))]
            public sealed class BadService : IWorker
            {
                public BadService(ICatgaMediator mediator) { }
            }
            """;

        var diagnostics = GetGeneratorDiagnostics(source);

        diagnostics.Should().ContainSingle(d =>
            d.Id == "CAT2004" &&
            d.GetMessage().Contains("BadService", StringComparison.Ordinal) &&
            d.GetMessage().Contains("ICatgaMediator", StringComparison.Ordinal));
    }

    [Fact]
    public void DoesNotReportDiagnostic_ForScopedCatgaLifetimeHandler()
    {
        const string source = """
            using Catga;
            using Catga.Abstractions;

            namespace Demo;

            [CatgaLifetime(ServiceLifetime.Scoped)]
            public sealed class GoodHandler : IRequestHandler<Ping, string>
            {
                public GoodHandler(ICatgaMediator mediator) { }

                public ValueTask<Catga.Core.CatgaResult<string>> HandleAsync(Ping request, CancellationToken cancellationToken = default)
                    => new(Catga.Core.CatgaResult<string>.Success("ok"));
            }

            public record Ping(long MessageId) : IRequest<string>;
            """;

        var diagnostics = GetGeneratorDiagnostics(source);

        diagnostics.Should().NotContain(d => d.Id == "CAT2004");
    }

    private static ImmutableArray<Diagnostic> GetGeneratorDiagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorDiagnosticsTests",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new UnifiedRegistrationGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult().Diagnostics;
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
                     typeof(Catga.Abstractions.IRequest<>).Assembly,
                     typeof(UnifiedRegistrationGenerator).Assembly
                 })
        {
            if (seen.Add(assembly.Location))
                yield return MetadataReference.CreateFromFile(assembly.Location);
        }
    }
}
