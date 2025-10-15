# Catga.Debugger.AspNetCore

ASP.NET Core integration for Catga.Debugger - Real-time debugging with Minimal APIs and SignalR.

## Features

- ✅ **Minimal APIs** - Fully AOT-compatible REST endpoints
- ⚠️ **SignalR Hub** - Real-time updates (not AOT-compatible)
- ✅ **Typed Responses** - All responses use strongly-typed records
- ✅ **Zero Allocation** - Channel-based event queuing
- ✅ **Backpressure** - Automatic handling of slow consumers

## AOT Compatibility

| Feature | AOT Compatible | Notes |
|---------|---------------|-------|
| Minimal API Endpoints | ✅ Yes | Fully AOT-compatible |
| Typed Responses | ✅ Yes | Uses source-generated JSON (future) |
| Background Service | ✅ Yes | Uses PeriodicTimer + Channels |
| SignalR Hub | ❌ No | SignalR requires runtime code generation |

**Recommendation**: For AOT deployments, use only the Minimal API endpoints. SignalR requires standard runtime compilation.

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add debugger with ASP.NET Core
builder.Services.AddCatgaDebuggerWithAspNetCore(options =>
{
    options.Mode = DebuggerMode.ProductionOptimized;
    options.SamplingRate = 0.001; // 0.1%
});

var app = builder.Build();

// Map endpoints and hub
app.MapCatgaDebugger("/debug");

app.Run();
```

## API Endpoints

- `GET /debug-api/flows` - Get all active flows
- `GET /debug-api/flows/{correlationId}` - Get specific flow
- `GET /debug-api/stats` - Get event store statistics
- `POST /debug-api/replay/system` - Start system-wide replay
- `POST /debug-api/replay/flow` - Start flow-level replay

## SignalR Hub

**Endpoint**: `/debug/hub`

### Client Methods

```typescript
// Subscribe to flow updates
await connection.invoke("SubscribeToFlow", "correlation-id");

// Subscribe to system updates
await connection.invoke("SubscribeToSystem");

// Get current stats
const stats = await connection.invoke("GetStats");
```

### Server Events

```typescript
// Flow event received
connection.on("FlowEventReceived", (update) => {
  console.log(update);
});

// Stats updated
connection.on("StatsUpdated", (stats) => {
  console.log(stats);
});

// Replay progress
connection.on("ReplayProgress", (progress) => {
  console.log(progress);
});
```

## Performance

- **Channel-based queuing** - Zero allocation, bounded buffers
- **PeriodicTimer** - No Thread Pool exhaustion
- **Backpressure control** - Drops oldest events when full
- **Batched broadcasts** - Efficient SignalR updates

## Security

```csharp
// Require authentication
builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DebuggerPolicy", policy =>
        policy.RequireRole("Admin", "Developer"));
});

// Apply to endpoints
app.MapCatgaDebuggerApi()
   .RequireAuthorization("DebuggerPolicy");
```

