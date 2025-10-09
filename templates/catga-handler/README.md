# Catga Handler Template

This template generates a complete CQRS handler with all necessary files.

## Usage

```bash
# Create a command handler
dotnet new catga-handler -n CreateUser --HandlerType command

# Create a query handler
dotnet new catga-handler -n GetUser --HandlerType query

# Create without validator
dotnet new catga-handler -n DeleteUser --IncludeValidator false

# Create without tests
dotnet new catga-handler -n UpdateUser --IncludeTests false
```

## Generated Files

For a **Command** handler:
- `{Name}Command.cs` - Command and response records
- `{Name}CommandHandler.cs` - Handler implementation
- `{Name}Validator.cs` - Validation logic (optional)
- `{Name}Tests.cs` - Unit tests (optional)

For a **Query** handler:
- `{Name}Query.cs` - Query and result records
- `{Name}QueryHandler.cs` - Handler implementation
- `{Name}Tests.cs` - Unit tests (optional)

## Example Usage in API

```csharp
// In your controller or endpoint
app.MapPost("/api/users", async (
    ICatgaMediator mediator,
    CreateUserCommand command) =>
{
    var response = await mediator.SendAsync(command);
    return Results.Created($"/api/users/{response.Id}", response);
});
```

## Customization

After generation, customize the handler:

1. **Add domain logic** in the `Handle` method
2. **Add validation rules** in the validator
3. **Add more test cases** in the test file
4. **Inject dependencies** via constructor

## Best Practices

- Keep handlers focused on one responsibility
- Use validators for input validation
- Write comprehensive unit tests
- Log important events
- Handle cancellation tokens properly

