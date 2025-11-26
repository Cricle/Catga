# Source Generator Guide (Legacy)

> **Note**: For the most up-to-date guide, see [source-generator-usage.md](./source-generator-usage.md)

# Source Generator Guide

Catga provides a **Source Generator** to automatically discover and register handlers at compile time, eliminating the need for manual registration and ensuring full Native AOT compatibility.

## 📖 Overview

The Catga Source Generator:
- ✅ Automatically finds all `IRequestHandler<,>` and `IEventHandler<>` implementations
- ✅ Generates registration code at compile time
- ✅ Zero reflection - fully AOT compatible
- ✅ Better IDE experience with IntelliSense
- ✅ Reduced boilerplate code
- ✅ Faster startup time

## 🚀 Quick Start

### 1. Install Packages

```xml
<ItemGroup>
  <PackageReference Include="Catga" />

  <!-- Add the source generator as an analyzer -->
  <ProjectReference Include="..\..\src\Catga.SourceGenerator\Catga.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 2. Write Your Handlers

Just implement the handler interfaces - no attributes needed!

```csharp
// Command Handler
public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    public async Task<CatgaResult<CreateUserResponse>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        // Your logic here
        return CatgaResult<CreateUserResponse>.Success(response);
    }
}

// Event Handler
public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    public async Task HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Your logic here
    }
}
```

### 3. Register Handlers

Use the generated extension method:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure Catga
builder.Services.AddCatga();

// ✨ ONE LINE - Source generator does the rest!
builder.Services.AddGeneratedHandlers();

var app = builder.Build();
```

## 🔍 How It Works

### Compile-Time Discovery

The source generator runs during compilation and:

1. **Scans your project** for classes implementing:
   - `IRequestHandler<TRequest, TResponse>`
   - `IEventHandler<TEvent>`

2. **Generates registration code** in the `Catga.DependencyInjection` namespace:
   ```csharp
   public static class CatgaGeneratedHandlerRegistrations
   {
       public static IServiceCollection AddGeneratedHandlers(this IServiceCollection services)
       {
           services.AddScoped<IRequestHandler<CreateUserCommand, CreateUserResponse>, CreateUserCommandHandler>();
           services.AddScoped<IEventHandler<UserCreatedEvent>, UserCreatedEventHandler>();
           return services;
       }
   }
   ```

3. **No runtime overhead** - all registration happens at compile time

### Generated Files

The source generator creates two files:

#### `CatgaHandlerAttribute.g.cs`
Defines an optional attribute for future customization:
```csharp
[CatgaHandler(ServiceLifetime.Scoped)]  // Optional - default is Scoped
public class MyHandler : IRequestHandler<MyCommand, MyResponse>
{
    // ...
}
```

#### `CatgaHandlerRegistration.g.cs`
Contains the `AddGeneratedHandlers()` extension method with all discovered handlers.

## 📂 View Generated Code

To see the generated code during build:

```bash
dotnet build /p:EmitCompilerGeneratedFiles=true
```

Generated files location:
```
obj/Debug/net9.0/generated/Catga.SourceGenerator/Catga.SourceGenerator.CatgaHandlerGenerator/
├── CatgaHandlerAttribute.g.cs
└── CatgaHandlerRegistration.g.cs
```

## 🎯 AOT Compatibility

The source generator approach is **fully Native AOT compatible**:

### ✅ Benefits

1. **No Reflection** - All handler types are known at compile time
2. **No Assembly Scanning** - No runtime type discovery
3. **Smaller Binary** - Unused code can be trimmed
4. **Faster Startup** - No runtime registration overhead
5. **Better Performance** - Direct method calls instead of dynamic invocation

### ❌ What's NOT Needed

```csharp
// ❌ No manual registration
services.AddScoped<IRequestHandler<CreateUserCommand, CreateUserResponse>, CreateUserCommandHandler>();

// ❌ No reflection-based scanning
services.AddHandlersFromAssembly(Assembly.GetExecutingAssembly());

// ❌ No runtime type discovery
[RequiresUnreferencedCode("Uses reflection")]
public static void ScanHandlers() { ... }
```

### ✅ What You Get

```csharp
// ✅ Simple, AOT-friendly registration
services.AddGeneratedHandlers();
```

## 🛠 Advanced Usage

### Custom Service Lifetime

Use the `[CatgaHandler]` attribute to customize service lifetime:

```csharp
[CatgaHandler(ServiceLifetime.Singleton)]
public class CachedQueryHandler : IRequestHandler<GetCachedDataQuery, CachedData>
{
    // This handler will be registered as Singleton
}

[CatgaHandler(ServiceLifetime.Transient)]
public class OneTimeHandler : IRequestHandler<OneTimeCommand, Unit>
{
    // This handler will be registered as Transient
}

// No attribute = Scoped (default)
public class NormalHandler : IRequestHandler<NormalCommand, NormalResponse>
{
    // This handler will be registered as Scoped
}
```

### Multiple Projects

Each project with handlers should:

1. Reference the source generator
2. Call `AddGeneratedHandlers()` in startup

```csharp
// In your main project
services.AddGeneratedHandlers();  // Registers handlers from this assembly

// If you have handlers in other assemblies, use manual registration:
services.AddScoped<IRequestHandler<ExternalCommand, ExternalResponse>, ExternalHandler>();
```

## 🐛 Troubleshooting

### Handlers Not Found

**Problem**: `AddGeneratedHandlers()` doesn't register any handlers

**Solutions**:
1. Ensure handlers implement `IRequestHandler<,>` or `IEventHandler<>`
2. Rebuild the project: `dotnet build`
3. Clean and rebuild: `dotnet clean && dotnet build`
4. Check generated code: `dotnet build /p:EmitCompilerGeneratedFiles=true`

### IntelliSense Not Working

**Problem**: IDE doesn't recognize `AddGeneratedHandlers()`

**Solutions**:
1. Rebuild the project
2. Restart your IDE
3. Close and reopen the solution
4. Clear IDE caches (varies by IDE)

### AOT Warnings

**Problem**: Getting AOT warnings about `AddGeneratedHandlers()`

**This should NOT happen** - the generated code is fully AOT compatible. If you see warnings:
1. Check if you're using the correct package version
2. Report an issue with the warning message

## 📊 Performance Comparison

| Approach | Startup Time | Binary Size | AOT Compatible |
|----------|--------------|-------------|----------------|
| Manual Registration | Fast | Small | ✅ Yes |
| Source Generator | Fast | Small | ✅ Yes |
| Reflection Scanning | Slow | Large | ❌ No |

## 🔗 Related

- [Getting Started Guide](../articles/getting-started.md)
- [AOT Compatibility](../aot/serialization-aot-guide.md)
- [OrderSystem Example](../../examples/OrderSystem.Api/README.md)

## 🤝 Contributing

Found a bug or have a feature request? Please [open an issue](https://github.com/Cricle/Catga/issues).
