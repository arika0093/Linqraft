using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// SelectExprInfo for explicit DTO name Select expressions (SelectExpr&lt;TIn, TResult&gt; form)
/// </summary>
public record SelectExprInfoExplicitDto : SelectExprInfo
{
    /// <summary>
    /// The anonymous object creation expression containing the property selections
    /// </summary>
    public required AnonymousObjectCreationExpressionSyntax AnonymousObject { get; init; }

    /// <summary>
    /// The explicit name for the DTO (from TResult type parameter)
    /// </summary>
    public required string ExplicitDtoName { get; init; }

    /// <summary>
    /// The target namespace where the DTO will be generated
    /// </summary>
    public required string TargetNamespace { get; init; }

    /// <summary>
    /// Parent class names in order from outermost to innermost (empty if DTO is not nested)
    /// </summary>
    public required List<string> ParentClasses { get; init; }

    /// <summary>
    /// The ITypeSymbol of the TResult type (for extracting accessibility)
    /// </summary>
    public required ITypeSymbol TResultType { get; init; }

    /// <summary>
    /// Generates DTO classes (including nested DTOs)
    /// </summary>
    public override List<GenerateDtoClassInfo> GenerateDtoClasses()
    {
        var structure = GenerateDtoStructure();
        var parentClassName = GetParentDtoClassName(structure);
        return GenerateDtoClasses(structure, parentClassName);
    }

    private List<GenerateDtoClassInfo> GenerateDtoClasses(
        DtoStructure structure,
        string? overrideClassName = null,
        List<string>? nestedParentClasses = null,
        List<string>? nestedParentAccessibilities = null
    )
    {
        var result = new List<GenerateDtoClassInfo>();
        // Extract the actual accessibility from TResultType
        var accessibility = GetAccessibilityString(TResultType);
        var className = overrideClassName ?? GetClassName(structure);

        // Determine parent classes for nested DTOs
        var currentParentClasses = nestedParentClasses ?? ParentClasses;
        var currentParentAccessibilities =
            nestedParentAccessibilities ?? GetParentAccessibilities();

        // Get existing properties from the TResultType (only for the main DTO, not nested)
        var existingProperties = new HashSet<string>();
        if (overrideClassName == ExplicitDtoName)
        {
            // This is the main DTO, check for existing properties
            var properties = TResultType.GetMembers().OfType<IPropertySymbol>();
            foreach (var property in properties)
            {
                existingProperties.Add(property.Name);
            }
        }

        // Nested DTOs are placed at the same level as the current DTO, not inside it
        // So they share the same parent classes
        foreach (var prop in structure.Properties)
        {
            if (prop.NestedStructure is not null)
            {
                // Recursively generate nested DTO classes with the same parent info
                result.AddRange(
                    GenerateDtoClasses(
                        prop.NestedStructure,
                        overrideClassName: null,
                        nestedParentClasses: currentParentClasses,
                        nestedParentAccessibilities: currentParentAccessibilities
                    )
                );
            }
        }
        // Generate current DTO class
        // Use GetActualDtoNamespace() to handle global namespace correctly
        var actualNamespace = GetActualDtoNamespace();
        var dtoClassInfo = new GenerateDtoClassInfo
        {
            Accessibility = accessibility,
            Namespace = actualNamespace,
            ClassName = className,
            Structure = structure,
            NestedClasses = [.. result],
            ParentClasses = currentParentClasses,
            ParentAccessibilities = currentParentAccessibilities,
            ExistingProperties = existingProperties,
        };
        result.Add(dtoClassInfo);
        return result;
    }

    /// <summary>
    /// Gets parent class accessibilities from TResultType
    /// </summary>
    private List<string> GetParentAccessibilities()
    {
        var accessibilities = new List<string>();
        var currentType = TResultType.ContainingType;

        // Traverse up the containing types to get all parent accessibilities
        while (currentType != null)
        {
            accessibilities.Insert(0, GetAccessibilityString(currentType));
            currentType = currentType.ContainingType;
        }

        return accessibilities;
    }

    /// <summary>
    /// Generates the DTO structure for unique ID generation
    /// </summary>
    protected override DtoStructure GenerateDtoStructure()
    {
        var propertyAccessibilities = ExtractPropertyAccessibilities();
        return DtoStructure.AnalyzeAnonymousType(
            AnonymousObject,
            SemanticModel,
            SourceType,
            propertyAccessibilities
        )!;
    }

    /// <summary>
    /// Extracts property accessibilities from the TResult type (if it exists as a partial class)
    /// Checks both the LinqraftAccessibility attribute and the actual property accessibility
    /// </summary>
    private Dictionary<string, string> ExtractPropertyAccessibilities()
    {
        var accessibilities = new Dictionary<string, string>();
        
        // Get all properties from the TResultType
        var properties = TResultType.GetMembers().OfType<IPropertySymbol>();
        
        foreach (var property in properties)
        {
            // First, check if the property has a LinqraftAccessibility attribute
            var attributeAccessibility = GetAccessibilityFromAttribute(property);
            
            if (attributeAccessibility != null)
            {
                // Use the attribute value if present
                accessibilities[property.Name] = attributeAccessibility;
            }
            else
            {
                // Fall back to the actual property accessibility
                var accessibility = GetAccessibilityString(property);
                accessibilities[property.Name] = accessibility;
            }
        }
        
        return accessibilities;
    }

    /// <summary>
    /// Extracts the accessibility from the LinqraftAccessibility attribute if present
    /// </summary>
    private string? GetAccessibilityFromAttribute(IPropertySymbol propertySymbol)
    {
        // Look for the LinqraftAccessibilityAttribute
        var attribute = propertySymbol.GetAttributes()
            .FirstOrDefault(attr => 
                attr.AttributeClass?.Name == "LinqraftAccessibilityAttribute" ||
                attr.AttributeClass?.ToDisplayString() == "Linqraft.LinqraftAccessibilityAttribute");
        
        if (attribute == null)
            return null;
        
        // Get the accessibility value from the attribute constructor argument
        if (attribute.ConstructorArguments.Length > 0)
        {
            var accessibilityValue = attribute.ConstructorArguments[0].Value?.ToString();
            if (!string.IsNullOrEmpty(accessibilityValue))
            {
                return accessibilityValue;
            }
        }
        
        return null;
    }

    /// <summary>
    /// Gets the accessibility string from a property symbol
    /// </summary>
    private string GetAccessibilityString(IPropertySymbol propertySymbol)
    {
        return propertySymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "public", // Default to public
        };
    }

    /// <summary>
    /// Gets the DTO class name
    /// </summary>
    protected override string GetClassName(DtoStructure structure) =>
        $"{structure.SourceTypeName}Dto_{structure.GetUniqueId()}";

    /// <summary>
    /// Gets the parent DTO class name
    /// </summary>
    protected override string GetParentDtoClassName(DtoStructure structure) => ExplicitDtoName;

    /// <summary>
    /// Gets the namespace where DTOs will be placed
    /// </summary>
    protected override string GetDtoNamespace() => GetActualDtoNamespace();

    // Get expression type string (for documentation)
    protected override string GetExprTypeString() => "explicit";

    // Get the full name for a nested DTO class (including parent classes)
    protected override string GetNestedDtoFullName(string nestedClassName)
    {
        var actualNamespace = GetActualDtoNamespace();
        // Nested DTOs are placed within the same parent classes as the main DTO
        if (ParentClasses.Count > 0)
        {
            return $"global::{actualNamespace}.{string.Join(".", ParentClasses)}.{nestedClassName}";
        }
        return $"global::{actualNamespace}.{nestedClassName}";
    }

    /// <summary>
    /// Gets the actual namespace where the DTO will be placed
    /// This mirrors the logic in SelectExprGroups.TargetNamespace getter
    /// </summary>
    private string GetActualDtoNamespace()
    {
        // Determine if this is a global namespace (same logic as SelectExprGroups.IsGlobalNamespace)
        var sourceNamespace = GetNamespaceString();
        var isGlobalNamespace =
            string.IsNullOrEmpty(sourceNamespace) || sourceNamespace.Contains("<");

        if (isGlobalNamespace)
        {
            return Configuration?.GlobalNamespace ?? "Linqraft";
        }
        return TargetNamespace;
    }

    /// <summary>
    /// Generates the SelectExpr method code
    /// </summary>
    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var sourceTypeFullName = structure.SourceTypeFullName;
        var actualNamespace = GetActualDtoNamespace();

        // Build full DTO name with parent classes if nested
        var dtoFullName =
            ParentClasses.Count > 0
                ? $"global::{actualNamespace}.{string.Join(".", ParentClasses)}.{dtoName}"
                : $"global::{actualNamespace}.{dtoName}";

        var returnTypePrefix = GetReturnTypePrefix();
        var sb = new StringBuilder();

        var id = GetUniqueId();
        var methodDecl =
            $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>(";
        sb.AppendLine(GenerateMethodHeaderPart(dtoName, location));
        sb.AppendLine($"{methodDecl}");
        sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, object> selector)");
        sb.AppendLine($"{{");
        sb.AppendLine(
            $"    var matchedQuery = query as object as {returnTypePrefix}<{sourceTypeFullName}>;"
        );
        sb.AppendLine(
            $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoFullName}"
        );
        sb.AppendLine($"    {{");

        // Generate property assignments
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignment(prop, 8);
                return $"        {prop.Name} = {assignment}";
            })
            .ToList();
        sb.AppendLine(string.Join($",\n", propertyAssignments));

        sb.AppendLine($"    }});");
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine($"}}");
        return sb.ToString();
    }
}
