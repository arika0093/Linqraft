using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    public override string GenerateDtoClasses(DtoStructure structure, List<string> dtoClasses)
    {
        return "";
        // var dtoName = GetClassName(structure);
        // var accessibility = GetAccessibilityString(SourceType);

        // var sb = new StringBuilder();
        // sb.AppendLine($"{accessibility} class {dtoName}");
        // sb.AppendLine("{");

        // foreach (var prop in structure.Properties)
        // {
        //     var propertyType = prop.TypeName;

        //     // For nested structures, recursively generate DTOs (add first)
        //     if (prop.NestedStructure is not null)
        //     {
        //         // Extract the base type (e.g., IEnumerable from IEnumerable<T>)
        //         var baseType = propertyType;
        //         if (propertyType.Contains("<"))
        //         {
        //             baseType = propertyType[..propertyType.IndexOf("<")];
        //         }

        //         var nestedDtoName = GenerateDtoClasses(prop.NestedStructure, dtoClasses);
        //         propertyType = $"{baseType}<{nestedDtoName}>";
        //     }
        //     sb.AppendLine($"    public required {propertyType} {prop.Name} {{ get; set; }}");
        // }

        // sb.AppendLine("}");

        // // Add current DTO (nested DTOs are already added by recursive calls)
        // dtoClasses.Add(sb.ToString());
        // return dtoName;
    }

    protected override string BuildSourceCode(
        string mainDtoName,
        List<string> dtoClasses,
        string selectExprMethod
    )
    {
        // force override to use "Anonymous" as dto name
        return base.BuildSourceCode("Anonymous", dtoClasses, selectExprMethod);
    }

    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var sourceTypeFullName = structure.SourceTypeFullName;
        var sb = new StringBuilder();

        sb.AppendLine(GenerateMethodHeaderPart("anonymous type", location));
        sb.AppendLine($"    public static IQueryable<TResult> SelectExpr<TResult>(");
        sb.AppendLine($"        this IQueryable<{sourceTypeFullName}> query,");
        sb.AppendLine($"        Func<{sourceTypeFullName}, TResult> selector");
        sb.AppendLine($"    )");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        var converted = query.Select(s => new");
        sb.AppendLine($"        {{");

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
        sb.AppendLine($"        return (IQueryable<TResult>)(object)converted;");
        sb.AppendLine("    }");
        return sb.ToString();
    }

    public override string GetClassName(DtoStructure structure) => "";
}
