using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Information about a class that inherits from LinqraftMappingDeclare&lt;T&gt;.
/// </summary>
/// <remarks>
/// This record implements custom equality comparison that excludes non-equatable Roslyn types
/// (SemanticModel, ISymbol, SyntaxNode) to support incremental generator caching.
/// </remarks>
public record LinqraftMappingDeclareInfo : IEquatable<LinqraftMappingDeclareInfo>
{
    /// <summary>
    /// The class declaration that inherits from LinqraftMappingDeclare
    /// </summary>
    public required ClassDeclarationSyntax ClassDeclaration { get; init; }

    /// <summary>
    /// The DefineMapping method declaration
    /// </summary>
    public required MethodDeclarationSyntax DefineMappingMethod { get; init; }

    /// <summary>
    /// The containing class symbol
    /// </summary>
    public required INamedTypeSymbol ContainingClass { get; init; }

    /// <summary>
    /// The semantic model for this class
    /// </summary>
    public required SemanticModel SemanticModel { get; init; }

    /// <summary>
    /// The namespace where the class is defined
    /// </summary>
    public required string ContainingNamespace { get; init; }

    /// <summary>
    /// The type parameter T from LinqraftMappingDeclare&lt;T&gt;
    /// </summary>
    public required ITypeSymbol SourceType { get; init; }

    /// <summary>
    /// Optional custom method name from [LinqraftMappingGenerate] attribute at class level
    /// If null, uses default naming convention: ProjectTo{EntityName}
    /// </summary>
    public string? CustomMethodName { get; init; }

    #region Custom Equality for Incremental Generator Caching

    /// <summary>
    /// Gets a unique identifier for equality comparison.
    /// </summary>
    private string GetEquatableIdentifier()
    {
        var filePath = ClassDeclaration?.GetLocation()?.SourceTree?.FilePath ?? "";
        var spanStart = ClassDeclaration?.SpanStart ?? 0;
        var containingClassName =
            ContainingClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
        var sourceTypeName =
            SourceType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
        var methodBodyHash = HashUtility.GenerateSha256Hash(
            DefineMappingMethod?.Body?.ToFullString()
                ?? DefineMappingMethod?.ExpressionBody?.ToFullString()
                ?? ""
        );

        return $"{filePath}|{spanStart}|{containingClassName}|{ContainingNamespace}|{sourceTypeName}|{CustomMethodName}|{methodBodyHash}";
    }

    /// <summary>
    /// Determines whether the specified LinqraftMappingDeclareInfo is equal to this instance.
    /// </summary>
    public virtual bool Equals(LinqraftMappingDeclareInfo? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return GetEquatableIdentifier() == other.GetEquatableIdentifier();
    }

    /// <summary>
    /// Returns a hash code based on the equatable identifier.
    /// </summary>
    public override int GetHashCode()
    {
        return GetEquatableIdentifier().GetHashCode();
    }

    #endregion
}
