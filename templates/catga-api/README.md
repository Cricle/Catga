# CatgaApi

A Catga-powered Web API project with CQRS pattern.

## Features

- ✅ CQRS with Catga framework
- ✅ Source-generated handlers
- ✅ Automatic validation
#if (EnableRateLimiting)
- ✅ Rate limiting
#endif
#if (EnableDistributedId)
- ✅ Distributed ID generation
#endif
#if (EnableOpenAPI)
- ✅ OpenAPI/Swagger
#endif

## Getting Started

### Run the application

```bash
dotnet run
```

#if (EnableOpenAPI)
### Access Swagger UI

Navigate to: `https://localhost:5001/swagger`
#endif

### Send a sample command

```bash
curl -X POST https://localhost:5001/api/commands/sample \
  -H "Content-Type: application/json" \
  -d '{"name":"Test","description":"Sample command"}'
```

## Project Structure

```
CatgaApi/
├── Commands/           # Command definitions and handlers
├── Queries/            # Query definitions and handlers (add your own)
├── Events/             # Event definitions and handlers (add your own)
├── Program.cs          # Application entry point
└── appsettings.json    # Configuration
```

## Adding New Handlers

1. Create a command/query class implementing `IRequest<TResponse>`
2. Create a handler class implementing `IRequestHandler<TRequest, TResponse>`
3. Handlers are automatically registered via source generators

Example:

```csharp
public record CreateUserCommand(string Username, string Email) 
    : IRequest<CreateUserResponse>;

public class CreateUserCommandHandler 
    : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    public async ValueTask<CatgaResult<CreateUserResponse>> Handle(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        // Your logic here
        return CatgaResult<CreateUserResponse>.Success(new CreateUserResponse(...));
    }
}
```

## Learn More

- [Catga Documentation](https://github.com/Cricle/Catga)
- [CQRS Pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/cqrs)

