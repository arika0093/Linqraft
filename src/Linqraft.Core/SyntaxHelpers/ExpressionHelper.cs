using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.SyntaxHelpers;

/// <summary>
/// Helper methods for working with expressions
/// </summary>
public static class ExpressionHelper
{
    /// <summary>
    /// Gets the property name from an expression
    /// </summary>
    /// <param name="expression">The expression to extract the property name from</param>
    /// <returns>The extracted property name, or null if unable to extract</returns>
    public static string? GetPropertyName(ExpressionSyntax expression)
    {
        return expression switch
        {
            // Get property name from member access (e.g., s.Id)
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            // Get property name from identifier (e.g., id)
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            // Handle conditional access (e.g., s.Child?.Property)
            ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding =>
                memberBinding.Name.Identifier.Text,
            _ => null,
        };
    }

    /// <summary>
    /// Gets the property name from an expression, with a default fallback value
    /// </summary>
    /// <param name="expression">The expression to extract the property name from</param>
    /// <param name="defaultName">The default name to return if unable to extract</param>
    /// <returns>The extracted property name, or the default name if unable to extract</returns>
    public static string GetPropertyNameOrDefault(
        ExpressionSyntax expression,
        string defaultName = "Property"
    )
    {
        return GetPropertyName(expression) ?? defaultName;
    }

    /// <summary>
    /// Finds an anonymous object creation expression within an expression tree
    /// </summary>
    /// <param name="expression">The expression to search</param>
    /// <returns>The anonymous object creation expression, or null if not found</returns>
    public static AnonymousObjectCreationExpressionSyntax? FindAnonymousObjectCreation(
        ExpressionSyntax expression
    )
    {
        // Check if the expression itself is an anonymous object creation
        if (expression is AnonymousObjectCreationExpressionSyntax anonymousObject)
        {
            return anonymousObject;
        }

        // Search descendants for anonymous object creation
        return expression
            .DescendantNodesAndSelf()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .FirstOrDefault();
    }

    /// <summary>
    /// Finds an object creation expression within an expression tree
    /// </summary>
    /// <param name="expression">The expression to search</param>
    /// <returns>The object creation expression, or null if not found</returns>
    public static ObjectCreationExpressionSyntax? FindObjectCreation(ExpressionSyntax expression)
    {
        // Check if the expression itself is an object creation
        if (expression is ObjectCreationExpressionSyntax objectCreation)
        {
            return objectCreation;
        }

        // Search descendants for object creation
        return expression
            .DescendantNodesAndSelf()
            .OfType<ObjectCreationExpressionSyntax>()
            .FirstOrDefault();
    }
}
