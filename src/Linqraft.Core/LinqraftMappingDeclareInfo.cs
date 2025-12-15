using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Information about a class that inherits from LinqraftMappingDeclare&lt;T&gt;
/// </summary>
public record LinqraftMappingDeclareInfo
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
}
