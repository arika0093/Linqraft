using Microsoft.CodeAnalysis;
using Linqraft.Core.RoslynHelpers;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Helper for generating collection-related expressions.
/// </summary>
internal static class CollectionHelper
{
    /// <summary>
    /// Gets the empty collection expression based on the target type and chained methods.
    /// </summary>
    /// <param name="typeSymbol">The target type symbol</param>
    /// <param name="fullyQualifiedElementTypeName">The fully qualified element type name</param>
    /// <param name="chainedMethods">The chained methods string (e.g., ".ToList()")</param>
    /// <returns>The empty collection expression</returns>
    public static string GetEmptyCollectionExpression(
        ITypeSymbol? typeSymbol,
        string fullyQualifiedElementTypeName,
        string chainedMethods)
    {
        // Check if the target type is a List<T> (either explicitly or via ToList())
        var isListType = IsListType(typeSymbol) || chainedMethods.Contains(".ToList()");
        if (isListType)
        {
            return $"new global::System.Collections.Generic.List<{fullyQualifiedElementTypeName}>()";
        }

        // Check if the target type is an array (via ToArray())
        if (chainedMethods.Contains(".ToArray()"))
        {
            return $"global::System.Array.Empty<{fullyQualifiedElementTypeName}>()";
        }

        // Default to Enumerable.Empty for IEnumerable<T> types
        return $"global::System.Linq.Enumerable.Empty<{fullyQualifiedElementTypeName}>()";
    }

    /// <summary>
    /// Gets the appropriate empty collection expression for a collection type symbol.
    /// </summary>
    /// <param name="typeSymbol">The collection type symbol</param>
    /// <param name="expressionText">The original expression text to detect chained methods</param>
    /// <returns>The empty collection expression</returns>
    public static string GetEmptyCollectionExpressionForType(
        ITypeSymbol typeSymbol,
        string expressionText)
    {
        // Get the element type from the collection
        var nonNullableType = RoslynTypeHelper.GetNonNullableType(typeSymbol) ?? typeSymbol;
        var elementType = RoslynTypeHelper.GetGenericTypeArgument(nonNullableType, 0);
        var elementTypeName =
            elementType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object";

        // Detect chained methods from the expression text
        var chainedMethods = "";
        if (expressionText.Contains(".ToList()"))
        {
            chainedMethods = ".ToList()";
        }
        else if (expressionText.Contains(".ToArray()"))
        {
            chainedMethods = ".ToArray()";
        }

        return GetEmptyCollectionExpression(typeSymbol, elementTypeName, chainedMethods);
    }

    /// <summary>
    /// Checks if a type symbol represents a List type.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to check</param>
    /// <returns>True if the type is a List</returns>
    public static bool IsListType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        // Get the underlying type if it's nullable
        var nonNullableType = RoslynTypeHelper.GetNonNullableType(namedType) ?? namedType;
        if (nonNullableType is not INamedTypeSymbol nonNullableNamedType)
            return false;

        // Check if the type is List<T>
        var typeName = nonNullableNamedType.Name;
        var containingNamespace = nonNullableNamedType.ContainingNamespace?.ToDisplayString();
        return typeName == "List" && containingNamespace == "System.Collections.Generic";
    }
}
