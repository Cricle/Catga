using System.Diagnostics.CodeAnalysis;

// RS1038: Analyzers that reference Workspaces for CodeFixProvider are allowed
[assembly: SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1038:Compiler extensions should be implemented in assemblies with compiler-provided references",
    Justification = "CodeFixProvider requires Workspaces dependency which is expected and valid",
    Scope = "type",
    Target = "~T:Catga.Analyzers.BestPracticeAnalyzers")]

[assembly: SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1038:Compiler extensions should be implemented in assemblies with compiler-provided references",
    Justification = "CodeFixProvider requires Workspaces dependency which is expected and valid",
    Scope = "type",
    Target = "~T:Catga.Analyzers.PerformanceAnalyzers")]

[assembly: SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1038:Compiler extensions should be implemented in assemblies with compiler-provided references",
    Justification = "CodeFixProvider requires Workspaces dependency which is expected and valid",
    Scope = "type",
    Target = "~T:Catga.Analyzers.CatgaHandlerAnalyzer")]

