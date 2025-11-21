using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Helper class for converting ternary null checks to null-conditional operators
/// </summary>
internal static class NullConditionalHelper
{
    /// <summary>
    /// Converts ternary null checks like "a != null ? a.B : null" to "a?.B"
    /// Also handles complex cases like "a != null && a.B != null ? a.B.C : null" to "a?.B?.C"
    /// </summary>
    public static ExpressionSyntax SimplifyNullChecks(ExpressionSyntax expression)
    {
        if (expression is ConditionalExpressionSyntax conditional)
        {
            return SimplifyConditionalExpression(conditional);
        }

        // Recursively simplify nested expressions
        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax anonymous => SimplifyAnonymousObject(anonymous),
            ObjectCreationExpressionSyntax objectCreation => SimplifyObjectCreation(objectCreation),
            _ => expression
        };
    }

    private static ExpressionSyntax SimplifyConditionalExpression(ConditionalExpressionSyntax conditional)
    {
        // Check if it's a null check pattern: condition ? whenTrue : null
        if (conditional.WhenFalse is LiteralExpressionSyntax literal 
            && literal.Kind() == SyntaxKind.NullLiteralExpression)
        {
            // Try to extract the null check pattern
            var nullConditional = TryConvertToNullConditional(conditional.Condition, conditional.WhenTrue);
            if (nullConditional != null)
            {
                return nullConditional;
            }
        }

        return conditional;
    }

    private static ExpressionSyntax? TryConvertToNullConditional(
        ExpressionSyntax condition,
        ExpressionSyntax whenTrue)
    {
        // Pattern: a != null ? a.B : null => a?.B
        if (condition is BinaryExpressionSyntax binary && binary.Kind() == SyntaxKind.NotEqualsExpression)
        {
            if (binary.Right is LiteralExpressionSyntax rightLiteral 
                && rightLiteral.Kind() == SyntaxKind.NullLiteralExpression)
            {
                var checkedExpression = binary.Left;
                return TryBuildNullConditional(checkedExpression, whenTrue);
            }
        }

        // Pattern: a != null && b != null ? a.B.C : null => a?.B?.C
        if (condition is BinaryExpressionSyntax andCondition && andCondition.Kind() == SyntaxKind.LogicalAndExpression)
        {
            var checks = ExtractNullChecks(andCondition);
            if (checks.Count > 0)
            {
                return TryBuildChainedNullConditional(checks, whenTrue);
            }
        }

        return null;
    }

    private static ExpressionSyntax? TryBuildNullConditional(
        ExpressionSyntax checkedExpression,
        ExpressionSyntax whenTrue)
    {
        // Check if whenTrue starts with the same expression
        var whenTrueStr = whenTrue.ToString();
        var checkedStr = checkedExpression.ToString();

        if (whenTrueStr.StartsWith(checkedStr))
        {
            // Replace the first occurrence with null conditional
            var remaining = whenTrueStr.Substring(checkedStr.Length);
            if (remaining.StartsWith("."))
            {
                var nullConditionalStr = checkedStr + "?" + remaining;
                return SyntaxFactory.ParseExpression(nullConditionalStr);
            }
        }

        return null;
    }

    private static ExpressionSyntax? TryBuildChainedNullConditional(
        List<string> checks,
        ExpressionSyntax whenTrue)
    {
        var whenTrueStr = whenTrue.ToString();

        // Build the null-conditional chain
        // For checks like ["a", "a.B"], whenTrue like "a.B.C"
        // Result should be "a?.B?.C"
        
        // Sort checks by length (shortest first) to build the chain correctly
        checks.Sort((a, b) => a.Length.CompareTo(b.Length));

        var result = whenTrueStr;
        
        // Replace each check with null-conditional
        foreach (var check in checks)
        {
            if (result.StartsWith(check + "."))
            {
                result = check + "?." + result.Substring(check.Length + 1);
            }
        }

        if (result != whenTrueStr)
        {
            return SyntaxFactory.ParseExpression(result);
        }

        return null;
    }

    private static List<string> ExtractNullChecks(BinaryExpressionSyntax andExpression)
    {
        var checks = new List<string>();

        void ExtractChecks(ExpressionSyntax expr)
        {
            if (expr is BinaryExpressionSyntax binary)
            {
                if (binary.Kind() == SyntaxKind.LogicalAndExpression)
                {
                    ExtractChecks(binary.Left);
                    ExtractChecks(binary.Right);
                }
                else if (binary.Kind() == SyntaxKind.NotEqualsExpression)
                {
                    if (binary.Right is LiteralExpressionSyntax literal 
                        && literal.Kind() == SyntaxKind.NullLiteralExpression)
                    {
                        checks.Add(binary.Left.ToString());
                    }
                }
            }
        }

        ExtractChecks(andExpression);
        return checks;
    }

    private static AnonymousObjectCreationExpressionSyntax SimplifyAnonymousObject(
        AnonymousObjectCreationExpressionSyntax anonymous)
    {
        var simplifiedInitializers = new List<AnonymousObjectMemberDeclaratorSyntax>();

        foreach (var initializer in anonymous.Initializers)
        {
            var simplifiedExpression = SimplifyNullChecks(initializer.Expression);
            
            var newInitializer = initializer.NameEquals != null
                ? SyntaxFactory.AnonymousObjectMemberDeclarator(
                    initializer.NameEquals,
                    simplifiedExpression)
                : SyntaxFactory.AnonymousObjectMemberDeclarator(simplifiedExpression);

            simplifiedInitializers.Add(newInitializer);
        }

        return SyntaxFactory.AnonymousObjectCreationExpression(
            SyntaxFactory.SeparatedList(simplifiedInitializers)
        );
    }

    private static ObjectCreationExpressionSyntax SimplifyObjectCreation(
        ObjectCreationExpressionSyntax objectCreation)
    {
        if (objectCreation.Initializer == null)
        {
            return objectCreation;
        }

        var simplifiedExpressions = new List<ExpressionSyntax>();

        foreach (var expression in objectCreation.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment)
            {
                var simplifiedRight = SimplifyNullChecks(assignment.Right);
                simplifiedExpressions.Add(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        assignment.Left,
                        simplifiedRight
                    )
                );
            }
            else
            {
                simplifiedExpressions.Add(SimplifyNullChecks(expression));
            }
        }

        var newInitializer = SyntaxFactory.InitializerExpression(
            objectCreation.Initializer.Kind(),
            SyntaxFactory.SeparatedList(simplifiedExpressions)
        );

        return objectCreation.WithInitializer(newInitializer);
    }
}
