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
/// Code fix provider that converts GroupBy anonymous type key to tuple.
/// This enables SelectExpr to work correctly with grouped queries.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GroupByAnonymousKeyCodeFixProvider))]
[Shared]
public class GroupByAnonymousKeyCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        [GroupByAnonymousKeyAnalyzer.AnalyzerId];

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the GroupBy invocation at the diagnostic location
        var groupByNode = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv => IsGroupByInvocation(inv.Expression));

        if (groupByNode == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert anonymous type to tuple",
                createChangedDocument: c =>
                    ConvertAnonymousToTupleAsync(context.Document, groupByNode, c),
                equivalenceKey: "ConvertAnonymousToTuple"
            ),
            diagnostic
        );
    }

    private static async Task<Document> ConvertAnonymousToTupleAsync(
        Document document,
        InvocationExpressionSyntax groupByInvocation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Find the anonymous type in the GroupBy lambda
        var anonymousType = FindAnonymousTypeInArguments(groupByInvocation.ArgumentList);
        if (anonymousType == null)
            return document;

        // Convert anonymous type to simpler expression
        var newExpression = ConvertAnonymousTypeToExpression(anonymousType);
        if (newExpression == null)
            return document;

        // Replace the anonymous type with the new expression
        var newRoot = root.ReplaceNode(anonymousType, newExpression);

        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsGroupByInvocation(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.Text == "GroupBy";

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text == "GroupBy";

            default:
                return false;
        }
    }

    private static AnonymousObjectCreationExpressionSyntax? FindAnonymousTypeInArguments(
        ArgumentListSyntax argumentList
    )
    {
        foreach (var argument in argumentList.Arguments)
        {
            // Look for lambda expressions
            var lambda = argument.Expression switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Body,
                ParenthesizedLambdaExpressionSyntax paren => paren.Body,
                _ => null,
            };

            if (lambda is AnonymousObjectCreationExpressionSyntax anonymousObject)
            {
                return anonymousObject;
            }
        }

        return null;
    }

    /// <summary>
    /// Converts an anonymous type to an expression that can be used as GroupBy key.
    /// For single properties, returns the expression directly.
    /// For multiple properties, returns a tuple expression.
    /// </summary>
    private static ExpressionSyntax? ConvertAnonymousTypeToExpression(
        AnonymousObjectCreationExpressionSyntax anonymousType
    )
    {
        var initializers = anonymousType.Initializers;
        if (initializers.Count == 0)
            return null;

        // For a single element without explicit name, return just the expression
        // new { x.Id } -> x.Id
        if (initializers.Count == 1)
        {
            var singleInit = initializers[0];
            if (singleInit.NameEquals == null)
            {
                return singleInit
                    .Expression.WithLeadingTrivia(anonymousType.GetLeadingTrivia())
                    .WithTrailingTrivia(anonymousType.GetTrailingTrivia());
            }
            // new { Key = x.Id } -> (Key: x.Id) - needs tuple for named element
        }

        var tupleArguments = initializers.Select(ConvertInitializerToTupleArgument).ToArray();

        // Create separated list with commas
        var separatedList = SyntaxFactory.SeparatedList(
            tupleArguments,
            Enumerable.Repeat(
                SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
                tupleArguments.Length - 1
            )
        );

        return SyntaxFactory
            .TupleExpression(separatedList)
            .WithLeadingTrivia(anonymousType.GetLeadingTrivia())
            .WithTrailingTrivia(anonymousType.GetTrailingTrivia());
    }

    private static ArgumentSyntax ConvertInitializerToTupleArgument(
        AnonymousObjectMemberDeclaratorSyntax initializer
    )
    {
        var expression = initializer.Expression;
        var nameEquals = initializer.NameEquals;

        if (nameEquals != null)
        {
            // Named property: { Name = value } -> Name: value
            var designation = SyntaxFactory.NameColon(nameEquals.Name);
            return SyntaxFactory.Argument(designation, default, expression);
        }
        else
        {
            // Inferred property: { x.Property } -> x.Property (tuple will infer the name)
            return SyntaxFactory.Argument(expression);
        }
    }
}
