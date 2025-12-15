using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.RoslynHelpers;
using Linqraft.Core.SyntaxHelpers;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Generator for null check expressions.
/// Converts null-conditional expressions (?.) to explicit null checks.
/// </summary>
internal class NullCheckGenerator
{
    private readonly SemanticModel _semanticModel;
    private readonly LinqraftConfiguration _configuration;

    /// <summary>
    /// Creates a new null check generator.
    /// </summary>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="configuration">The Linqraft configuration</param>
    public NullCheckGenerator(SemanticModel semanticModel, LinqraftConfiguration configuration)
    {
        _semanticModel = semanticModel;
        _configuration = configuration;
    }

    /// <summary>
    /// Converts a null-conditional access expression to an explicit null check.
    /// Example: c.Child?.Id → c.Child != null ? c.Child.Id : default(int?)
    /// </summary>
    /// <param name="syntax">The expression syntax containing null-conditional access</param>
    /// <param name="typeSymbol">The type of the expression result</param>
    /// <returns>The converted expression with explicit null check</returns>
    public string ConvertToExplicitNullCheck(ExpressionSyntax syntax, ITypeSymbol typeSymbol)
    {
        // Use Roslyn to verify this uses conditional access
        var hasConditionalAccess = syntax
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any();

        if (!hasConditionalAccess)
            return syntax.ToString();

        var expression = syntax.ToString();

        // Remove comments from syntax before processing
        var cleanSyntax = RemoveComments(syntax);
        var cleanExpression = cleanSyntax.ToString();

        // Build the access path without ?. operators
        var accessPath = cleanExpression.Replace("?.", ".");

        // Build null checks using string manipulation
        var checks = new List<string>();
        var parts = cleanExpression.Split(["?."], StringSplitOptions.None);

        if (parts.Length < 2)
            return expression;

        // All parts except the first require null checks
        var currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            checks.Add($"{currentPath} != null");
            var nextPart = parts[i];
            var dotIndex = nextPart.IndexOf('.');
            var propertyName = dotIndex > 0 ? nextPart[..dotIndex] : nextPart;
            currentPath = $"{currentPath}.{propertyName}";
        }

        if (checks.Count == 0)
            return expression;

        // Build null checks
        var nullCheckPart = string.Join(" && ", checks);

        // Check if expression contains Select or SelectMany invocation
        var hasSelectOrSelectMany =
            RoslynTypeHelper.ContainsSelectInvocation(syntax)
            || RoslynTypeHelper.ContainsSelectManyInvocation(syntax);

        // Determine the default value
        string defaultValue;

        if (
            _configuration.ArrayNullabilityRemoval
            && hasSelectOrSelectMany
            && RoslynTypeHelper.IsCollectionType(typeSymbol)
        )
        {
            defaultValue = GetEmptyCollectionExpressionForType(typeSymbol, cleanExpression);
        }
        else
        {
            defaultValue = GetDefaultValueForType(typeSymbol);
        }

        return $"{nullCheckPart} ? {accessPath} : {defaultValue}";
    }

    /// <summary>
    /// Checks if an expression needs null check conversion.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <param name="isNullable">Whether the property is nullable</param>
    /// <param name="typeSymbol">The type symbol of the expression</param>
    /// <returns>True if the expression needs null check conversion</returns>
    public bool NeedsNullCheckConversion(ExpressionSyntax expression, bool isNullable, ITypeSymbol typeSymbol)
    {
        var hasConditionalAccess = expression
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any();

        var hasSelectOrSelectMany =
            RoslynTypeHelper.ContainsSelectInvocation(expression)
            || RoslynTypeHelper.ContainsSelectManyInvocation(expression);

        var isCollectionWithSelect =
            _configuration.ArrayNullabilityRemoval
            && hasSelectOrSelectMany
            && RoslynTypeHelper.IsCollectionType(typeSymbol);

        return hasConditionalAccess && (isNullable || isCollectionWithSelect);
    }

    /// <summary>
    /// Gets the default value for a type symbol.
    /// </summary>
    /// <param name="typeSymbol">The type symbol</param>
    /// <returns>The default value as a string</returns>
    public static string GetDefaultValueForType(ITypeSymbol typeSymbol)
    {
        var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (RoslynTypeHelper.IsNullableType(typeSymbol))
        {
            if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return $"default({typeName})";
            }
            return "null";
        }
        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Boolean => "false",
            SpecialType.System_Char => "'\\0'",
            SpecialType.System_String => "string.Empty",
            _ => "default",
        };
    }

    /// <summary>
    /// Gets the empty collection expression for a collection type.
    /// </summary>
    private static string GetEmptyCollectionExpressionForType(ITypeSymbol typeSymbol, string expressionText)
    {
        return CollectionHelper.GetEmptyCollectionExpressionForType(typeSymbol, expressionText);
    }

    /// <summary>
    /// Removes comments from a syntax node while preserving other trivia (whitespace, etc).
    /// </summary>
    private static T RemoveComments<T>(T node) where T : SyntaxNode
    {
        return (T)node.ReplaceTrivia(
            node.DescendantTrivia(descendIntoTrivia: true),
            (originalTrivia, _) =>
            {
                // Remove single-line and multi-line comments
                if (
                    originalTrivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    || originalTrivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                )
                {
                    return default;
                }
                return originalTrivia;
            }
        );
    }
}
