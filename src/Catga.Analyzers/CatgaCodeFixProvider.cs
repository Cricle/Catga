using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Catga.Analyzers;

/// <summary>
/// Code fix provider for Catga analyzer diagnostics
/// CodeFixProvider requires Workspaces dependency which is expected
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CatgaCodeFixProvider)), Shared]
[SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1038:Compiler extensions should be implemented in assemblies with compiler-provided references", 
    Justification = "CodeFixProvider requires Workspaces which is a valid dependency for code fix providers")]
public class CatgaCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            CatgaHandlerAnalyzer.MissingAsyncSuffixDiagnosticId,
            CatgaHandlerAnalyzer.MissingCancellationTokenDiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var declaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First();
        if (declaration == null)
            return;

        // Register code fixes based on diagnostic ID
        switch (diagnostic.Id)
        {
            case CatgaHandlerAnalyzer.MissingAsyncSuffixDiagnosticId:
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Add 'Async' suffix",
                        createChangedDocument: c => AddAsyncSuffixAsync(context.Document, declaration, c),
                        equivalenceKey: nameof(CatgaHandlerAnalyzer.MissingAsyncSuffixDiagnosticId)),
                    diagnostic);
                break;

            case CatgaHandlerAnalyzer.MissingCancellationTokenDiagnosticId:
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Add CancellationToken parameter",
                        createChangedDocument: c => AddCancellationTokenAsync(context.Document, declaration, c),
                        equivalenceKey: nameof(CatgaHandlerAnalyzer.MissingCancellationTokenDiagnosticId)),
                    diagnostic);
                break;
        }
    }

    private static async Task<Document> AddAsyncSuffixAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newName = methodDeclaration.Identifier.Text + "Async";
        var newIdentifier = SyntaxFactory.Identifier(newName);
        var newMethod = methodDeclaration.WithIdentifier(newIdentifier);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddCancellationTokenAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Add CancellationToken parameter as last parameter with default value
        var cancellationTokenParam = SyntaxFactory.Parameter(
            SyntaxFactory.Identifier("cancellationToken"))
            .WithType(SyntaxFactory.ParseTypeName("CancellationToken"))
            .WithDefault(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.DefaultLiteralExpression,
                    SyntaxFactory.Token(SyntaxKind.DefaultKeyword))));

        var newParameters = methodDeclaration.ParameterList.AddParameters(cancellationTokenParam);
        var newMethod = methodDeclaration.WithParameterList(newParameters);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }
}
