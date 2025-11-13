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

    public override string GenerateDtoClasses(DtoStructure structure, List<string> dtoClasses) =>
        "";

    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var sourceTypeFullName = structure.SourceTypeFullName;
        var sb = new StringBuilder();

        var id = GetUniqueId();
        sb.AppendLine(GenerateMethodHeaderPart("anonymous type", location));
        sb.AppendLine($"    public static IQueryable<TResult> SelectExpr_{id}<T, TResult>(");
        sb.AppendLine($"        this IQueryable<T> query,");
        sb.AppendLine($"        Func<T, TResult> selector");
        sb.AppendLine($"    )");
        sb.AppendLine($"    {{");
        sb.AppendLine(
            $"        var matchedQuery = query as object as IQueryable<{sourceTypeFullName}>;"
        );
        sb.AppendLine($"        var converted = matchedQuery.Select(s => new");
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
        sb.AppendLine($"        return converted as object as IQueryable<TResult>;");
        sb.AppendLine("    }");
        return sb.ToString();
    }

    public override string GetClassName(DtoStructure structure) => "";
}
