using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core.SyntaxHelpers;
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
        [TernaryNullCheckToConditionalAnalyzer.AnalyzerId];

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

        // Get the containing statement to extract proper indentation
        var containingStatement = conditional.Ancestors().FirstOrDefault(a => a is StatementSyntax);

        // Extract null checks from the condition
        var nullChecks = ExtractNullChecks(conditional.Condition);
        if (nullChecks.Count == 0)
            return document;

        // Determine which branch has the object creation
        var whenTrueExpr = RemoveNullableCast(conditional.WhenTrue);
        var whenFalseExpr = RemoveNullableCast(conditional.WhenFalse);

        var whenTrueIsNull = IsNullOrNullCast(conditional.WhenTrue);
        var whenFalseIsNull = IsNullOrNullCast(conditional.WhenFalse);

        ExpressionSyntax objectCreationExpr;
        ExpressionSyntax originalObjectCreationWithTrivia;
        List<ExpressionSyntax> effectiveNullChecks;

        if (whenFalseIsNull && !whenTrueIsNull)
        {
            // Standard case: condition ? new{} : null
            objectCreationExpr = whenTrueExpr;
            originalObjectCreationWithTrivia = conditional.WhenTrue;
            effectiveNullChecks = nullChecks;
        }
        else if (whenTrueIsNull && !whenFalseIsNull)
        {
            // Inverted case: condition ? null : new{}
            // When the condition is inverted (e.g., p.Child == null ? null : new{}),
            // the null checks need to be inverted conceptually
            objectCreationExpr = whenFalseExpr;
            originalObjectCreationWithTrivia = conditional.WhenFalse;
            effectiveNullChecks = InvertNullChecks(nullChecks);
        }
        else
        {
            // Neither or both are null - shouldn't happen based on analyzer, but handle gracefully
            return document;
        }

        // Convert the object creation to use null-conditional operators
        // Preserve the structure and trivia of the object creation
        var converted = ConvertObjectCreationToNullConditional(
            objectCreationExpr,
            effectiveNullChecks
        );
        if (converted == null)
            return document;

        // Preserve overall trivia from the conditional expression
        converted = TriviaHelper.PreserveTrivia(conditional, converted);

        var newRoot = root.ReplaceNode(conditional, converted);
        var documentWithNewRoot = document.WithSyntaxRoot(newRoot);

        // Format and normalize line endings to ensure proper indentation
        return await CodeFixFormattingHelper
            .FormatAndNormalizeLineEndingsAsync(documentWithNewRoot, cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<ExpressionSyntax> InvertNullChecks(List<ExpressionSyntax> nullChecks)
    {
        // When the condition is inverted (e.g., p.Child == null instead of p.Child != null),
        // the null checks extracted should work the same way for our purposes,
        // since we're converting member accesses to null-conditional anyway.
        // So we can just return the same list.
        return nullChecks;
    }

    private static ExpressionSyntax PreserveTriviaForObjectCreation(
        ExpressionSyntax newExpression,
        ExpressionSyntax originalExpression,
        SyntaxNode? containingStatement
    )
    {
        // For object creation expressions, we need to preserve the trivia structure
        // to maintain proper indentation and formatting
        // Note: We do NOT preserve trailing trivia here, as that will come from the conditional

        // Get the statement-level indentation from the containing statement
        var statementIndentation =
            containingStatement
                ?.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia))
                .LastOrDefault()
            ?? default;

        if (
            newExpression is ObjectCreationExpressionSyntax newObjCreation
            && originalExpression is ObjectCreationExpressionSyntax origObjCreation
        )
        {
            // Preserve initializer trivia if present
            if (newObjCreation.Initializer != null && origObjCreation.Initializer != null)
            {
                var newInitializer = newObjCreation
                    .Initializer.WithOpenBraceToken(
                        newObjCreation.Initializer.OpenBraceToken.WithTriviaFrom(
                            origObjCreation.Initializer.OpenBraceToken
                        )
                    )
                    .WithCloseBraceToken(
                        // Clear all trivia - let the formatter handle it
                        newObjCreation.Initializer.CloseBraceToken.WithoutTrivia()
                    );

                newObjCreation = newObjCreation.WithInitializer(newInitializer);
            }

            // Only preserve leading trivia of the entire expression
            return newObjCreation.WithLeadingTrivia(originalExpression.GetLeadingTrivia());
        }

        if (
            newExpression is AnonymousObjectCreationExpressionSyntax newAnonCreation
            && originalExpression is AnonymousObjectCreationExpressionSyntax origAnonCreation
        )
        {
            // Preserve brace trivia for anonymous objects
            // Use statement-level indentation instead of the original (which is inside ternary branch)
            return newAnonCreation
                .WithNewKeyword(
                    newAnonCreation.NewKeyword.WithTriviaFrom(origAnonCreation.NewKeyword)
                )
                .WithOpenBraceToken(origAnonCreation.OpenBraceToken)
                .WithCloseBraceToken(
                    // Clear all trivia - let the formatter handle it
                    newAnonCreation.CloseBraceToken.WithoutTrivia()
                )
                .WithLeadingTrivia(originalExpression.GetLeadingTrivia());
        }

        if (
            newExpression is AnonymousObjectCreationExpressionSyntax anonCreation
            && originalExpression is CastExpressionSyntax castToAnon
            && castToAnon.Expression is AnonymousObjectCreationExpressionSyntax origAnon
        )
        {
            // Handle cast expressions wrapping anonymous objects
            // Use statement-level indentation instead of the original (which is inside ternary branch)
            return anonCreation
                .WithNewKeyword(anonCreation.NewKeyword.WithTriviaFrom(origAnon.NewKeyword))
                .WithOpenBraceToken(origAnon.OpenBraceToken)
                .WithCloseBraceToken(
                    // Clear all trivia - let the formatter handle it
                    anonCreation.CloseBraceToken.WithoutTrivia()
                )
                .WithLeadingTrivia(castToAnon.GetLeadingTrivia());
        }

        // For cast expressions, unwrap and preserve trivia
        if (originalExpression is CastExpressionSyntax cast)
        {
            return PreserveTriviaForObjectCreation(
                newExpression,
                cast.Expression,
                containingStatement
            );
        }

        // Fallback: just preserve leading trivia
        return newExpression.WithLeadingTrivia(originalExpression.GetLeadingTrivia());
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

        public override SyntaxNode? VisitAnonymousObjectCreationExpression(
            AnonymousObjectCreationExpressionSyntax node
        )
        {
            // Transform each initializer while preserving the structure
            var newInitializers = new List<AnonymousObjectMemberDeclaratorSyntax>();

            foreach (var initializer in node.Initializers)
            {
                // Visit the expression to convert member accesses to null-conditional
                var newExpression = (ExpressionSyntax)Visit(initializer.Expression);

                // Create new initializer preserving the original trivia
                AnonymousObjectMemberDeclaratorSyntax newInitializer;
                if (initializer.NameEquals != null)
                {
                    // Explicit name: preserve it with trivia
                    newInitializer = SyntaxFactory
                        .AnonymousObjectMemberDeclarator(initializer.NameEquals, newExpression)
                        .WithLeadingTrivia(initializer.GetLeadingTrivia())
                        .WithTrailingTrivia(initializer.GetTrailingTrivia());
                }
                else
                {
                    // Implicit name: create a declarator preserving trivia
                    newInitializer = SyntaxFactory
                        .AnonymousObjectMemberDeclarator(newExpression)
                        .WithLeadingTrivia(initializer.GetLeadingTrivia())
                        .WithTrailingTrivia(initializer.GetTrailingTrivia());
                }

                newInitializers.Add(newInitializer);
            }

            // Preserve the original separators (commas with their trivia)
            var originalSeparators = node.Initializers.GetSeparators().ToList();
            var newSeparatedList = SyntaxFactory.SeparatedList(newInitializers, originalSeparators);

            // Create new anonymous object creation preserving brace trivia
            var result = SyntaxFactory.AnonymousObjectCreationExpression(
                node.NewKeyword, // Preserve new keyword with its trivia
                node.OpenBraceToken, // Preserve open brace with its trivia
                newSeparatedList,
                node.CloseBraceToken // Preserve close brace with its trivia
            );

            // Preserve overall trivia from the original node
            return result
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
        }

        public override SyntaxNode? VisitObjectCreationExpression(
            ObjectCreationExpressionSyntax node
        )
        {
            // Handle named object creation similar to anonymous
            if (node.Initializer == null)
                return base.VisitObjectCreationExpression(node);

            // Transform each initializer expression while preserving structure and trivia
            var newExpressions = new List<ExpressionSyntax>();

            foreach (var expression in node.Initializer.Expressions)
            {
                // Visit to convert member accesses, preserving trivia
                var visitedExpression = (ExpressionSyntax)Visit(expression);

                // Preserve the trivia from the original expression
                var newExpression = visitedExpression
                    .WithLeadingTrivia(expression.GetLeadingTrivia())
                    .WithTrailingTrivia(expression.GetTrailingTrivia());

                newExpressions.Add(newExpression);
            }

            // Preserve the original separators (commas with their trivia)
            var originalSeparators = node.Initializer.Expressions.GetSeparators().ToList();
            var newSeparatedList = SyntaxFactory.SeparatedList(newExpressions, originalSeparators);

            // Create new initializer preserving brace trivia
            var newInitializer = SyntaxFactory.InitializerExpression(
                node.Initializer.Kind(),
                node.Initializer.OpenBraceToken,
                newSeparatedList,
                node.Initializer.CloseBraceToken
            );

            // Create new object creation with the new initializer
            var result = node.WithInitializer(newInitializer);

            return result;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
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

            // Preserve the trivia from the original node
            return TriviaHelper.PreserveTrivia(node, result);
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
            var current = (ExpressionSyntax?)node;

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
                        current = null;
                        break;
                    default:
                        // Unsupported expression type, return empty to skip rewriting
                        return [];
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

        // Check for "x == null" pattern (inverted condition)
        if (binary.Kind() == SyntaxKind.EqualsExpression)
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

    private static bool IsNullOrNullCast(ExpressionSyntax expr)
    {
        // Check for simple null
        if (IsNullLiteral(expr))
            return true;

        // Check for (Type?)null cast
        if (
            expr is CastExpressionSyntax cast
            && cast.Type is NullableTypeSyntax
            && IsNullLiteral(cast.Expression)
        )
            return true;

        return false;
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
