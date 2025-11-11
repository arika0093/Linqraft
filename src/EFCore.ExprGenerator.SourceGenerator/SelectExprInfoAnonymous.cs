using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCore.ExprGenerator;

/// <summary>
/// Record for anonymous Select expression information
/// </summary>
internal record SelectExprInfoAnonymous : SelectExprInfo
{
    public required AnonymousObjectCreationExpressionSyntax AnonymousObject { get; init; }

    public override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeAnonymousType(AnonymousObject, SemanticModel, SourceType)!;
    }

    public override string GenerateDtoClasses(
        DtoStructure structure,
        List<string> dtoClasses,
        string namespaceName
    )
    {
        var dtoName = GetClassName(structure);
        var accessibility = GetAccessibilityString(SourceType);

        var sb = new StringBuilder();
        sb.AppendLine($"{accessibility} class {dtoName}");
        sb.AppendLine("{");

        foreach (var prop in structure.Properties)
        {
            var propertyType = prop.TypeName;

            // For nested structures, recursively generate DTOs (add first)
            if (prop.NestedStructure is not null)
            {
                // Extract the base type (e.g., IEnumerable from IEnumerable<T>)
                var baseType = propertyType;
                if (propertyType.Contains("<"))
                {
                    baseType = propertyType[..propertyType.IndexOf("<")];
                }

                var nestedId = prop.NestedStructure.GetUniqueId();
                var nestedDtoName = GenerateDtoClasses(
                    prop.NestedStructure,
                    dtoClasses,
                    namespaceName
                );
                // Since propertyType already has a fully qualified name starting with global::,
                // add global:: to nestedDtoName as well
                var nestedDtoFullName = $"global::{namespaceName}.{nestedDtoName}";
                propertyType = $"{baseType}<{nestedDtoFullName}>";
            }
            sb.AppendLine($"    public required {propertyType} {prop.Name} {{ get; set; }}");
        }

        sb.AppendLine("}");

        // Add current DTO (nested DTOs are already added by recursive calls)
        dtoClasses.Add(sb.ToString());
        return dtoName;
    }

    protected override string GenerateSelectExprMethod(string dtoName, DtoStructure structure)
    {
        var sourceTypeFullName = structure.SourceTypeFullName;
        var sb = new StringBuilder();
        sb.AppendLine(
            $$""""
                /// <summary>
                /// generated select expression method of {{dtoName}}
                /// </summary>
                public static IQueryable<{{dtoName}}> SelectExpr<TResult>(
                    this IQueryable<{{sourceTypeFullName}}> query,
                    Func<{{sourceTypeFullName}}, TResult> selector)
                {
                    return query.Select(s => new {{dtoName}}
                    {
            """"
        );
        // Generate property assignments
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignment(prop, 12);
                return $"            {prop.Name} = {assignment}";
            })
            .ToList();
        sb.AppendLine(string.Join($",\n", propertyAssignments));
        sb.AppendLine("        });");
        sb.AppendLine("    }");
        return sb.ToString();
    }

    public override string GetClassName(DtoStructure structure) =>
        $"{structure.SourceTypeName}Dto_{GetUniqueId()}";
}
