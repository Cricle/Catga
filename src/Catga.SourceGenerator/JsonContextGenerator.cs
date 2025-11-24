using Microsoft.CodeAnalysis;

namespace Catga.SourceGenerator;

[Generator]
public sealed class JsonContextGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // JSON serialization is removed; generator intentionally disabled.
    }
}
