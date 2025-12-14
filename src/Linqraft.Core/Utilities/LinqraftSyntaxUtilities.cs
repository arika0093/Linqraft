using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.Pipeline;
using Linqraft.Core.Pipeline.Parsing;

namespace Linqraft.Core.Utilities;

/// <summary>
/// Public utilities for external libraries to use Linqraft's syntax analysis capabilities.
/// Note: Source Generator workflows cannot be directly accessed from external libraries.
/// These utilities provide syntax analysis and transformation helpers only.
/// </summary>
public static class LinqraftSyntaxUtilities
{
    /// <summary>
    /// Parses a lambda expression to extract anonymous type structure information.
    /// </summary>
    /// <param name="lambda">The lambda expression to parse</param>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <returns>The parsed anonymous type structure, or null if not an anonymous type</returns>
    public static AnonymousTypeStructure? ParseAnonymousType(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel)
    {
        var parser = new LambdaAnonymousTypeParser();
        var context = new PipelineContext
        {
            TargetNode = lambda,
            SemanticModel = semanticModel
        };
        var parsed = parser.Parse(context);

        if (parsed.LambdaBody == null)
            return null;

        var anonymousObject = parsed.LambdaBody as AnonymousObjectCreationExpressionSyntax;
        if (anonymousObject == null)
            return null;

        var properties = ExtractProperties(anonymousObject, semanticModel);

        return new AnonymousTypeStructure
        {
            Properties = properties,
            ParameterName = parsed.LambdaParameterName
        };
    }

    /// <summary>
    /// Infers property information from a projection expression.
    /// </summary>
    /// <param name="expression">The expression to analyze</param>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <returns>The inferred properties, or null if analysis fails</returns>
    public static IReadOnlyList<AnonymousTypeProperty>? InferPropertiesFromExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        // Get type info from the expression
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type == null)
            return null;

        // For anonymous types, extract properties
        if (typeInfo.Type.IsAnonymousType)
        {
            return typeInfo.Type.GetMembers()
                .OfType<IPropertySymbol>()
                .Select(p => new AnonymousTypeProperty
                {
                    Name = p.Name,
                    Expression = expression,
                    Type = p.Type
                })
                .ToList();
        }

        return null;
    }

    /// <summary>
    /// Checks if an expression contains null-conditional access operators.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression contains null-conditional access</returns>
    public static bool HasNullConditionalAccess(ExpressionSyntax expression)
    {
        return SyntaxHelpers.NullConditionalHelper.HasNullConditionalAccess(expression);
    }

    /// <summary>
    /// Checks if an expression is a null literal or a nullable cast to null.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression is null or a nullable cast to null</returns>
    public static bool IsNullOrNullCast(ExpressionSyntax expression)
    {
        return SyntaxHelpers.NullConditionalHelper.IsNullOrNullCast(expression);
    }

    /// <summary>
    /// Gets the lambda parameter name from a lambda expression.
    /// </summary>
    /// <param name="lambda">The lambda expression</param>
    /// <returns>The parameter name, or null if not found</returns>
    public static string? GetLambdaParameterName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count > 0
                => paren.ParameterList.Parameters[0].Identifier.Text,
            _ => null
        };
    }

    /// <summary>
    /// Gets the body expression from a lambda expression.
    /// </summary>
    /// <param name="lambda">The lambda expression</param>
    /// <returns>The body expression, or null if not an expression body</returns>
    public static ExpressionSyntax? GetLambdaBody(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };
    }

    private static List<AnonymousTypeProperty> ExtractProperties(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        SemanticModel semanticModel)
    {
        var properties = new List<AnonymousTypeProperty>();

        foreach (var initializer in anonymousObject.Initializers)
        {
            var propertyName = initializer.NameEquals?.Name.Identifier.Text
                ?? GetImplicitPropertyName(initializer.Expression);

            if (propertyName == null)
                continue;

            var typeInfo = semanticModel.GetTypeInfo(initializer.Expression);
            properties.Add(new AnonymousTypeProperty
            {
                Name = propertyName,
                Expression = initializer.Expression,
                Type = typeInfo.Type
            });
        }

        return properties;
    }

    private static string? GetImplicitPropertyName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }
}

/// <summary>
/// Represents the structure of an anonymous type extracted from a lambda expression.
/// </summary>
public record AnonymousTypeStructure
{
    /// <summary>
    /// The properties of the anonymous type.
    /// </summary>
    public required IReadOnlyList<AnonymousTypeProperty> Properties { get; init; }

    /// <summary>
    /// The lambda parameter name, if available.
    /// </summary>
    public string? ParameterName { get; init; }
}

/// <summary>
/// Represents a property in an anonymous type.
/// </summary>
public record AnonymousTypeProperty
{
    /// <summary>
    /// The name of the property.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The expression that initializes the property.
    /// </summary>
    public required ExpressionSyntax Expression { get; init; }

    /// <summary>
    /// The type of the property, if resolved.
    /// </summary>
    public ITypeSymbol? Type { get; init; }
}
