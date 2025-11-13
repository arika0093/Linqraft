using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCore.ExprGenerator;

/// <summary>
/// Record for explicit DTO name Select expression information (SelectExpr&lt;TIn, TResult&gt; form)
/// </summary>
internal record SelectExprInfoExplicitDto : SelectExprInfo
{
    public required AnonymousObjectCreationExpressionSyntax AnonymousObject { get; init; }
    public required string ExplicitDtoName { get; init; }
    public required string TargetNamespace { get; init; }

    // Generate DTO classes (including nested DTOs)
    public override List<GenerateDtoClassInfo> GenerateDtoClasses()
    {
        var structure = GenerateDtoStructure();
        var parentClassName = GetParentDtoClassName(structure);
        return GenerateDtoClasses(structure, parentClassName);
    }

    private List<GenerateDtoClassInfo> GenerateDtoClasses(
        DtoStructure structure,
        string? overrideClassName = null
    )
    {
        var result = new List<GenerateDtoClassInfo>();
        var accessibility = GetAccessibilityString(SourceType);
        var className = overrideClassName ?? GetClassName(structure);
        var ns = GetNamespaceString();

        foreach (var prop in structure.Properties)
        {
            if (prop.NestedStructure is not null)
            {
                // Recursively generate nested DTO classes
                result.AddRange(GenerateDtoClasses(prop.NestedStructure));
            }
        }
        // Generate current DTO class
        var dtoClassInfo = new GenerateDtoClassInfo
        {
            Accessibility = accessibility,
            Namespace = TargetNamespace,
            ClassName = className,
            Structure = structure,
        };
        result.Add(dtoClassInfo);
        return result;
    }

    // Generate DTO structure for unique ID generation
    protected override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeAnonymousType(AnonymousObject, SemanticModel, SourceType)!;
    }

    // Get DTO class name
    protected override string GetClassName(DtoStructure structure) => $"{structure.SourceTypeName}Dto_{structure.GetUniqueId()}";

    // Get parent DTO class name
    protected override string GetParentDtoClassName(DtoStructure structure) => ExplicitDtoName;

    // Generate SelectExpr method
    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var accessibility = GetAccessibilityString(SourceType);
        var sourceTypeFullName = structure.SourceTypeFullName;
        var dtoFullName = $"global::{TargetNamespace}.{dtoName}";
        var sb = new StringBuilder();

        var id = GetUniqueId();
        var methodDecl = $"public static IQueryable<TResult> SelectExpr_{id}<TIn, TResult>(";
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// generated select expression method {dtoName} (explicit)");
        sb.AppendLine($"/// at {location.GetDisplayLocation()}");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"{location.GetInterceptsLocationAttributeSyntax()}");
        sb.AppendLine($"{methodDecl}");
        sb.AppendLine($"    this IQueryable<TIn> query,");
        sb.AppendLine($"    Func<TIn, object> selector) where TResult : {dtoFullName}");
        sb.AppendLine($"{{");
        sb.AppendLine(
            $"    var matchedQuery = query as object as IQueryable<{sourceTypeFullName}>;"
        );
        sb.AppendLine($"    var converted = matchedQuery.Select(s => new {dtoFullName}");
        sb.AppendLine($"    {{");

        // Generate property assignments
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignment(prop, 8);
                return $"    {prop.Name} = {assignment}";
            })
            .ToList();
        sb.AppendLine(string.Join($",\n", propertyAssignments));

        sb.AppendLine($"    }});");
        sb.AppendLine($"    return converted as object as IQueryable<TResult>;");
        sb.AppendLine($"}}");
        return sb.ToString();
    }
}
