using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Equatable model representing the essential identifying information for a SelectExpr invocation.
/// This model contains only value types and strings, ensuring proper equality comparison for incremental generator caching.
/// </summary>
/// <remarks>
/// Following the incremental generators cookbook pattern:
/// - No ISymbol or SemanticModel references (they are never equatable between runs)
/// - No SyntaxNode references (edits make them non-equatable)
/// - Only string representations that uniquely identify the invocation
/// </remarks>
public sealed record SelectExprInvocationModel : IEquatable<SelectExprInvocationModel>
{
    /// <summary>
    /// A hash of the lambda body text to detect changes
    /// </summary>
    public required string LambdaBodyHash { get; init; }

    /// <summary>
    /// The namespace where the SelectExpr is invoked
    /// </summary>
    public required string CallerNamespace { get; init; }

    /// <summary>
    /// The file path where the invocation occurs
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The span start position in the file
    /// </summary>
    public required int SpanStart { get; init; }

    /// <summary>
    /// The span length in the file
    /// </summary>
    public required int SpanLength { get; init; }

    /// <summary>
    /// The type of SelectExpr pattern (Anonymous, ExplicitDto, Named)
    /// </summary>
    public required string PatternType { get; init; }

    /// <summary>
    /// For ExplicitDto pattern, the explicit DTO name
    /// </summary>
    public string? ExplicitDtoName { get; init; }

    /// <summary>
    /// The source type full name
    /// </summary>
    public required string SourceTypeFullName { get; init; }

    /// <summary>
    /// Whether this has a capture argument
    /// </summary>
    public required bool HasCapture { get; init; }

    /// <summary>
    /// A hash of the capture argument expression (if any)
    /// </summary>
    public string? CaptureExpressionHash { get; init; }
}

/// <summary>
/// Equatable model representing the essential information extracted from a LinqraftMappingGenerate attributed method.
/// This model contains only value types and strings, ensuring proper equality comparison for incremental generator caching.
/// </summary>
public sealed record MappingMethodModel : IEquatable<MappingMethodModel>
{
    /// <summary>
    /// The name of the method to generate (from attribute parameter)
    /// </summary>
    public required string TargetMethodName { get; init; }

    /// <summary>
    /// The fully qualified name of the containing class
    /// </summary>
    public required string ContainingClassFullName { get; init; }

    /// <summary>
    /// The simple name of the containing class
    /// </summary>
    public required string ContainingClassName { get; init; }

    /// <summary>
    /// The namespace where the class is defined
    /// </summary>
    public required string ContainingNamespace { get; init; }

    /// <summary>
    /// Whether the containing class is static
    /// </summary>
    public required bool IsContainingClassStatic { get; init; }

    /// <summary>
    /// The accessibility of the containing class
    /// </summary>
    public required string ContainingClassAccessibility { get; init; }

    /// <summary>
    /// A hash of the method body to detect changes
    /// </summary>
    public required string MethodBodyHash { get; init; }

    /// <summary>
    /// The file path where the method is declared
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The span start position for identification
    /// </summary>
    public required int SpanStart { get; init; }
}

/// <summary>
/// Equatable model representing the essential information extracted from a LinqraftMappingDeclare class.
/// This model contains only value types and strings, ensuring proper equality comparison for incremental generator caching.
/// </summary>
public sealed record MappingDeclareModel : IEquatable<MappingDeclareModel>
{
    /// <summary>
    /// The fully qualified name of the containing class
    /// </summary>
    public required string ContainingClassFullName { get; init; }

    /// <summary>
    /// The simple name of the containing class
    /// </summary>
    public required string ContainingClassName { get; init; }

    /// <summary>
    /// The namespace where the class is defined
    /// </summary>
    public required string ContainingNamespace { get; init; }

    /// <summary>
    /// The fully qualified name of the source type T from LinqraftMappingDeclare&lt;T&gt;
    /// </summary>
    public required string SourceTypeFullName { get; init; }

    /// <summary>
    /// The simple name of the source type
    /// </summary>
    public required string SourceTypeName { get; init; }

    /// <summary>
    /// Optional custom method name from [LinqraftMappingGenerate] attribute at class level
    /// </summary>
    public required string? CustomMethodName { get; init; }

    /// <summary>
    /// The accessibility of the containing class
    /// </summary>
    public required string ContainingClassAccessibility { get; init; }

    /// <summary>
    /// A hash of the DefineMapping method body to detect changes
    /// </summary>
    public required string DefineMappingMethodBodyHash { get; init; }

    /// <summary>
    /// The file path where the class is declared
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The span start position for identification
    /// </summary>
    public required int SpanStart { get; init; }
}

/// <summary>
/// Extended context that pairs an equatable model with the syntax needed for generation.
/// This is used internally during generation but the model part is used for equality/caching.
/// </summary>
/// <typeparam name="TModel">The equatable model type</typeparam>
/// <typeparam name="TSyntax">The syntax type</typeparam>
internal sealed record GenerationContext<TModel, TSyntax>
    where TModel : IEquatable<TModel>
    where TSyntax : SyntaxNode
{
    public required TModel Model { get; init; }
    public required TSyntax Syntax { get; init; }
    public required SemanticModel SemanticModel { get; init; }
}
