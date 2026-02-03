using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Information about a method marked with [LinqraftMappingGenerate] attribute.
/// </summary>
/// <remarks>
/// This record implements custom equality comparison that excludes non-equatable Roslyn types
/// (SemanticModel, ISymbol, SyntaxNode) to support incremental generator caching.
/// </remarks>
public record SelectExprMappingInfo : IEquatable<SelectExprMappingInfo>
{
    /// <summary>
    /// The method declaration with the attribute
    /// </summary>
    public required MethodDeclarationSyntax MethodDeclaration { get; init; }

    /// <summary>
    /// The name of the method to generate (from attribute parameter)
    /// </summary>
    public required string TargetMethodName { get; init; }

    /// <summary>
    /// The containing class (must be static partial)
    /// </summary>
    public required INamedTypeSymbol ContainingClass { get; init; }

    /// <summary>
    /// The semantic model for this method
    /// </summary>
    public required SemanticModel SemanticModel { get; init; }

    /// <summary>
    /// The namespace where the class is defined
    /// </summary>
    public required string ContainingNamespace { get; init; }

    #region Custom Equality for Incremental Generator Caching

    /// <summary>
    /// Gets a unique identifier for equality comparison.
    /// </summary>
    private string GetEquatableIdentifier()
    {
        var filePath = MethodDeclaration?.GetLocation()?.SourceTree?.FilePath ?? "";
        var spanStart = MethodDeclaration?.SpanStart ?? 0;
        var containingClassName = ContainingClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
        var methodBodyHash = HashUtility.GenerateSha256Hash(MethodDeclaration?.Body?.ToFullString() ?? MethodDeclaration?.ExpressionBody?.ToFullString() ?? "");

        return $"{filePath}|{spanStart}|{TargetMethodName}|{containingClassName}|{ContainingNamespace}|{methodBodyHash}";
    }

    /// <summary>
    /// Determines whether the specified SelectExprMappingInfo is equal to this instance.
    /// </summary>
    public virtual bool Equals(SelectExprMappingInfo? other)
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
