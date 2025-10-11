using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Catga.Analyzers;

/// <summary>
/// Analyzer for detecting concurrency and thread-safety issues
/// Helps identify potential race conditions and thread-safety violations
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ConcurrencySafetyAnalyzer : DiagnosticAnalyzer
{
    // CATGA201: Non-thread-safe collection in concurrent context
    private static readonly DiagnosticDescriptor NonThreadSafeCollectionRule = new(
        id: "CATGA201",
        title: "Non-thread-safe collection used in concurrent context",
        messageFormat: "Collection type '{0}' is not thread-safe. Consider using Concurrent{0} or adding synchronization.",
        category: "Concurrency",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Non-thread-safe collections like Dictionary, List, HashSet can cause race conditions in concurrent scenarios. Use ConcurrentDictionary, ConcurrentBag, etc.");

    // CATGA202: Missing volatile or Interlocked for shared field
    private static readonly DiagnosticDescriptor MissingVolatileRule = new(
        id: "CATGA202",
        title: "Shared field accessed without synchronization",
        messageFormat: "Field '{0}' is accessed from multiple threads without volatile or Interlocked. This may cause visibility issues.",
        category: "Concurrency",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Fields accessed from multiple threads should use volatile keyword or Interlocked methods to ensure proper memory visibility.");

    // CATGA203: Potential deadlock detected
    private static readonly DiagnosticDescriptor PotentialDeadlockRule = new(
        id: "CATGA203",
        title: "Potential deadlock detected",
        messageFormat: "Lock acquisition order may cause deadlock. Lock on '{0}' after '{1}' conflicts with reverse order elsewhere.",
        category: "Concurrency",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Inconsistent lock ordering can cause deadlocks. Always acquire locks in the same order across all code paths.");

    // CATGA204: Double-checked locking without volatile
    private static readonly DiagnosticDescriptor DoubleCheckedLockingRule = new(
        id: "CATGA204",
        title: "Double-checked locking without volatile field",
        messageFormat: "Double-checked locking pattern detected but field '{0}' is not volatile. This can cause initialization bugs.",
        category: "Concurrency",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Double-checked locking requires the field to be volatile to work correctly. Without volatile, other threads may see partially initialized objects.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            NonThreadSafeCollectionRule,
            MissingVolatileRule,
            PotentialDeadlockRule,
            DoubleCheckedLockingRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLockStatement, SyntaxKind.LockStatement);
        context.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);
    }

    private void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        var fieldDeclaration = (FieldDeclarationSyntax)context.Node;

        // Skip if already volatile or const
        if (fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.VolatileKeyword) ||
                                                m.IsKind(SyntaxKind.ConstKeyword) ||
                                                m.IsKind(SyntaxKind.ReadOnlyKeyword)))
            return;

        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            var fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
            if (fieldSymbol == null)
                continue;

            var fieldType = fieldSymbol.Type;

            // Check for non-thread-safe collections
            if (IsNonThreadSafeCollection(fieldType))
            {
                // Check if field is instance field in a potentially concurrent type
                if (!fieldSymbol.IsStatic && IsPotentiallyConcurrentType(fieldSymbol.ContainingType))
                {
                    var diagnostic = Diagnostic.Create(
                        NonThreadSafeCollectionRule,
                        variable.GetLocation(),
                        fieldType.Name);
                    context.ReportDiagnostic(diagnostic);
                }
            }

            // Check for shared mutable fields without synchronization
            if (fieldSymbol.IsStatic && !fieldSymbol.IsConst && IsMutableType(fieldType))
            {
                var diagnostic = Diagnostic.Create(
                    MissingVolatileRule,
                    variable.GetLocation(),
                    fieldSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private void AnalyzeLockStatement(SyntaxNodeAnalysisContext context)
    {
        var lockStatement = (LockStatementSyntax)context.Node;

        // Check for nested locks (potential deadlock)
        var parentLock = lockStatement.Ancestors().OfType<LockStatementSyntax>().FirstOrDefault();
        if (parentLock != null)
        {
            var outerLockExpr = GetLockExpression(parentLock);
            var innerLockExpr = GetLockExpression(lockStatement);

            if (outerLockExpr != null && innerLockExpr != null)
            {
                var diagnostic = Diagnostic.Create(
                    PotentialDeadlockRule,
                    lockStatement.GetLocation(),
                    innerLockExpr,
                    outerLockExpr);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;

        // Detect double-checked locking pattern
        // Pattern: if (field == null) { lock(obj) { if (field == null) { field = ...; } } }

        if (ifStatement.Statement is not BlockSyntax outerBlock)
            return;

        var lockStatement = outerBlock.Statements.OfType<LockStatementSyntax>().FirstOrDefault();
        if (lockStatement == null)
            return;

        if (lockStatement.Statement is not BlockSyntax innerBlock)
            return;

        var innerIf = innerBlock.Statements.OfType<IfStatementSyntax>().FirstOrDefault();
        if (innerIf == null)
            return;

        // Check if both conditions check the same field
        var outerCondition = GetNullCheckField(context, ifStatement.Condition);
        var innerCondition = GetNullCheckField(context, innerIf.Condition);

        if (outerCondition != null && innerCondition != null &&
            outerCondition.Equals(innerCondition, SymbolEqualityComparer.Default))
        {
            // Check if field is volatile
            if (outerCondition is IFieldSymbol fieldSymbol && !fieldSymbol.IsVolatile)
            {
                var diagnostic = Diagnostic.Create(
                    DoubleCheckedLockingRule,
                    ifStatement.GetLocation(),
                    fieldSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsNonThreadSafeCollection(ITypeSymbol type)
    {
        var typeName = type.ToDisplayString();

        return typeName.StartsWith("System.Collections.Generic.Dictionary<") ||
               typeName.StartsWith("System.Collections.Generic.List<") ||
               typeName.StartsWith("System.Collections.Generic.HashSet<") ||
               typeName.StartsWith("System.Collections.Generic.Queue<") ||
               typeName.StartsWith("System.Collections.Generic.Stack<");
    }

    private static bool IsPotentiallyConcurrentType(INamedTypeSymbol type)
    {
        // Check if type is used in concurrent scenarios (handlers, services, etc.)
        if (type.Name.EndsWith("Handler") ||
            type.Name.EndsWith("Service") ||
            type.Name.EndsWith("Repository") ||
            type.Name.EndsWith("Store"))
            return true;

        // Check if type implements handler interfaces
        foreach (var @interface in type.AllInterfaces)
        {
            var interfaceName = @interface.ToDisplayString();
            if (interfaceName.Contains("IRequestHandler") ||
                interfaceName.Contains("IEventHandler") ||
                interfaceName.Contains("IService"))
                return true;
        }

        return false;
    }

    private static bool IsMutableType(ITypeSymbol type)
    {
        // Value types are generally immutable if all fields are readonly
        if (type.IsValueType)
            return false;

        // String is immutable
        if (type.SpecialType == SpecialType.System_String)
            return false;

        // Everything else is potentially mutable
        return true;
    }

    private static string? GetLockExpression(LockStatementSyntax lockStatement)
    {
        return lockStatement.Expression.ToString();
    }

    private static ISymbol? GetNullCheckField(SyntaxNodeAnalysisContext context, ExpressionSyntax condition)
    {
        // Handle: field == null or null == field
        if (condition is BinaryExpressionSyntax binary)
        {
            if (binary.IsKind(SyntaxKind.EqualsExpression))
            {
                if (binary.Right.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    return context.SemanticModel.GetSymbolInfo(binary.Left).Symbol;
                }
                if (binary.Left.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    return context.SemanticModel.GetSymbolInfo(binary.Right).Symbol;
                }
            }
        }

        return null;
    }
}

