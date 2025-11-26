using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Represents the structure of a DTO with its source type and properties
/// </summary>
public record DtoStructure(ITypeSymbol SourceType, List<DtoProperty> Properties)
{
    /// <summary>
    /// Optional hint name for generating better class names.
    /// This is typically the property name that this structure is assigned to.
    /// For example, if the anonymous type is used in: SampleData = new { Id = s.Id }
    /// then HintName would be "SampleData".
    /// </summary>
    public string? HintName { get; init; }

    /// <summary>
    /// Gets the simple name of the source type
    /// </summary>
    public string SourceTypeName => SourceType.Name;

    /// <summary>
    /// Gets the best name to use for generating class names.
    /// Uses HintName if available, otherwise falls back to SourceTypeName.
    /// </summary>
    public string BestName => !string.IsNullOrEmpty(HintName) ? HintName! : SourceTypeName;

    /// <summary>
    /// Gets the fully qualified name of the source type
    /// </summary>
    public string SourceTypeFullName =>
        SourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Generates a unique identifier for this DTO structure based on its properties
    /// </summary>
    public string GetUniqueId()
    {
        // Generate hash from property structure
        var signatureFullName = SourceTypeFullName;
        var signatureProps = string.Join(
            "|",
            Properties.Select(p => $"{p.Name}:{p.TypeName}:{p.IsNullable}")
        );
        var signature = $"{signatureFullName}|{signatureProps}";
        return HashUtility.GenerateSha256Hash(signature);
    }

    /// <summary>
    /// Analyzes a named type (predefined DTO) object creation expression
    /// </summary>
    /// <param name="namedObj">The object creation expression to analyze</param>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="sourceType">The source type being selected from</param>
    /// <param name="hintName">Optional hint name for better class naming</param>
    /// <returns>A DtoStructure representing the named type</returns>
    public static DtoStructure? AnalyzeNamedType(
        ObjectCreationExpressionSyntax namedObj,
        SemanticModel semanticModel,
        ITypeSymbol sourceType,
        string? hintName = null
    )
    {
        // For named types, get the return type (the type being constructed) instead of the source type
        var returnTypeInfo = semanticModel.GetTypeInfo(namedObj);
        var returnType = returnTypeInfo.Type ?? returnTypeInfo.ConvertedType;

        // If we can't determine the return type, fall back to source type
        var targetType = returnType ?? sourceType;

        // Get properties from the target DTO type for nullable information
        var targetProperties = targetType
            .GetMembers()
            .OfType<IPropertySymbol>()
            .ToDictionary(p => p.Name, p => p);

        var properties = new List<DtoProperty>();
        foreach (var arg in namedObj.ArgumentList?.Arguments ?? [])
        {
            // Get property name from argument name
            string propertyName;
            if (arg.NameColon is not null)
            {
                propertyName = arg.NameColon.Name.Identifier.Text;
            }
            else
            {
                continue; // Skip if no name is provided
            }
            var expression = arg.Expression;
            targetProperties.TryGetValue(propertyName, out var targetProp);
            var property = DtoProperty.AnalyzeExpression(
                propertyName,
                expression,
                semanticModel,
                targetProp
            );
            if (property is not null)
            {
                properties.Add(property);
            }
        }
        foreach (
            var init in namedObj.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? []
        )
        {
            // Get property name from left side of assignment
            string propertyName = init.Left.ToString();
            var expression = init.Right;
            targetProperties.TryGetValue(propertyName, out var targetProp);
            var property = DtoProperty.AnalyzeExpression(
                propertyName,
                expression,
                semanticModel,
                targetProp
            );
            if (property is not null)
            {
                properties.Add(property);
            }
        }
        return new DtoStructure(SourceType: targetType, Properties: properties)
        {
            HintName = hintName,
        };
    }

    /// <summary>
    /// Analyzes an anonymous type object creation expression
    /// </summary>
    /// <param name="anonymousObj">The anonymous object creation expression to analyze</param>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="sourceType">The source type being selected from</param>
    /// <param name="hintName">Optional hint name for better class naming</param>
    /// <returns>A DtoStructure representing the anonymous type</returns>
    public static DtoStructure? AnalyzeAnonymousType(
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        SemanticModel semanticModel,
        ITypeSymbol sourceType,
        string? hintName = null
    )
    {
        // Get the type info of the anonymous object itself
        // This will have complete type information including for expressions with capture parameters
        var anonymousTypeInfo = semanticModel.GetTypeInfo(anonymousObj);
        var anonymousType = anonymousTypeInfo.Type ?? anonymousTypeInfo.ConvertedType;

        // Build a dictionary of property names to their types from the anonymous type
        var anonymousProperties = anonymousType
            ?.GetMembers()
            .OfType<IPropertySymbol>()
            .ToDictionary(p => p.Name, p => p.Type);

        var properties = new List<DtoProperty>();
        foreach (var initializer in anonymousObj.Initializers)
        {
            string propertyName;
            var expression = initializer.Expression;
            // For explicit property names (e.g., Id = s.Id)
            if (initializer.NameEquals is not null)
            {
                propertyName = initializer.NameEquals.Name.Identifier.Text;
            }
            // For implicit property names (e.g., s.Id)
            else
            {
                // Get property name inferred from expression
                var name = GetImplicitPropertyName(expression);
                if (name is null)
                {
                    continue;
                }
                propertyName = name;
            }

            // Try to get the property type from the anonymous type first
            IPropertySymbol? targetProperty = null;
            if (
                anonymousProperties != null
                && anonymousProperties.TryGetValue(propertyName, out var propType)
            )
            {
                // Create a temporary property symbol for type information
                targetProperty = anonymousType
                    ?.GetMembers(propertyName)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault();
            }

            var property = DtoProperty.AnalyzeExpression(
                propertyName,
                expression,
                semanticModel,
                targetProperty
            );
            if (property is not null)
            {
                properties.Add(property);
            }
        }
        return new DtoStructure(SourceType: sourceType, Properties: properties)
        {
            HintName = hintName,
        };
    }

    private static string? GetImplicitPropertyName(ExpressionSyntax expression)
    {
        return ExpressionHelper.GetPropertyName(expression);
    }
}
