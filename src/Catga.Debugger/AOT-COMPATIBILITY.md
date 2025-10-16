# Catga.Debugger - AOT Compatibility Guide

## Overview

**Catga.Debugger** is designed with AOT compatibility in mind, but includes optional reflection-based features for development convenience.

## AOT Status

| Component | AOT Compatible | Notes |
|-----------|---------------|-------|
| **Event Capture** | ⚠️ Partial | Requires `IDebugCapture` implementation for full AOT |
| **Event Storage** | ✅ Yes | Ring buffer, zero-allocation |
| **Replay Engine** | ✅ Yes | Fully AOT-compatible |
| **State Reconstruction** | ✅ Yes | No reflection used |
| **Adaptive Sampling** | ✅ Yes | No reflection used |

## Reflection Usage

### Variable Capture (Optional)

**Location**: `ReplayableEventCapturer<TRequest, TResponse>.CaptureVariables(object)`

**Purpose**: Captures message properties for debugging

**AOT Impact**: Uses reflection to inspect object properties

**Solution**: Implement `IDebugCapture` on your message types

```csharp
[MemoryPackable]
public partial record CreateOrderCommand : IRequest<OrderCreatedResult>, IDebugCapture
{
    public required string CustomerId { get; init; }
    public required List<OrderItem> Items { get; init; }
    
    // AOT-friendly variable capture
    public Dictionary<string, object?> CaptureVariables()
    {
        return new()
        {
            [nameof(CustomerId)] = CustomerId,
            ["ItemCount"] = Items?.Count ?? 0,
        };
    }
}
```

### Call Stack Capture (Optional)

**Location**: `ReplayableEventCapturer<TRequest, TResponse>.CaptureCallStack()`

**Purpose**: Captures call stack for debugging

**AOT Impact**: Uses `StackFrame.GetMethod()` which requires metadata

**Solution**: Disable in production:

```csharp
services.AddCatgaDebugger(options =>
{
    options.CaptureCallStacks = false; // Disable for AOT
});
```

## Production AOT Configuration

### Recommended Settings

```csharp
services.AddCatgaDebugger(options =>
{
    // Mode
    options.Mode = DebuggerMode.ProductionOptimized;
    options.SamplingRate = 0.001; // 0.1%
    
    // Disable reflection-based features
    options.CaptureVariables = false;    // Use IDebugCapture instead
    options.CaptureCallStacks = false;   // Not AOT-compatible
    options.CaptureMemoryState = false;  // Optional, not needed
    
    // Zero-allocation features
    options.UseRingBuffer = true;
    options.EnableZeroCopy = true;
    options.EnableObjectPooling = true;
    options.EnableAdaptiveSampling = true;
});
```

### Pre-configured Production Mode

```csharp
// Simplest approach - all AOT-compatible defaults
services.AddCatgaDebuggerForProduction();
```

## Development Mode

For development, you can use reflection-based features:

```csharp
services.AddCatgaDebuggerForDevelopment();
```

This enables:
- ✅ 100% sampling
- ✅ Reflection-based variable capture (fallback)
- ✅ Call stack capture
- ✅ Memory state capture

## AOT Publishing

### Step 1: Disable Reflection Features

```csharp
services.AddCatgaDebugger(options =>
{
    options.CaptureCallStacks = false;
    options.CaptureVariables = false; // Or implement IDebugCapture
});
```

### Step 2: Implement IDebugCapture

```csharp
public partial record MyCommand : IRequest<MyResult>, IDebugCapture
{
    public Dictionary<string, object?> CaptureVariables()
    {
        return new()
        {
            ["Property1"] = Property1,
            ["Property2"] = Property2,
        };
    }
}
```

### Step 3: Publish with AOT

```bash
dotnet publish -c Release -r win-x64 --property:PublishAot=true
```

## Warnings Explained

### IL2091 - Generic Constraints

**Warning**: `'TRequest' generic argument does not satisfy 'DynamicallyAccessedMemberTypes.All'`

**Status**: ✅ Suppressed

**Reason**: Generic constraints are enforced by `IPipelineBehavior` interface. Types are validated at DI registration time.

### IL2026/IL3050 - Reflection Usage

**Warning**: `Using member 'CaptureVariables' which has 'RequiresUnreferencedCodeAttribute'`

**Status**: ✅ Suppressed at call sites

**Reason**: Methods are marked with attributes. Callers are aware of AOT limitations.

## Best Practices

1. **Use IDebugCapture** - Implement on all messages for full AOT compatibility
2. **Disable Call Stacks** - Not needed in production
3. **Use Production Mode** - Pre-configured for AOT
4. **Test AOT Builds** - Verify no runtime errors

## Migration Path

### From Reflection to AOT

**Before** (Development):
```csharp
// Automatic reflection-based capture
public partial record MyCommand : IRequest<MyResult>
{
    public string Property { get; init; }
}
```

**After** (Production AOT):
```csharp
// Explicit AOT-friendly capture
public partial record MyCommand : IRequest<MyResult>, IDebugCapture
{
    public string Property { get; init; }
    
    public Dictionary<string, object?> CaptureVariables()
    {
        return new() { [nameof(Property)] = Property };
    }
}
```

## Performance Impact

| Feature | AOT | Reflection |
|---------|-----|-----------|
| **Event Capture** | <0.01μs | <0.01μs |
| **Variable Capture (IDebugCapture)** | <0.005μs | N/A |
| **Variable Capture (Reflection)** | N/A | ~1μs |
| **Call Stack Capture** | Disabled | ~5μs |

## Support

For AOT-related issues, please:
1. Check this guide
2. Review `docs/DEBUGGER.md`
3. See `examples/OrderSystem.Api` for working example
4. Open an issue on GitHub

## Summary

✅ **Fully AOT-compatible** when using `IDebugCapture`  
⚠️ **Partial AOT** with reflection fallback (development only)  
❌ **Not AOT** if call stack capture is enabled (disable in production)

**Recommendation**: Use `AddCatgaDebuggerForProduction()` for production AOT deployments.

