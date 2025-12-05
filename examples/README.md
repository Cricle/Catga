# Catga Examples

## OrderSystem.Api

Simple order system demonstrating Catga with source-generated endpoints.

```bash
cd examples/OrderSystem.Api
dotnet run
```

Open http://localhost:5275/swagger for API docs.

## Features

- **CQRS Pattern** - Commands and queries separation
- **Mediator Pattern** - ICatgaMediator for handler dispatch
- **MemoryPack** - Fast serialization

## Project Structure

```
examples/
├── OrderSystem.Api/          # Main API application
│   ├── Handlers/             # CQRS command/query handlers
│   ├── Services/             # Repository
│   ├── Domain/               # Order entity
│   └── Messages/             # Commands and queries
├── OrderSystem.AppHost/      # Aspire orchestration
└── SimpleOrder/              # Minimal example
```
