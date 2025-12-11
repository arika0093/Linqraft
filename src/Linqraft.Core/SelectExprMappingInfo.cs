using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Information about a method marked with [LinqraftMappingGenerate] attribute
/// </summary>
public record SelectExprMappingInfo
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
}
