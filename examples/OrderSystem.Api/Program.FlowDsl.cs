using Catga.Flow.Extensions;
using Catga.Flow.Dsl;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Flows;

namespace OrderSystem.Api;

/// <summary>
/// Program configuration specifically for Flow DSL features.
/// Shows all possible ways to configure and use Flow DSL.
/// </summary>
public static class ProgramFlowDslExtensions
{
    /// <summary>
    /// Configure Flow DSL with all features demonstrated.
    /// </summary>
    public static WebApplicationBuilder ConfigureFlowDsl(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var environment = builder.Environment;

        // Method 1: Simple InMemory configuration for development
        if (environment.IsDevelopment())
        {
            builder.Services.AddFlowDsl(options =>
            {
                options.AutoRegisterFlows = true;
                options.EnableMetrics = true;
                options.MaxRetryAttempts = 3;
                options.StepTimeout = TimeSpan.FromMinutes(5);
            });
        }

        // Method 2: Redis configuration for staging/production
        else if (environment.IsStaging() || environment.IsProduction())
        {
            var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";

            builder.Services.AddFlowDslWithRedis(redisConnection, options =>
            {
                options.RedisPrefix = "orderflow:";
                options.AutoRegisterFlows = true;
                options.EnableMetrics = true;
            });
        }

        // Method 3: NATS configuration for event-driven scenarios
        else if (configuration.GetValue<bool>("UseNats"))
        {
            var natsUrl = configuration.GetValue<string>("NatsUrl") ?? "nats://localhost:4222";

            builder.Services.AddFlowDslWithNats(natsUrl, options =>
            {
                options.NatsBucket = "orderflows";
                options.AutoRegisterFlows = true;
            });
        }

        // Method 4: Configuration-based setup
        // This reads from appsettings.json
        builder.Services.AddFlowDslFromConfiguration(configuration);

        // Method 5: Fluent builder pattern with source-generated registration
        builder.Services.ConfigureFlowDsl(flow => flow
            .UseRedisStorage("localhost:6379", "orderflow:")
            .RegisterGeneratedFlows() // Automatically registers all source-generated flows
            .RegisterFlow<OrderFlowState, ComprehensiveOrderFlow>() // Can still manually register specific flows
            .WithMetrics()
            .WithRetryPolicy(maxAttempts: 3, retryDelay: TimeSpan.FromSeconds(5))
            .WithStepTimeout(TimeSpan.FromMinutes(10)));

        // Register specific flows manually if needed
        builder.Services
            .AddFlow<OrderFlowState, ComprehensiveOrderFlow>()
            .AddFlow<PaymentFlowState, PaymentProcessingFlow>()
            .AddFlow<ShippingFlowState, ShippingOrchestrationFlow>()
            .AddFlow<InventoryFlowState, InventoryManagementFlow>()
            .AddFlow<CustomerFlowState, CustomerOnboardingFlow>();

        return builder;
    }

    /// <summary>
    /// Configure Flow DSL endpoints and middleware.
    /// </summary>
    public static WebApplication UseFlowDsl(this WebApplication app)
    {
        // Map Flow DSL management endpoints
        var flowGroup = app.MapGroup("/api/flows")
            .WithTags("Flow DSL")
            .RequireAuthorization();

        // List all registered flows
        flowGroup.MapGet("/", async (IServiceProvider provider) =>
        {
            var flowTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.BaseType?.IsGenericType == true &&
                           t.BaseType.GetGenericTypeDefinition() == typeof(FlowConfig<>))
                .Select(t => new
                {
                    Name = t.Name,
                    StateType = t.BaseType!.GetGenericArguments()[0].Name,
                    Assembly = t.Assembly.GetName().Name
                })
                .ToList();

            return Results.Ok(flowTypes);
        });

        // Get flow status
        flowGroup.MapGet("/{flowId}/status", async (
            string flowId,
            IDslFlowStore store,
            CancellationToken ct) =>
        {
            var snapshot = await store.GetAsync<OrderFlowState>(flowId, ct);
            if (snapshot == null)
                return Results.NotFound($"Flow {flowId} not found");

            return Results.Ok(new
            {
                snapshot.FlowId,
                snapshot.Status,
                snapshot.Position,
                snapshot.CreatedAt,
                snapshot.UpdatedAt,
                snapshot.Version,
                snapshot.Error
            });
        });

        // Start a new flow
        flowGroup.MapPost("/order/start", async (
            StartOrderFlowRequest request,
            DslFlowExecutor<OrderFlowState, ComprehensiveOrderFlow> executor,
            CancellationToken ct) =>
        {
            var state = new OrderFlowState
            {
                FlowId = Guid.NewGuid().ToString(),
                OrderId = request.OrderId,
                Order = request.Order,
                Warehouses = request.AvailableWarehouses
            };

            var result = await executor.RunAsync(state, ct);

            return result.IsSuccess
                ? Results.Ok(new { FlowId = state.FlowId, Status = result.Status })
                : Results.BadRequest(new { Error = result.Error });
        });

        // Resume a flow
        flowGroup.MapPost("/{flowId}/resume", async (
            string flowId,
            DslFlowExecutor<OrderFlowState, ComprehensiveOrderFlow> executor,
            CancellationToken ct) =>
        {
            var result = await executor.ResumeAsync(flowId, ct);

            return result.IsSuccess
                ? Results.Ok(new { Status = result.Status, State = result.State })
                : Results.BadRequest(new { Error = result.Error });
        });

        // Get flow metrics
        flowGroup.MapGet("/metrics", async (IDslFlowStore store) =>
        {
            // This would connect to your metrics provider
            return Results.Ok(new
            {
                TotalFlows = 1000, // Example metrics
                ActiveFlows = 50,
                CompletedFlows = 900,
                FailedFlows = 50,
                AverageExecutionTime = "45s",
                SuccessRate = "90%"
            });
        });

        // Get timed-out wait conditions
        flowGroup.MapGet("/wait-conditions/timed-out", async (
            IDslFlowStore store,
            CancellationToken ct) =>
        {
            var timedOut = await store.GetTimedOutWaitConditionsAsync(ct);
            return Results.Ok(timedOut);
        });

        // Get ForEach progress
        flowGroup.MapGet("/{flowId}/progress/{stepIndex}", async (
            string flowId,
            int stepIndex,
            IDslFlowStore store,
            CancellationToken ct) =>
        {
            var progress = await store.GetForEachProgressAsync(flowId, stepIndex, ct);
            return progress != null
                ? Results.Ok(progress)
                : Results.NotFound();
        });

        return app;
    }
}

// Request/Response models
public class StartOrderFlowRequest
{
    public string OrderId { get; set; } = string.Empty;
    public Order Order { get; set; } = new();
    public List<Warehouse> AvailableWarehouses { get; set; } = new();
}

// Additional flow configurations for complete example

public class PaymentProcessingFlow : FlowConfig<PaymentFlowState>
{
    protected override void Configure(IFlowBuilder<PaymentFlowState> flow)
    {
        flow.Name("payment-processing");

        // Global retry (simple demo)
        flow.Retry(3);

        // Single payment step
        flow.Send(s => new ProcessPaymentCommand(s.PaymentId, s.Amount));
    }
}

[FlowState]
public partial class PaymentFlowState : IFlowState
{
    public string? FlowId { get; set; }

    [FlowStateField]
    private string _paymentId = string.Empty;

    [FlowStateField]
    private decimal _amount;
}

public class ShippingOrchestrationFlow : FlowConfig<ShippingFlowState>
{
    protected override void Configure(IFlowBuilder<ShippingFlowState> flow)
    {
        flow.Name("shipping-orchestration");
        // Simplified shipping flow for demo
    }
}

[FlowState]
public partial class ShippingFlowState : IFlowState
{
    public string? FlowId { get; set; }

    [FlowStateField]
    private string _shipmentId = string.Empty;

    [FlowStateField]
    private string _selectedCarrier = string.Empty;

    [FlowStateField]
    private ShippingQuote? _selectedQuote;
}

public class InventoryManagementFlow : FlowConfig<InventoryFlowState>
{
    protected override void Configure(IFlowBuilder<InventoryFlowState> flow)
    {
        flow.Name("inventory-management");
        // Simplified inventory flow for demo
    }
}

[FlowState]
public partial class InventoryFlowState : IFlowState
{
    public string? FlowId { get; set; }

    [FlowStateField]
    private List<Product> _products = new();

    [FlowStateField]
    private int _totalQuantity;
}

public class CustomerOnboardingFlow : FlowConfig<CustomerFlowState>
{
    protected override void Configure(IFlowBuilder<CustomerFlowState> flow)
    {
        flow.Name("customer-onboarding");

        // Sequential customer onboarding steps
        flow.Send(s => new ValidateCustomerDataCommand(s.CustomerId));
        flow.Send(s => new CreateCustomerAccountCommand(s.CustomerId));
        flow.Send(s => new SendWelcomePackageCommand(s.CustomerId));
        flow.Publish(s => new CustomerOnboardedEvent { CustomerId = s.CustomerId });
    }
}

[FlowState]
public partial class CustomerFlowState : IFlowState
{
    public string? FlowId { get; set; }

    [FlowStateField]
    private string _customerId = string.Empty;
}

// Supporting classes
public class Product
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class ShippingQuote
{
    public string Carrier { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public int EstimatedDays { get; set; }
}

public class PaymentResult
{
    public string Provider { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
}
