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

        var sb = new StringBuilder();
        sb.AppendLine($"public class {dtoName}");
        sb.AppendLine("{");

        foreach (var prop in structure.Properties)
        {
            var propertyType = prop.TypeName;
            // If propertyType is a generic type, use only the base type
            if (propertyType.Contains("<"))
            {
                propertyType = propertyType[..propertyType.IndexOf("<")];
            }

            // For nested structures, recursively generate DTOs (add first)
            if (prop.NestedStructure is not null)
            {
                var nestedId = prop.NestedStructure.GetUniqueId();
                var nestedDtoName = GenerateDtoClasses(
                    prop.NestedStructure,
                    dtoClasses,
                    namespaceName
                );
                // Since propertyType already has a fully qualified name starting with global::,
                // add global:: to nestedDtoName as well
                var nestedDtoFullName = $"global::{namespaceName}.{nestedDtoName}";
                propertyType = $"{propertyType}<{nestedDtoFullName}>";
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
