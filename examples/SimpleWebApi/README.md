# SimpleWebApi Example - Source Generator Demo

This example demonstrates the **simplified Catga API** using **Source Generator** for automatic handler registration.

## ✨ Key Features

1. **Automatic Handler Discovery** - No manual registration needed!
2. **Compile-Time Code Generation** - Fully AOT compatible
3. **Zero Reflection** - All registration happens at compile time
4. **Better IDE Experience** - IntelliSense works perfectly

## 🚀 What's Different?

### ❌ Old Way (Manual Registration)
```csharp
// Every handler needs manual registration
services.AddScoped<IRequestHandler<CreateUserCommand, CreateUserResponse>, CreateUserCommandHandler>();
services.AddScoped<IRequestHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
services.AddScoped<IEventHandler<UserCreatedEvent>, UserCreatedEventHandler>();
```

### ✅ New Way (Auto-Generated)
```csharp
// Just ONE line - source generator does the rest!
builder.Services.AddGeneratedHandlers();
```

## 📋 How It Works

1. **Write your handlers** - implement `IRequestHandler<,>` or `IEventHandler<>`
   ```csharp
   public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, CreateUserResponse>
   {
       // Your implementation
   }
   ```

2. **Build the project** - Source generator automatically finds all handlers

3. **Generated code** - Catga.SourceGenerator creates registration methods
   ```csharp
   // Auto-generated in Catga.DependencyInjection namespace
   public static IServiceCollection AddGeneratedHandlers(this IServiceCollection services)
   {
       services.AddScoped<IRequestHandler<CreateUserCommand, CreateUserResponse>, CreateUserCommandHandler>();
       services.AddScoped<IRequestHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
       services.AddScoped<IEventHandler<UserCreatedEvent>, UserCreatedEventHandler>();
       return services;
   }
   ```

## 🔧 Setup

### 1. Add NuGet Packages
```xml
<ItemGroup>
  <PackageReference Include="Catga" />
  <PackageReference Include="Catga.SourceGenerator"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
  <PackageReference Include="Catga.Serialization.Json" />
</ItemGroup>
```

### 2. Configure Catga
```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure Catga
builder.Services.AddCatga(options =>
{
    options.EnableLogging = true;
});

// Add serializer
builder.Services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();

// ✨ Auto-register all handlers - ONE LINE!
builder.Services.AddGeneratedHandlers();
```

### 3. Use in Controllers/Endpoints
```csharp
app.MapPost("/users", async (ICatgaMediator mediator, CreateUserCommand command) =>
{
    var result = await mediator.SendAsync<CreateUserCommand, CreateUserResponse>(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

## 🎯 AOT Compatibility

This approach is **fully Native AOT compatible**:
- ✅ No reflection at runtime
- ✅ No assembly scanning
- ✅ All registrations happen at compile time
- ✅ Smaller binary size
- ✅ Faster startup time

## 🔍 View Generated Code

To see the generated code during build:
```bash
dotnet build /p:EmitCompilerGeneratedFiles=true
```

Generated files will be in:
```
obj/Debug/net9.0/generated/Catga.SourceGenerator/
├── CatgaHandlerAttribute.g.cs
└── CatgaHandlerRegistration.g.cs
```

## 🏃 Run the Example

```bash
cd examples/SimpleWebApi
dotnet run
```

Then open: `https://localhost:5001/swagger`

### Test Endpoints

**Create User:**
```bash
curl -X POST https://localhost:5001/users \
  -H "Content-Type: application/json" \
  -d '{
    "username": "john_doe",
    "email": "john@example.com"
  }'
```

**Get User:**
```bash
curl https://localhost:5001/users/123
```

## 📖 Learn More

- [Source Generator Guide](../../docs/guides/source-generator.md)
- [AOT Compatibility](../../docs/aot/README.md)
- [Getting Started](../../docs/guides/GETTING_STARTED.md)
