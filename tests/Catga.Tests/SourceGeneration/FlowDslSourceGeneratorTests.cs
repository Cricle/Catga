using System.Linq;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using FluentAssertions;
using Catga.SourceGenerator;
using Catga.Flow.Dsl;

namespace Catga.Tests.SourceGeneration;

/// <summary>
/// Unit tests for Flow DSL source generator.
/// Verifies that the generator correctly discovers and generates registration code.
/// </summary>
public class FlowDslSourceGeneratorTests
{
    [Fact]
    public async Task Generator_DiscoversSimpleFlowConfig()
    {
        // Arrange
        var source = @"
using Catga.Flow.Dsl;
using System.Collections.Generic;

namespace TestApp
{
    public class SimpleFlow : FlowConfig<SimpleState>
    {
        protected override void Configure(IFlowBuilder<SimpleState> flow)
        {
            flow.Name(""simple-flow"");
        }
    }

    public class SimpleState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }
}";

        // Act
        var (compilation, diagnostics) = await CompileWithGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();

        var generatedFile = compilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("CatgaGeneratedFlowRegistrations"));

        generatedFile.Should().NotBeNull();

        var content = generatedFile!.ToString();
        content.Should().Contain("AddSimpleFlow");
        content.Should().Contain("FlowConfig<TestApp.SimpleState>");
        content.Should().Contain("TestApp.SimpleFlow");
    }

    [Fact]
    public async Task Generator_HandlesMultipleFlowConfigs()
    {
        // Arrange
        var source = @"
using Catga.Flow.Dsl;
using System.Collections.Generic;

namespace TestApp
{
    public class FirstFlow : FlowConfig<FirstState>
    {
        protected override void Configure(IFlowBuilder<FirstState> flow) { }
    }

    public class SecondFlow : FlowConfig<SecondState>
    {
        protected override void Configure(IFlowBuilder<SecondState> flow) { }
    }

    public class ThirdFlow : FlowConfig<ThirdState>
    {
        protected override void Configure(IFlowBuilder<ThirdState> flow) { }
    }

    public class FirstState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public class SecondState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public class ThirdState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }
}";

        // Act
        var (compilation, diagnostics) = await CompileWithGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();

        var generatedFile = compilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("CatgaGeneratedFlowRegistrations"));

        var content = generatedFile!.ToString();

        // Should generate individual registration methods
        content.Should().Contain("AddFirstFlow");
        content.Should().Contain("AddSecondFlow");
        content.Should().Contain("AddThirdFlow");

        // Should generate metadata
        content.Should().Contain("GetRegisteredFlows");
        content.Should().Contain("Found 3 flow configuration(s)");
    }

    [Fact]
    public async Task Generator_IgnoresAbstractFlowConfigs()
    {
        // Arrange
        var source = @"
using Catga.Flow.Dsl;
using System.Collections.Generic;

namespace TestApp
{
    public abstract class BaseFlow : FlowConfig<BaseState>
    {
        // Abstract flow should be ignored
    }

    public class ConcreteFlow : BaseFlow
    {
        protected override void Configure(IFlowBuilder<BaseState> flow) { }
    }

    public class BaseState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }
}";

        // Act
        var (compilation, diagnostics) = await CompileWithGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();

        var generatedFile = compilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("CatgaGeneratedFlowRegistrations"));

        var content = generatedFile!.ToString();

        // Should only register concrete flow
        content.Should().Contain("AddConcreteFlow");
        content.Should().NotContain("AddBaseFlow");
        content.Should().Contain("Found 1 flow configuration(s)");
    }

    [Fact]
    public async Task Generator_HandlesGenericStateTypes()
    {
        // Arrange
        var source = @"
using Catga.Flow.Dsl;
using System.Collections.Generic;

namespace TestApp
{
    public class GenericFlow : FlowConfig<GenericState<string>>
    {
        protected override void Configure(IFlowBuilder<GenericState<string>> flow) { }
    }

    public class GenericState<T> : IFlowState
    {
        public string? FlowId { get; set; }
        public T? Data { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }
}";

        // Act
        var (compilation, diagnostics) = await CompileWithGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();

        var generatedFile = compilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("CatgaGeneratedFlowRegistrations"));

        var content = generatedFile!.ToString();

        // Should handle generic types correctly
        content.Should().Contain("AddGenericFlow");
        content.Should().Contain("GenericState<string>");
        content.Should().Contain("DslFlowExecutor<TestApp.GenericState<string>, TestApp.GenericFlow>");
    }

    [Fact]
    public async Task Generator_HandlesNestedNamespaces()
    {
        // Arrange
        var source = @"
using Catga.Flow.Dsl;
using System.Collections.Generic;

namespace TestApp.Domain.Flows.Order
{
    public class NestedOrderFlow : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow) { }
    }

    public class OrderState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }
}";

        // Act
        var (compilation, diagnostics) = await CompileWithGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();

        var generatedFile = compilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("CatgaGeneratedFlowRegistrations"));

        var content = generatedFile!.ToString();

        // Should handle nested namespaces
        content.Should().Contain("AddNestedOrderFlow");
        content.Should().Contain("TestApp.Domain.Flows.Order.NestedOrderFlow");
        content.Should().Contain("TestApp.Domain.Flows.Order.OrderState");
    }

    [Fact]
    public async Task Generator_GeneratesFlowRegistrationRecord()
    {
        // Arrange
        var source = @"
using Catga.Flow.Dsl;
using System.Collections.Generic;

namespace TestApp
{
    public class TestFlow : FlowConfig<TestState>
    {
        protected override void Configure(IFlowBuilder<TestState> flow) { }
    }

    public class TestState : IFlowState
    {
        public string? FlowId { get; set; }
        public bool HasChanges => false;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }
}";

        // Act
        var (compilation, diagnostics) = await CompileWithGenerator(source);

        // Assert
        var generatedFile = compilation.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.Contains("CatgaGeneratedFlowRegistrations"));

        var content = generatedFile!.ToString();

        // Should generate FlowRegistration record
        content.Should().Contain("public record FlowRegistration");
        content.Should().Contain("string Name");
        content.Should().Contain("System.Type StateType");
        content.Should().Contain("System.Type FlowType");

        // Should use it in GetRegisteredFlows
        content.Should().Contain(@"new FlowRegistration(""TestFlow""");
    }

    // Helper method to compile source with generator
    private async Task<(Compilation, ImmutableArray<Diagnostic>)> CompileWithGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IFlowState).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FlowConfig<>).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new FlowDslRegistrationGenerator();

        var driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics);

        return (newCompilation, diagnostics);
    }
}
