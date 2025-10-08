# Catga Analyzers Guide

Catga provides **Roslyn Analyzers** to help you write better code by detecting common issues at compile time.

## 📖 Overview

The Catga Analyzers package includes:
- ✅ **Compile-time diagnostics** - Find issues before runtime
- ✅ **Code fixes** - Automatic suggestions to fix problems
- ✅ **IDE integration** - Works in Visual Studio, VS Code, Rider
- ✅ **CI/CD friendly** - Fail builds on warnings

## 🚀 Installation

### Option 1: With Source Generator (Recommended)

If you're already using the source generator, analyzers are included:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Catga.SourceGenerator\Catga.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### Option 2: Standalone Analyzer

Add the analyzer package:

```xml
<ItemGroup>
  <PackageReference Include="Catga.Analyzers" Version="1.0.0" />
</ItemGroup>
```

## 🔍 Diagnostic Rules

### CATGA001: Handler Not Registered

**Severity**: Info
**Category**: Usage

**Description**: Detects handlers that implement `IRequestHandler` or `IEventHandler` but may not be registered.

**Example**:
```csharp
// ⚠️ CATGA001: Handler may not be registered
public class MyHandler : IRequestHandler<MyCommand, MyResponse>
{
    public Task<CatgaResult<MyResponse>> HandleAsync(...)
    {
        // ...
    }
}
```

**Fix**: Ensure you call `AddGeneratedHandlers()` in your startup code:
```csharp
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();  // ✅ Registers all handlers
```

### CATGA002: Invalid Handler Signature

**Severity**: Warning
**Category**: Design

**Description**: Handler method doesn't follow the correct signature pattern.

**Example**:
```csharp
public class MyHandler : IRequestHandler<MyCommand, MyResponse>
{
    // ❌ CATGA002: Invalid signature
    public void Handle(MyCommand request)
    {
        // ...
    }
}
```

**Correct Signature**:
```csharp
public class MyHandler : IRequestHandler<MyCommand, MyResponse>
{
    // ✅ Correct signature
    public Task<CatgaResult<MyResponse>> HandleAsync(
        MyCommand request,
        CancellationToken cancellationToken = default)
    {
        // ...
    }
}
```

### CATGA003: Missing Async Suffix

**Severity**: Info
**Category**: Naming

**Description**: Async methods should end with 'Async' for clarity.

**Example**:
```csharp
// ⚠️ CATGA003: Missing 'Async' suffix
public Task<CatgaResult<MyResponse>> Handle(MyCommand request)
{
    // ...
}
```

**Code Fix Available**:
```csharp
// ✅ Fixed automatically
public Task<CatgaResult<MyResponse>> HandleAsync(MyCommand request)
{
    // ...
}
```

### CATGA004: Missing CancellationToken

**Severity**: Info
**Category**: Design

**Description**: Handler should accept `CancellationToken` for better async control.

**Example**:
```csharp
// ⚠️ CATGA004: Missing CancellationToken
public Task<CatgaResult<MyResponse>> HandleAsync(MyCommand request)
{
    // ...
}
```

**Code Fix Available**:
```csharp
// ✅ Fixed automatically
public Task<CatgaResult<MyResponse>> HandleAsync(
    MyCommand request,
    CancellationToken cancellationToken = default)
{
    // ...
}
```

## 💡 Using Code Fixes

### In Visual Studio / VS Code

1. Place cursor on the warning
2. Press `Ctrl+.` (or click the light bulb 💡)
3. Select the suggested fix
4. Code is automatically corrected

### Example

**Before**:
```csharp
public Task<CatgaResult<User>> Handle(CreateUserCommand request)  // ⚠️ CATGA003
{
    return Task.FromResult(CatgaResult<User>.Success(user));
}
```

**After** (Press `Ctrl+.` and select "Add 'Async' suffix"):
```csharp
public Task<CatgaResult<User>> HandleAsync(CreateUserCommand request)  // ✅
{
    return Task.FromResult(CatgaResult<User>.Success(user));
}
```

## ⚙️ Configuration

### Suppress Specific Warnings

#### In Code
```csharp
#pragma warning disable CATGA001
public class MyHandler : IRequestHandler<MyCommand, MyResponse>
{
    // ...
}
#pragma warning restore CATGA001
```

#### In .editorconfig
```ini
# Disable CATGA001 globally
dotnet_diagnostic.CATGA001.severity = none

# Or set to error to fail builds
dotnet_diagnostic.CATGA002.severity = error
```

### Change Severity

```xml
<!-- In .csproj -->
<PropertyGroup>
  <WarningsAsErrors>CATGA002</WarningsAsErrors>
</PropertyGroup>
```

## 🎯 Best Practices

### 1. Enable All Analyzers

```xml
<PropertyGroup>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <AnalysisLevel>latest</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

### 2. Treat Warnings as Errors in CI

```xml
<PropertyGroup Condition="'$(CI)' == 'true'">
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

### 3. Use Code Fixes

Don't ignore warnings - use the automatic code fixes to correct issues quickly.

### 4. Review Info-Level Diagnostics

Even "Info" level diagnostics can help improve code quality:
- CATGA001: Reminds you to use source generator
- CATGA003: Enforces async naming conventions
- CATGA004: Encourages cancellation support

## 🐛 Troubleshooting

### Analyzer Not Working

**Problem**: Warnings don't appear

**Solutions**:
1. **Rebuild the project**: `dotnet clean && dotnet build`
2. **Restart IDE**: Close and reopen Visual Studio / VS Code
3. **Check analyzer is loaded**:
   ```bash
   dotnet build /v:detailed | findstr "Catga.Analyzers"
   ```

### Code Fix Not Available

**Problem**: Can't see the light bulb or code fix

**Solutions**:
1. Ensure you have the latest version
2. Only some diagnostics have code fixes:
   - ✅ CATGA003: Add 'Async' suffix
   - ✅ CATGA004: Add CancellationToken
   - ❌ CATGA001: No automatic fix (requires manual registration)
   - ❌ CATGA002: No automatic fix (signature too complex)

### Too Many Warnings

**Problem**: Analyzer reports too many warnings

**Solutions**:
1. **Fix incrementally**: Suppress and fix one type at a time
2. **Adjust severity**: Use `.editorconfig` to lower severity
3. **Disable specific rules**: Use `#pragma warning disable`

## 📊 Example Project

See the `examples/SimpleWebApi` project for analyzer integration:

```xml
<!-- SimpleWebApi.csproj -->
<ItemGroup>
  <!-- Source Generator + Analyzers -->
  <ProjectReference Include="..\..\src\Catga.SourceGenerator\Catga.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />

  <!-- Optional: Standalone Analyzers -->
  <PackageReference Include="Catga.Analyzers" Version="1.0.0" />
</ItemGroup>
```

## 🔗 Related

- [Source Generator Guide](source-generator.md)
- [Getting Started](GETTING_STARTED.md)
- [API Design](FRIENDLY_API.md)

## 🤝 Contributing

Found a bug or want to add a new diagnostic? Please [open an issue](https://github.com/Cricle/Catga/issues)!

### Adding New Diagnostics

1. Create new `DiagnosticDescriptor` in `CatgaHandlerAnalyzer.cs`
2. Implement detection logic
3. Add code fix in `CatgaCodeFixProvider.cs` (if applicable)
4. Update this documentation

---

**Status**: ✅ Production Ready
**Supported IDEs**: Visual Studio, VS Code, Rider
**CI/CD**: Fully supported
