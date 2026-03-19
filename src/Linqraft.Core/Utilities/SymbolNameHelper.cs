using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Utilities;

internal static class SymbolNameHelper
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat = SymbolDisplayFormat
        .FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.ExpandNullable
        );

    public static string ToFullyQualifiedTypeName(this ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(FullyQualifiedFormat);
    }

    public static string GetNamespace(INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        return namespaceSymbol.ToDisplayString();
    }

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

    public static bool IsQueryable(ITypeSymbol? symbol)
    {
        return ImplementsGenericInterface(symbol, "System.Linq.IQueryable<T>");
    }

    public static bool IsEnumerable(ITypeSymbol? symbol)
    {
        return ImplementsGenericInterface(symbol, "System.Collections.Generic.IEnumerable<T>");
    }

    public static bool IsCompilerConstant(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol local => local.IsConst,
            IFieldSymbol field => field.IsConst,
            _ => false,
        };
    }

    public static string SanitizeHintName(string value)
    {
        var characters = value
            .Select(current => char.IsLetterOrDigit(current) ? current : '_')
            .ToArray();
        return new string(characters);
    }

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
