using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Utilities;

/// <summary>
/// Provides helpers for formatting and classifying Roslyn symbols.
/// </summary>
internal static class SymbolNameHelper
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = SymbolDisplayFormat
        .FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.ExpandNullable
        );

    /// <summary>
    /// Formats a type symbol as a fully qualified type name.
    /// </summary>
    public static string ToFullyQualifiedTypeName(this ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(FullyQualifiedFormat);
    }

    /// <summary>
    /// Gets the display name of a namespace, or an empty string for the global namespace.
    /// </summary>
    public static string GetNamespace(INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        return namespaceSymbol.ToDisplayString();
    }

    /// <summary>
    /// Gets the C# accessibility keyword for a Roslyn accessibility value.
    /// </summary>
    public static string GetAccessibilityKeyword(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };
    }

    /// <summary>
    /// Determines whether the symbol is a queryable.
    /// </summary>
    public static bool IsQueryable(ITypeSymbol? symbol)
    {
        return ImplementsGenericInterface(symbol, "System.Linq.IQueryable<T>");
    }

    /// <summary>
    /// Determines whether the symbol is an enumerable.
    /// </summary>
    public static bool IsEnumerable(ITypeSymbol? symbol)
    {
        return ImplementsGenericInterface(symbol, "System.Collections.Generic.IEnumerable<T>");
    }

    /// <summary>
    /// Determines whether the symbol is a compiler constant.
    /// </summary>
    public static bool IsCompilerConstant(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol local => local.IsConst,
            IFieldSymbol field => field.IsConst,
            _ => false,
        };
    }

    /// <summary>
    /// Sanitizes a hint name so it is safe to use in generated file names.
    /// </summary>
    public static string SanitizeHintName(string value)
    {
        var characters = value
            .Select(current => char.IsLetterOrDigit(current) ? current : '_')
            .ToArray();
        return new string(characters);
    }

    /// <summary>
    /// Determines whether the symbol implements the specified generic interface.
    /// </summary>
    private static bool ImplementsGenericInterface(ITypeSymbol? symbol, string metadataName)
    {
        if (symbol is null)
        {
            return false;
        }

        if (
            symbol is INamedTypeSymbol namedType
            && namedType.ConstructedFrom.ToDisplayString() == metadataName
        )
        {
            return true;
        }

        return symbol.AllInterfaces.Any(current =>
            current.ConstructedFrom.ToDisplayString() == metadataName
        );
    }
}
