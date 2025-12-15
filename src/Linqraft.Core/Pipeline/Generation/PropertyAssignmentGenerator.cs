using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.Pipeline.Transformation;
using Linqraft.Core.RoslynHelpers;
using Linqraft.Core.SyntaxHelpers;
using Linqraft.Core.Formatting;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Generator for property assignment expressions.
/// Converts property expressions to fully qualified code strings.
/// </summary>
internal class PropertyAssignmentGenerator
{
    private readonly SemanticModel _semanticModel;
    private readonly LinqraftConfiguration _configuration;
    private readonly TransformationPipeline _transformationPipeline;

    /// <summary>
    /// Creates a new property assignment generator.
    /// </summary>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="configuration">The Linqraft configuration</param>
    public PropertyAssignmentGenerator(SemanticModel semanticModel, LinqraftConfiguration configuration)
    {
        _semanticModel = semanticModel;
        _configuration = configuration;
        _transformationPipeline = new TransformationPipeline(
            new NullConditionalTransformer(),
            new FullyQualifyingTransformer()
        );
    }

    /// <summary>
    /// Gets the transformation pipeline.
    /// </summary>
    public TransformationPipeline TransformationPipeline => _transformationPipeline;

    /// <summary>
    /// Generates a fully qualified expression string using the transformation pipeline.
    /// </summary>
    /// <param name="expression">The expression to transform</param>
    /// <param name="expectedType">The expected type of the result</param>
    /// <returns>The transformed expression as a string</returns>
    public string FullyQualifyExpression(ExpressionSyntax expression, ITypeSymbol expectedType)
    {
        var context = new TransformContext
        {
            Expression = expression,
            SemanticModel = _semanticModel,
            ExpectedType = expectedType
        };

        var transformed = _transformationPipeline.Transform(context);
        return transformed.ToString();
    }

    /// <summary>
    /// Checks if an expression has null-conditional access.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression has null-conditional access</returns>
    public bool HasNullConditionalAccess(ExpressionSyntax expression)
    {
        return NullConditionalHelper.HasNullConditionalAccess(expression);
    }

    /// <summary>
    /// Checks if an expression contains Select or SelectMany invocations.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression contains Select or SelectMany</returns>
    public bool HasSelectOrSelectMany(ExpressionSyntax expression)
    {
        return RoslynTypeHelper.ContainsSelectInvocation(expression)
            || RoslynTypeHelper.ContainsSelectManyInvocation(expression);
    }

    /// <summary>
    /// Checks if a type is a collection type.
    /// </summary>
    /// <param name="typeSymbol">The type to check</param>
    /// <returns>True if the type is a collection</returns>
    public bool IsCollectionType(ITypeSymbol typeSymbol)
    {
        return RoslynTypeHelper.IsCollectionType(typeSymbol);
    }

    /// <summary>
    /// Converts an object creation expression to use fully qualified type names.
    /// </summary>
    /// <param name="objectCreation">The object creation expression</param>
    /// <returns>The expression with fully qualified type names</returns>
    public string ConvertObjectCreationToFullyQualified(ObjectCreationExpressionSyntax objectCreation)
    {
        var typeInfo = _semanticModel.GetTypeInfo(objectCreation);
        if (typeInfo.Type is not INamedTypeSymbol typeSymbol)
            return objectCreation.ToString();

        var fullyQualifiedTypeName = typeSymbol.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );

        var original = objectCreation.ToString();
        var originalTypeName = objectCreation.Type?.ToString() ?? "";
        
        if (!string.IsNullOrEmpty(originalTypeName) && !originalTypeName.StartsWith("global::"))
        {
            return original.Replace(originalTypeName, fullyQualifiedTypeName);
        }

        return original;
    }

    /// <summary>
    /// Gets the fully qualified name for a static or const member.
    /// </summary>
    /// <param name="memberAccess">The member access expression</param>
    /// <returns>The fully qualified name, or null if not applicable</returns>
    public string? GetFullyQualifiedStaticMember(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Kind() != SyntaxKind.SimpleMemberAccessExpression)
            return null;

        var symbolInfo = _semanticModel.GetSymbolInfo(memberAccess);

        if (symbolInfo.Symbol is IFieldSymbol fieldSymbol
            && (fieldSymbol.IsStatic || fieldSymbol.IsConst))
        {
            var containingType = fieldSymbol.ContainingType;
            var fullTypeName = containingType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            return $"{fullTypeName}.{fieldSymbol.Name}";
        }

        if (symbolInfo.Symbol is IPropertySymbol propertySymbol && propertySymbol.IsStatic)
        {
            var containingType = propertySymbol.ContainingType;
            var fullTypeName = containingType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            return $"{fullTypeName}.{propertySymbol.Name}";
        }

        return null;
    }

    /// <summary>
    /// Gets the fully qualified name for an identifier (enum, static field, etc.).
    /// </summary>
    /// <param name="identifier">The identifier expression</param>
    /// <returns>The fully qualified name, or null if not applicable</returns>
    public string? GetFullyQualifiedIdentifier(IdentifierNameSyntax identifier)
    {
        var symbolInfo = _semanticModel.GetSymbolInfo(identifier);

        if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
        {
            // Check for enum values
            if (fieldSymbol.ContainingType?.TypeKind == TypeKind.Enum)
            {
                var containingType = fieldSymbol.ContainingType;
                var fullTypeName = containingType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                return $"{fullTypeName}.{fieldSymbol.Name}";
            }

            // Check for const/static fields
            if ((fieldSymbol.IsConst || fieldSymbol.IsStatic) && fieldSymbol.ContainingType is not null)
            {
                if (fieldSymbol.DeclaredAccessibility == Accessibility.Public
                    || fieldSymbol.DeclaredAccessibility == Accessibility.Internal)
                {
                    var containingType = fieldSymbol.ContainingType;
                    var fullTypeName = containingType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    return $"{fullTypeName}.{fieldSymbol.Name}";
                }
            }
        }
        else if (symbolInfo.Symbol is IPropertySymbol propertySymbol && propertySymbol.IsStatic)
        {
            if (propertySymbol.DeclaredAccessibility == Accessibility.Public
                || propertySymbol.DeclaredAccessibility == Accessibility.Internal)
            {
                var containingType = propertySymbol.ContainingType;
                var fullTypeName = containingType!.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                return $"{fullTypeName}.{propertySymbol.Name}";
            }
        }

        return null;
    }

    /// <summary>
    /// Generates a property assignment expression for a DtoProperty.
    /// This is the main entry point for generating property assignment code.
    /// </summary>
    /// <param name="property">The property to generate assignment for</param>
    /// <param name="indents">The indentation level</param>
    /// <param name="lambdaParameterName">The lambda parameter name</param>
    /// <param name="callerNamespace">The caller namespace</param>
    /// <returns>The generated property assignment code</returns>
    public string GeneratePropertyAssignment(
        DtoProperty property,
        int indents,
        string lambdaParameterName,
        string callerNamespace)
    {
        var expression = property.OriginalExpression;
        var syntax = property.OriginalSyntax;

        // For nested structure cases, delegate to specialized handlers
        if (property.NestedStructure is not null)
        {
            // If the nested structure is from a named type (not anonymous),
            // preserve the original type name with full qualification
            if (property.IsNestedFromNamedType)
            {
                // For named types in Select, preserve the original object creation
                // but convert type names to fully qualified names
                return FullyQualifyExpression(syntax, property.TypeSymbol);
            }

            // For other nested cases, convert anonymous types to DTOs
            // This is a simplified version - the full implementation is in SelectExprInfo
            return FullyQualifyExpression(syntax, property.TypeSymbol);
        }

        // If object creation expression, convert type names to fully qualified names.
        if (syntax is ObjectCreationExpressionSyntax objectCreation)
        {
            return ConvertObjectCreationToFullyQualified(objectCreation);
        }

        // If nullable operator is used, convert to explicit null check
        var hasConditionalAccess = HasNullConditionalAccess(syntax);
        var hasSelectOrSelectMany = HasSelectOrSelectMany(syntax);
        var isCollectionWithSelect = _configuration.ArrayNullabilityRemoval
            && hasSelectOrSelectMany
            && IsCollectionType(property.TypeSymbol);

        if (hasConditionalAccess && (property.IsNullable || isCollectionWithSelect))
        {
            // Delegate to NullCheckGenerator for null-conditional conversion
            return FullyQualifyExpression(syntax, property.TypeSymbol);
        }

        // For static/const expression, return expression with full-name resolution
        if (syntax is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Kind() == SyntaxKind.SimpleMemberAccessExpression)
        {
            var fullyQualified = GetFullyQualifiedStaticMember(memberAccess);
            if (fullyQualified is not null)
            {
                return fullyQualified;
            }
        }

        // For simple identifier, check if it's a static/const member
        if (syntax is IdentifierNameSyntax identifierName)
        {
            var fullyQualified = GetFullyQualifiedIdentifier(identifierName);
            if (fullyQualified is not null)
            {
                return fullyQualified;
            }
        }

        // For any other expression, ensure all references are fully qualified
        return FullyQualifyExpression(syntax, property.TypeSymbol);
    }
}
