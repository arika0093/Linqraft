using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.RoslynHelpers;

/// <summary>
/// Type checking helper using Roslyn semantic analysis.
/// Provides more accurate and robust type checking by replacing string-based type comparisons.
/// </summary>
public static class RoslynTypeHelper
{
    /// <summary>
    /// Determines whether a type is a nullable type.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to check</param>
    /// <returns>True if the type is nullable</returns>
    public static bool IsNullableType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // Nullable<T> (nullable value type)
        if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return true;

        // C# 8.0+ nullable reference types
        return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
    }

    /// <summary>
    /// Determines whether a type is nullable based on the type name string (legacy method).
    /// Use IsNullableType(ITypeSymbol) when possible.
    /// </summary>
    /// <param name="typeName">The type name string</param>
    /// <returns>True if the type name ends with "?"</returns>
    public static bool IsNullableTypeByString(string typeName)
    {
        return !string.IsNullOrEmpty(typeName) && typeName.EndsWith("?");
    }

    /// <summary>
    /// Removes the nullable modifier from a type.
    /// </summary>
    /// <param name="typeSymbol">The type symbol</param>
    /// <returns>The non-nullable type symbol</returns>
    public static ITypeSymbol? GetNonNullableType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
            return null;

        // For nullable reference types
        if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        // For Nullable<T> (value types), return the underlying type
        if (
            typeSymbol is INamedTypeSymbol namedType
            && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && namedType.TypeArguments.Length == 1
        )
        {
            return namedType.TypeArguments[0];
        }

        return typeSymbol;
    }

    /// <summary>
    /// Removes the "?" suffix from a type name string (legacy method).
    /// </summary>
    /// <param name="typeName">The type name string</param>
    /// <returns>The type name with "?" removed</returns>
    public static string RemoveNullableSuffixFromString(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        return typeName.TrimEnd('?');
    }

    /// <summary>
    /// Determines whether a type implements IQueryable&lt;T&gt;.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to check</param>
    /// <param name="compilation">The compilation</param>
    /// <returns>True if the type implements IQueryable&lt;T&gt;</returns>
    public static bool ImplementsIQueryable(ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol == null || compilation == null)
            return false;

        var iqueryableSymbol = compilation.GetTypeByMetadataName("System.Linq.IQueryable`1");
        if (iqueryableSymbol == null)
            return false;

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            // Check if it's directly IQueryable<T>
            if (SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, iqueryableSymbol))
                return true;

            // Scan interfaces
            return namedType.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, iqueryableSymbol)
            );
        }

        return false;
    }

    /// <summary>
    /// Determines whether a type implements IEnumerable&lt;T&gt;.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to check</param>
    /// <param name="compilation">The compilation</param>
    /// <returns>True if the type implements IEnumerable&lt;T&gt;</returns>
    public static bool ImplementsIEnumerable(ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol == null || compilation == null)
            return false;

        var ienumerableSymbol = compilation.GetTypeByMetadataName(
            "System.Collections.Generic.IEnumerable`1"
        );
        if (ienumerableSymbol == null)
            return false;

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, ienumerableSymbol))
                return true;

            return namedType.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, ienumerableSymbol)
            );
        }

        return false;
    }

    /// <summary>
    /// Safely gets a type argument from a generic type.
    /// </summary>
    /// <param name="typeSymbol">The type symbol</param>
    /// <param name="index">The index of the type argument (default: 0)</param>
    /// <returns>The type argument, or null if it doesn't exist</returns>
    public static ITypeSymbol? GetGenericTypeArgument(ITypeSymbol typeSymbol, int index = 0)
    {
        if (
            typeSymbol is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.TypeArguments.Length > index
        )
        {
            return namedType.TypeArguments[index];
        }

        return null;
    }

    /// <summary>
    /// Determines whether a type is an anonymous type.
    /// </summary>
    /// <param name="typeSymbol">The type symbol</param>
    /// <returns>True if the type is an anonymous type</returns>
    public static bool IsAnonymousType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.IsAnonymousType ?? false;
    }

    /// <summary>
    /// Determines whether a namespace is the global namespace.
    /// </summary>
    /// <param name="namespaceSymbol">The namespace symbol</param>
    /// <returns>True if the namespace is the global namespace</returns>
    public static bool IsGlobalNamespace(INamespaceSymbol? namespaceSymbol)
    {
        return namespaceSymbol?.IsGlobalNamespace ?? false;
    }

    /// <summary>
    /// Determines whether an expression contains a Select method invocation.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression contains a Select method invocation</returns>
    public static bool ContainsSelectInvocation(ExpressionSyntax expression)
    {
        if (expression == null)
            return false;

        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv =>
                inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "Select"
            );
    }

    /// <summary>
    /// Determines whether an expression contains a SelectMany method invocation.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression contains a SelectMany method invocation</returns>
    public static bool ContainsSelectManyInvocation(ExpressionSyntax expression)
    {
        if (expression == null)
            return false;

        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv =>
                inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "SelectMany"
            );
    }

    /// <summary>
    /// Determines whether a type is generic based on the type name string (legacy - for backwards compatibility).
    /// Use INamedTypeSymbol.IsGenericType when possible.
    /// </summary>
    /// <param name="typeName">The type name string</param>
    /// <returns>True if the type name contains "&lt;"</returns>
    public static bool IsGenericTypeByString(string typeName)
    {
        return !string.IsNullOrEmpty(typeName) && typeName.Contains("<");
    }

    /// <summary>
    /// Determines whether a type name indicates an anonymous type (string-based - legacy).
    /// Use IsAnonymousType(ITypeSymbol) when possible.
    /// </summary>
    /// <param name="typeName">The type name string</param>
    /// <returns>True if the type name indicates an anonymous type</returns>
    public static bool IsAnonymousTypeByString(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;

        // Starts with "global::<anonymous" or contains "<>" and "AnonymousType"
        return typeName.StartsWith("global::<anonymous")
            || (
                typeName.Contains("<")
                && typeName.Contains(">")
                && typeName.Contains("AnonymousType")
            );
    }

    /// <summary>
    /// Determines whether an expression returns IQueryable&lt;T&gt;.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <param name="semanticModel">The semantic model</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>True if the expression returns IQueryable&lt;T&gt;</returns>
    public static bool IsIQueryableExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken = default
    )
    {
        if (expression == null || semanticModel == null)
            return false;

        // Get the type of the expression
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.Type;

        if (type == null)
            return false;

        return ImplementsIQueryable(type, semanticModel.Compilation);
    }

    /// <summary>
    /// Gets the source type T from IQueryable&lt;T&gt;.
    /// </summary>
    /// <param name="expression">The IQueryable&lt;T&gt; expression</param>
    /// <param name="semanticModel">The semantic model</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The source type T, or null if it cannot be obtained</returns>
    public static ITypeSymbol? GetSourceTypeFromQueryable(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken = default
    )
    {
        if (expression == null || semanticModel == null)
            return null;

        // Get the type of the expression
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.Type;

        if (type == null)
            return null;

        // Check if it implements IQueryable<T>
        if (!ImplementsIQueryable(type, semanticModel.Compilation))
            return null;

        // Get the type argument T from IQueryable<T>
        if (type is INamedTypeSymbol namedType)
        {
            // Direct IQueryable<T>
            if (
                SymbolEqualityComparer.Default.Equals(
                    namedType.ConstructedFrom,
                    semanticModel.Compilation.GetTypeByMetadataName("System.Linq.IQueryable`1")
                )
            )
            {
                return GetGenericTypeArgument(namedType, 0);
            }

            // Find IQueryable<T> in interfaces
            var iqueryableInterface = namedType.AllInterfaces.FirstOrDefault(i =>
                SymbolEqualityComparer.Default.Equals(
                    i.ConstructedFrom,
                    semanticModel.Compilation.GetTypeByMetadataName("System.Linq.IQueryable`1")
                )
            );

            if (iqueryableInterface != null)
            {
                return GetGenericTypeArgument(iqueryableInterface, 0);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the namespace name from a SyntaxNode.
    /// </summary>
    /// <param name="node">The SyntaxNode</param>
    /// <returns>The namespace name, or empty string for global namespace</returns>
    public static string GetNamespaceFromSyntaxNode(SyntaxNode node)
    {
        if (node == null)
            return string.Empty;

        // Find the containing namespace or file-scoped namespace
        var namespaceDecl = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return namespaceDecl?.Name.ToString() ?? string.Empty;
    }
}
