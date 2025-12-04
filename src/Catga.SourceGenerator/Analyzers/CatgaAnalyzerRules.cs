using Microsoft.CodeAnalysis;

namespace Catga.SourceGenerator.Analyzers;

/// <summary>Diagnostic rule definitions for Catga analyzers</summary>
internal static class CatgaAnalyzerRules
{
    private const string Category = "Catga";

    // Performance rules (CAT1xxx)
    public static readonly DiagnosticDescriptor MissingAotAttribute = new(
        id: "CAT1001",
        title: "Handler should be marked with [DynamicallyAccessedMembers] for AOT",
        messageFormat: "Handler '{0}' should have [DynamicallyAccessedMembers] attribute for Native AOT compatibility",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Handlers without [DynamicallyAccessedMembers] may cause trimming issues in Native AOT.");

    public static readonly DiagnosticDescriptor BlockingCallInHandler = new(
        id: "CAT1002",
        title: "Avoid blocking calls in async handlers",
        messageFormat: "Handler '{0}' contains blocking call '{1}'. Use async/await instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blocking calls (.Result, .Wait()) can cause deadlocks. Use await instead.");

    public static readonly DiagnosticDescriptor ReflectionInHandler = new(
        id: "CAT1003",
        title: "Avoid reflection in handlers",
        messageFormat: "Handler '{0}' uses reflection which is not AOT-compatible",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reflection is not supported in Native AOT. Use source generators instead.");

    // Usage rules (CAT2xxx)
    public static readonly DiagnosticDescriptor HandlerNotRegistered = new(
        id: "CAT2001",
        title: "Handler is not registered in DI container",
        messageFormat: "Handler '{0}' is defined but not registered. Use AddHandler() or AddGeneratedHandlers().",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Handlers must be registered in the DI container to be used.");

    public static readonly DiagnosticDescriptor MessageWithoutHandler = new(
        id: "CAT2002",
        title: "Message has no handler",
        messageFormat: "Message '{0}' is sent but no handler is registered",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every message should have at least one handler.");

    public static readonly DiagnosticDescriptor MultipleSyncHandlers = new(
        id: "CAT2003",
        title: "Multiple handlers for Request",
        messageFormat: "Request '{0}' has multiple handlers. Only one handler is allowed for IRequest<T>.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Requests can only have one handler. Use INotification for multiple handlers.",
        customTags: "CompilationEnd");

    // Design rules (CAT3xxx)
    public static readonly DiagnosticDescriptor CommandShouldNotReturnData = new(
        id: "CAT3001",
        title: "Command should not return domain data",
        messageFormat: "Command '{0}' returns complex data. Consider using Query or returning only ID/status.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Commands should modify state and return minimal data (void, bool, ID). Use Query for data retrieval.");

    public static readonly DiagnosticDescriptor QueryShouldBeImmutable = new(
        id: "CAT3002",
        title: "Query should be immutable",
        messageFormat: "Query '{0}' has mutable properties. Queries should be immutable (use record or readonly).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Queries represent read-only operations and should be immutable.");

    public static readonly DiagnosticDescriptor EventShouldBePastTense = new(
        id: "CAT3003",
        title: "Event should use past tense",
        messageFormat: "Event '{0}' should use past tense (e.g., 'UserCreated' not 'CreateUser')",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Events represent something that has already happened.");

    // Serialization rules (CAT4xxx)
    public static readonly DiagnosticDescriptor MissingMemoryPackAttribute = new(
        id: "CAT4001",
        title: "Message should have [MemoryPackable] for AOT",
        messageFormat: "Message '{0}' should be marked with [MemoryPackable] for Native AOT serialization",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false, // User choice
        description: "MemoryPack provides zero-allocation, AOT-friendly serialization.");

    public static readonly DiagnosticDescriptor NonSerializableProperty = new(
        id: "CAT4002",
        title: "Property is not serializable",
        messageFormat: "Property '{0}' in message '{1}' may not be serializable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "All message properties should be serializable for distributed messaging.");

    // AutoTelemetry rules (CAT5xxx)
    public static readonly DiagnosticDescriptor MissingPartialModifier = new(
        id: "CAT5001",
        title: "Handler with [CatgaHandler] must be partial",
        messageFormat: "Class '{0}' has [CatgaHandler] but is not partial. Add 'partial' modifier.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes with [CatgaHandler] must be partial to allow source generation.");

    public static readonly DiagnosticDescriptor MissingHandleAsyncCore = new(
        id: "CAT5002",
        title: "Handler with [CatgaHandler] must implement HandleAsyncCore",
        messageFormat: "Class '{0}' has [CatgaHandler] but is missing 'HandleAsyncCore' method",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes with [CatgaHandler] must implement HandleAsyncCore method.");

    public static readonly DiagnosticDescriptor WrongHandleAsyncCoreSignature = new(
        id: "CAT5003",
        title: "HandleAsyncCore has wrong signature",
        messageFormat: "Method 'HandleAsyncCore' in '{0}' has wrong signature",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HandleAsyncCore must match the expected signature.");

    public static readonly DiagnosticDescriptor HandlerNotImplementingInterface = new(
        id: "CAT5004",
        title: "Handler with [CatgaHandler] must implement IRequestHandler",
        messageFormat: "Class '{0}' has [CatgaHandler] but does not implement IRequestHandler",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes with [CatgaHandler] must implement IRequestHandler interface.");
}

