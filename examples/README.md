# Catga Examples

## OrderSystem.Api

Simple order system demonstrating Catga with source-generated endpoints.

```bash
cd examples/OrderSystem.Api
dotnet run
```

Open http://localhost:5275/swagger for API docs.

## Features

- **Source Generated Endpoints** - `[Route]` attribute auto-generates API endpoints
- **CQRS Pattern** - Commands and queries separation
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
