using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts Select to SelectExpr
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SelectToSelectExprCodeFixProvider)), Shared]
public class SelectToSelectExprCodeFixProvider : CodeFixProvider
{
    private const string Title = "Convert to SelectExpr";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.SelectToSelectExpr.Id);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the invocation expression
        var token = root.FindToken(diagnosticSpan.Start);
        var invocation = token.Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().First();
        if (invocation == null)
            return;

        // Register the code fix
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ConvertToSelectExprAsync(context.Document, invocation, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ConvertToSelectExprAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Get the member access expression (e.g., "query.Select")
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        // Create new member access with "SelectExpr" instead of "Select"
        var newMemberAccess = memberAccess.WithName(
            SyntaxFactory.IdentifierName("SelectExpr")
                .WithTriviaFrom(memberAccess.Name));

        // Create the new invocation
        var newInvocation = invocation.WithExpression(newMemberAccess);

        // Replace the old invocation with the new one
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }
}
