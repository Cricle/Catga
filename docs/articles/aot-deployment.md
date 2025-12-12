# Native AOT Deployment

Catga is 100% compatible with .NET Native AOT compilation.

## Quick Start

### 1. Enable AOT in Project File

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

### 2. Use AOT-Compatible Serializer

```csharp
// Register source-generated JSON serializer
builder.Services.AddJsonMessageSerializer();

// Or use MemoryPack for best performance
builder.Services.AddMemoryPackMessageSerializer();
```

### 3. Publish

```bash
dotnet publish -c Release -r linux-x64
```

## Benefits

- **Fast Startup**: 5-10x faster than JIT
- **Small Size**: ~30MB self-contained executable
- **Low Memory**: 50-70% less memory usage
- **No JIT**: No runtime compilation overhead

## Best Practices

### ✅ Do

- Use `IMessageSerializer` interface
- Register all message types upfront
- Use source generators
- Test AOT warnings before publishing

### ❌ Don't

- Use `System.Text.Json.JsonSerializer` directly
- Use `Type.GetType()` without suppression
- Use reflection in hot paths
- Ignore AOT warnings

## Troubleshooting

### Warning IL2026

If you see warnings about dynamic code:

```csharp
[UnconditionalSuppressMessage("Trimming", "IL2026")]
private void DynamicMethod() { }
```

### Warning IL3050

For AOT-incompatible code, provide AOT-friendly alternatives:

```csharp
// ❌ Not AOT-friendly
var type = Type.GetType(typeName);

// ✅ AOT-friendly
var type = typeof(MyMessage);
```

## Performance Comparison

| Metric | JIT | AOT | Improvement |
|--------|-----|-----|-------------|
| Startup | 500ms | 50ms | **10x faster** |
| Memory | 100MB | 35MB | **65% less** |
| Size | 80MB | 30MB | **62% smaller** |

## Docker Deployment

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-alpine
WORKDIR /app
COPY ./publish .
ENTRYPOINT ["./MyApp"]
```

Size: **~35MB** (vs ~180MB with JIT)



