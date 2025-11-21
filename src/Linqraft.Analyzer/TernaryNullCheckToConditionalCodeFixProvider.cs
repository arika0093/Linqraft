using System.Collections.Generic;
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
/// Code fix provider that converts ternary null checks returning objects to use null-conditional operators.
/// </summary>
[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(TernaryNullCheckToConditionalCodeFixProvider)
)]
[Shared]
public class TernaryNullCheckToConditionalCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(TernaryNullCheckToConditionalAnalyzer.DiagnosticId);

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

        var conditional = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<ConditionalExpressionSyntax>()
            .First();

        if (conditional == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Use null-conditional operators",
                createChangedDocument: c =>
                    ConvertToNullConditionalAsync(context.Document, conditional, c),
                equivalenceKey: "UseNullConditionalOperators"
            ),
            diagnostic
        );
    }

    private static async Task<Document> ConvertToNullConditionalAsync(
        Document document,
        ConditionalExpressionSyntax conditional,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Extract null checks from the condition
        var nullChecks = ExtractNullChecks(conditional.Condition);
        if (nullChecks.Count == 0)
            return document;

        // Get the "when true" expression
        var whenTrueExpr = RemoveNullableCast(conditional.WhenTrue);

        // Convert the object creation to use null-conditional operators
        var converted = ConvertObjectCreationToNullConditional(whenTrueExpr, nullChecks);
        if (converted == null)
            return document;

        var newRoot = root.ReplaceNode(conditional, converted);
        return document.WithSyntaxRoot(newRoot);
    }

    private static ExpressionSyntax? ConvertObjectCreationToNullConditional(
        ExpressionSyntax objectCreation,
        List<ExpressionSyntax> nullChecks
    )
    {
        // Create a rewriter to replace member accesses with null-conditional versions
        var rewriter = new NullConditionalRewriter(nullChecks);
        return (ExpressionSyntax)rewriter.Visit(objectCreation);
    }

    private class NullConditionalRewriter : CSharpSyntaxRewriter
    {
        private readonly HashSet<string> _nullCheckedPaths;

        public NullConditionalRewriter(List<ExpressionSyntax> nullChecks)
        {
            _nullCheckedPaths = new HashSet<string>(nullChecks.Select(nc => nc.ToString()));
        }

        public override SyntaxNode? VisitMemberAccessExpression(
            MemberAccessExpressionSyntax node
        )
        {
            // Decompose the member access chain
            var parts = DecomposeMemberAccessChain(node);
            if (parts.Count == 0)
                return node;

            // Build the expression with conditional accesses where needed
            ExpressionSyntax result = SyntaxFactory.IdentifierName(parts[0]);
            
            for (int i = 1; i < parts.Count; i++)
            {
                // Build the path up to the previous part
                var previousPath = string.Join(".", parts.Take(i));
                
                // Check if the previous path was null-checked
                if (_nullCheckedPaths.Contains(previousPath))
                {
                    // We need to start or continue a conditional access chain
                    // Check if result is already a conditional access
                    if (result is ConditionalAccessExpressionSyntax existingConditional)
                    {
                        // Continue the conditional chain by updating the WhenNotNull part
                        var newWhenNotNull = AppendMemberBinding(
                            existingConditional.WhenNotNull,
                            parts[i]
                        );
                        result = existingConditional.WithWhenNotNull(newWhenNotNull);
                    }
                    else
                    {
                        // Start a new conditional access
                        result = SyntaxFactory.ConditionalAccessExpression(
                            result,
                            SyntaxFactory.MemberBindingExpression(
                                SyntaxFactory.IdentifierName(parts[i])
                            )
                        );
                    }
                }
                else
                {
                    // Regular member access
                    result = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        result,
                        SyntaxFactory.IdentifierName(parts[i])
                    );
                }
            }

            return result;
        }

        private ExpressionSyntax AppendMemberBinding(
            ExpressionSyntax whenNotNull,
            string memberName
        )
        {
            // Append another member binding to the WhenNotNull part
            var newBinding = SyntaxFactory.MemberBindingExpression(
                SyntaxFactory.IdentifierName(memberName)
            );

            // If whenNotNull is already a conditional access, we need to nest
            if (whenNotNull is ConditionalAccessExpressionSyntax nested)
            {
                return nested.WithWhenNotNull(AppendMemberBinding(nested.WhenNotNull, memberName));
            }

            // If it's a member binding, create a conditional access
            if (whenNotNull is MemberBindingExpressionSyntax)
            {
                return SyntaxFactory.ConditionalAccessExpression(whenNotNull, newBinding);
            }

            // Fallback: just return the new binding
            return newBinding;
        }

        private List<string> DecomposeMemberAccessChain(MemberAccessExpressionSyntax node)
        {
            var parts = new List<string>();
            var current = (ExpressionSyntax)node;

            while (current != null)
            {
                switch (current)
                {
                    case MemberAccessExpressionSyntax memberAccess:
                        parts.Insert(0, memberAccess.Name.Identifier.Text);
                        current = memberAccess.Expression;
                        break;
                    case IdentifierNameSyntax identifier:
                        parts.Insert(0, identifier.Identifier.Text);
                        current = null!;
                        break;
                    default:
                        // Unsupported expression type, return empty to skip rewriting
                        return new List<string>();
                }
            }

            return parts;
        }
    }

    private static List<ExpressionSyntax> ExtractNullChecks(ExpressionSyntax condition)
    {
        var nullChecks = new List<ExpressionSyntax>();

        // Handle chained && expressions
        var current = condition;
        while (
            current is BinaryExpressionSyntax binary
            && binary.Kind() == SyntaxKind.LogicalAndExpression
        )
        {
            // Check the right side
            if (IsNullCheckComparison(binary.Right, out var checkedExpr))
            {
                nullChecks.Insert(0, checkedExpr);
            }

            current = binary.Left;
        }

        // Check the final (leftmost) expression
        if (IsNullCheckComparison(current, out var finalCheckedExpr))
        {
            nullChecks.Insert(0, finalCheckedExpr);
        }

        return nullChecks;
    }

    private static bool IsNullCheckComparison(
        ExpressionSyntax expr,
        out ExpressionSyntax checkedExpression
    )
    {
        checkedExpression = null!;

        if (expr is not BinaryExpressionSyntax binary)
            return false;

        // Check for "x != null" pattern
        if (binary.Kind() == SyntaxKind.NotEqualsExpression)
        {
            if (IsNullLiteral(binary.Right))
            {
                checkedExpression = binary.Left;
                return true;
            }
            if (IsNullLiteral(binary.Left))
            {
                checkedExpression = binary.Right;
                return true;
            }
        }

        return false;
    }

    private static bool IsNullLiteral(ExpressionSyntax expr)
    {
        return expr is LiteralExpressionSyntax literal
            && literal.Kind() == SyntaxKind.NullLiteralExpression;
    }

    private static ExpressionSyntax RemoveNullableCast(ExpressionSyntax expr)
    {
        // Remove (Type?) cast if present
        if (expr is CastExpressionSyntax cast && cast.Type is NullableTypeSyntax)
        {
            return cast.Expression;
        }

        return expr;
    }
}
