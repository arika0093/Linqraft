using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft;

/// <summary>
/// Record for explicit DTO name Select expression information (SelectExpr&lt;TIn, TResult&gt; form)
/// </summary>
internal record SelectExprInfoExplicitDto : SelectExprInfo
{
    public required AnonymousObjectCreationExpressionSyntax AnonymousObject { get; init; }
    public required string ExplicitDtoName { get; init; }
    public required string TargetNamespace { get; init; }

    /// <summary>
    /// Parent class names in order from outermost to innermost (empty if DTO is not nested)
    /// </summary>
    public required List<string> ParentClasses { get; init; }

    /// <summary>
    /// The ITypeSymbol of the TResult type (for extracting accessibility)
    /// </summary>
    public required ITypeSymbol TResultType { get; init; }

    // Generate DTO classes (including nested DTOs)
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
        };
        result.Add(dtoClassInfo);
        return result;
    }

    // Get parent class accessibilities from TResultType
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

    // Generate DTO structure for unique ID generation
    protected override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeAnonymousType(AnonymousObject, SemanticModel, SourceType)!;
    }

    // Get DTO class name
    protected override string GetClassName(DtoStructure structure) =>
        $"{structure.SourceTypeName}Dto_{structure.GetUniqueId()}";

    // Get parent DTO class name
    protected override string GetParentDtoClassName(DtoStructure structure) => ExplicitDtoName;

    // Get the namespace where DTOs will be placed
    protected override string GetDtoNamespace() => GetActualDtoNamespace();

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

    // Get the actual namespace where the DTO will be placed
    // This mirrors the logic in SelectExprGroups.TargetNamespace getter
    private string GetActualDtoNamespace()
    {
        // Determine if this is a global namespace (same logic as SelectExprGroups.IsGlobalNamespace)
        var sourceNamespace = GetNamespaceString();
        var isGlobalNamespace =
            string.IsNullOrEmpty(sourceNamespace) || sourceNamespace.Contains("<");

        if (isGlobalNamespace)
        {
            return "Linqraft";
        }
        return TargetNamespace;
    }

    // Generate SelectExpr method
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
        sb.AppendLine(GenerateMethodHeaderPart(dtoName, location, "explicit"));
        sb.AppendLine($"{methodDecl}");
        sb.AppendLine($"    this {returnTypePrefix}<TIn> query,");
        sb.AppendLine($"    Func<TIn, object> selector) where TResult : {dtoFullName}");
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
