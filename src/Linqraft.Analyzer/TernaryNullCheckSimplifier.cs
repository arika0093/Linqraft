using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Simplifies ternary operators used for null checks into null-conditional operators.
/// </summary>
internal static class TernaryNullCheckSimplifier
{
    /// <summary>
    /// Simplifies all ternary null checks in the given expression to use ?. and ?? operators.
    /// This includes both simple member access patterns and object creation patterns.
    /// </summary>
    public static ExpressionSyntax SimplifyTernaryNullChecks(ExpressionSyntax expression)
    {
        var rewriter = new TernaryNullCheckRewriter(simplifyObjectCreations: true);
        return (ExpressionSyntax)rewriter.Visit(expression);
    }

    /// <summary>
    /// Simplifies only simple member access ternary null checks (excludes object creation patterns).
    /// Use this for strict/predefined modes where object creation patterns should be preserved.
    /// </summary>
    public static ExpressionSyntax SimplifySimpleTernaryNullChecks(ExpressionSyntax expression)
    {
        var rewriter = new TernaryNullCheckRewriter(simplifyObjectCreations: false);
        return (ExpressionSyntax)rewriter.Visit(expression);
    }

    /// <summary>
    /// Simplifies ternary null checks in lambda expressions within an invocation.
    /// Processes all lambda arguments and applies null-conditional operator simplifications.
    /// </summary>
    public static InvocationExpressionSyntax SimplifyTernaryNullChecksInInvocation(
        InvocationExpressionSyntax invocation
    )
    {
        return SimplifyTernaryNullChecksInInvocation(invocation, simplifyObjectCreations: true);
    }

    /// <summary>
    /// Simplifies ternary null checks in lambda expressions within an invocation.
    /// Processes all lambda arguments and applies null-conditional operator simplifications.
    /// </summary>
    /// <param name="invocation">The invocation expression to process</param>
    /// <param name="simplifyObjectCreations">If true, simplifies object creation patterns.
    /// If false, only simplifies simple member access patterns.</param>
    public static InvocationExpressionSyntax SimplifyTernaryNullChecksInInvocation(
        InvocationExpressionSyntax invocation,
        bool simplifyObjectCreations
    )
    {
        // Find and simplify ternary null checks in lambda body
        var newArguments = new List<ArgumentSyntax>();
        var hasChanges = false;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (
                argument.Expression is SimpleLambdaExpressionSyntax simpleLambda
                && simpleLambda.Body is ExpressionSyntax bodyExpr
            )
            {
                var simplifiedBody = simplifyObjectCreations
                    ? SimplifyTernaryNullChecks(bodyExpr)
                    : SimplifySimpleTernaryNullChecks(bodyExpr);
                if (simplifiedBody != bodyExpr)
                {
                    hasChanges = true;
                }
                var newLambda = simpleLambda.WithBody(simplifiedBody);
                newArguments.Add(argument.WithExpression(newLambda));
            }
            else if (
                argument.Expression is ParenthesizedLambdaExpressionSyntax parenLambda
                && parenLambda.Body is ExpressionSyntax parenBodyExpr
            )
            {
                var simplifiedBody = simplifyObjectCreations
                    ? SimplifyTernaryNullChecks(parenBodyExpr)
                    : SimplifySimpleTernaryNullChecks(parenBodyExpr);
                if (simplifiedBody != parenBodyExpr)
                {
                    hasChanges = true;
                }
                var newLambda = parenLambda.WithBody(simplifiedBody);
                newArguments.Add(argument.WithExpression(newLambda));
            }
            else
            {
                newArguments.Add(argument);
            }
        }

        // If no changes were made, return the original invocation to preserve trivia
        if (!hasChanges)
        {
            return invocation;
        }

        // Create a new argument list preserving the original tokens' trivia
        var originalArgumentList = invocation.ArgumentList;
        var newArgumentList = SyntaxFactory.ArgumentList(
            originalArgumentList.OpenParenToken,
            SyntaxFactory.SeparatedList(
                newArguments,
                originalArgumentList.Arguments.GetSeparators()
            ),
            originalArgumentList.CloseParenToken
        );

        return invocation.WithArgumentList(newArgumentList);
    }

    private class TernaryNullCheckRewriter : CSharpSyntaxRewriter
    {
        private readonly bool _simplifyObjectCreations;

        public TernaryNullCheckRewriter(bool simplifyObjectCreations)
        {
            _simplifyObjectCreations = simplifyObjectCreations;
        }

        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            // First, visit children to handle nested ternaries
            node = (ConditionalExpressionSyntax)base.VisitConditionalExpression(node)!;

            // Try to simplify this ternary if it's a null check
            var simplified = TrySimplifyTernaryNullCheck(node);
            return simplified ?? node;
        }

        private ExpressionSyntax? TrySimplifyTernaryNullCheck(ConditionalExpressionSyntax ternary)
        {
            // Check if the condition is a null check (simple or complex with &&)
            var nullChecks = NullConditionalHelper.ExtractNullChecks(ternary.Condition);
            if (nullChecks.Count == 0)
                return null;

            // Extract both expressions, removing any type casts
            var whenTrueExpr = NullConditionalHelper.RemoveNullableCast(ternary.WhenTrue);
            var whenFalseExpr = NullConditionalHelper.RemoveNullableCast(ternary.WhenFalse);

            // Check if one branch is null and the other contains an object creation
            var whenTrueIsNull = NullConditionalHelper.IsNullOrNullCast(ternary.WhenTrue);
            var whenFalseIsNull = NullConditionalHelper.IsNullOrNullCast(ternary.WhenFalse);
            var whenTrueHasObject = IsObjectCreation(whenTrueExpr);
            var whenFalseHasObject = IsObjectCreation(whenFalseExpr);

            // Handle object creation patterns (previously LQRS004):
            // condition ? new{} : null  OR  condition ? null : new{}
            // These are only simplified when _simplifyObjectCreations is true
            if ((whenTrueIsNull && whenFalseHasObject) || (whenFalseIsNull && whenTrueHasObject))
            {
                // Skip object creation simplification if not enabled
                if (!_simplifyObjectCreations)
                    return null;

                var objectExpr = whenFalseIsNull ? whenTrueExpr : whenFalseExpr;
                var effectiveNullChecks = whenTrueIsNull
                    ? InvertNullChecks(nullChecks)
                    : nullChecks;

                // Convert the object creation to use null-conditional operators inside
                var converted = ConvertObjectCreationToNullConditional(
                    objectExpr,
                    effectiveNullChecks
                );
                if (converted == null)
                    return null;

                // Preserve trivia from the conditional expression
                return TriviaHelper.PreserveTrivia(ternary, converted);
            }

            // Only proceed if the else clause is null (standard pattern)
            if (!whenFalseIsNull)
                return null;

            // Build the null-conditional chain for the WhenTrue expression
            var nullConditionalExpr = NullConditionalHelper.BuildNullConditionalChain(
                whenTrueExpr,
                nullChecks
            );
            if (nullConditionalExpr == null)
                return null;

            // Preserve leading trivia from the ternary (e.g., indentation)
            // but remove trailing trivia (which might contain newlines before the comma)
            nullConditionalExpr = nullConditionalExpr
                .WithLeadingTrivia(ternary.GetLeadingTrivia())
                .WithTrailingTrivia(SyntaxTriviaList.Empty);

            // If the else clause is not simply null but a collection initializer, use ?? operator
            if (IsEmptyCollectionInitializer(ternary.WhenFalse))
            {
                return SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    nullConditionalExpr,
                    ternary.WhenFalse
                );
            }

            return nullConditionalExpr;
        }

        /// <summary>
        /// Handles inverted null check conditions (e.g., p.Child == null ? null : expr).
        /// This method intentionally returns the same list because the null-checked paths
        /// are the same regardless of whether the condition is "!= null" or "== null".
        /// The only difference is which branch has the object/expression, which is handled
        /// by the caller when determining effectiveNullChecks vs objectExpr.
        /// </summary>
        private static List<ExpressionSyntax> InvertNullChecks(List<ExpressionSyntax> nullChecks)
        {
            return nullChecks;
        }

        private static ExpressionSyntax? ConvertObjectCreationToNullConditional(
            ExpressionSyntax objectCreation,
            List<ExpressionSyntax> nullChecks
        )
        {
            // Create a rewriter to replace member accesses with null-conditional versions
            var rewriter = new ObjectCreationNullConditionalRewriter(nullChecks);
            return (ExpressionSyntax)rewriter.Visit(objectCreation);
        }

        /// <summary>
        /// Rewriter that converts member accesses inside object creations to use null-conditional operators.
        /// </summary>
        private class ObjectCreationNullConditionalRewriter : CSharpSyntaxRewriter
        {
            private readonly HashSet<string> _nullCheckedPaths;

            public ObjectCreationNullConditionalRewriter(List<ExpressionSyntax> nullChecks)
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
                    var newExpression = (ExpressionSyntax)Visit(initializer.Expression)!;

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
                var newSeparatedList = SyntaxFactory.SeparatedList(
                    newInitializers,
                    originalSeparators
                );

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
                    var visitedExpression = (ExpressionSyntax)Visit(expression)!;

                    // Preserve the trivia from the original expression
                    var newExpression = visitedExpression
                        .WithLeadingTrivia(expression.GetLeadingTrivia())
                        .WithTrailingTrivia(expression.GetTrailingTrivia());

                    newExpressions.Add(newExpression);
                }

                // Preserve the original separators (commas with their trivia)
                var originalSeparators = node.Initializer.Expressions.GetSeparators().ToList();
                var newSeparatedList = SyntaxFactory.SeparatedList(
                    newExpressions,
                    originalSeparators
                );

                // Create new initializer preserving brace trivia
                var newInitializer = SyntaxFactory.InitializerExpression(
                    node.Initializer.Kind(),
                    node.Initializer.OpenBraceToken,
                    newSeparatedList,
                    node.Initializer.CloseBraceToken
                );

                // Create new object creation with the new initializer
                return node.WithInitializer(newInitializer);
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
                    return nested.WithWhenNotNull(
                        AppendMemberBinding(nested.WhenNotNull, memberName)
                    );
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

        private static bool IsEmptyCollectionInitializer(ExpressionSyntax expr)
        {
            // Check for [] pattern (collection expression)
            if (expr is CollectionExpressionSyntax collection && collection.Elements.Count == 0)
                return true;

            // Check for Array.Empty<T>() or similar patterns
            // This can be extended as needed

            return false;
        }

        private static bool IsObjectCreation(ExpressionSyntax expr)
        {
            // Check if the expression is an object creation (named or anonymous)
            return expr is ObjectCreationExpressionSyntax
                || expr is AnonymousObjectCreationExpressionSyntax;
        }
    }
}
