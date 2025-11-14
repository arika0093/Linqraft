using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft;

/// <summary>
/// Record for named Select expression information
/// </summary>
internal record SelectExprInfoNamed : SelectExprInfo
{
    public required ObjectCreationExpressionSyntax ObjectCreation { get; init; }

    // Generate DTO classes (including nested DTOs)
    public override List<GenerateDtoClassInfo> GenerateDtoClasses() => [];

    // Generate DTO structure for unique ID generation
    protected override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeNamedType(ObjectCreation, SemanticModel, SourceType)!;
    }

    // Get DTO class name
    protected override string GetClassName(DtoStructure structure) => structure.SourceTypeName;

    // Get parent DTO class name
    protected override string GetParentDtoClassName(DtoStructure structure) =>
        GetClassName(structure);

    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        // Use SourceType for the query source (not the DTO return type)
        var querySourceTypeFullName = SourceType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );
        var returnTypePrefix = GetReturnTypePrefix();

        var sb = new StringBuilder();
        var id = GetUniqueId();
        sb.AppendLine(GenerateMethodHeaderPart(dtoName, location));
        sb.AppendLine($"public static {returnTypePrefix}<TResult> SelectExpr_{id}<T, TResult>(");
        sb.AppendLine($"    this {returnTypePrefix}<T> query,");
        sb.AppendLine($"    Func<T, TResult> selector)");
        sb.AppendLine("{");
        sb.AppendLine(
            $"    var matchedQuery = query as object as {returnTypePrefix}<{querySourceTypeFullName}>;"
        );
        sb.AppendLine(
            $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoName}"
        );
        sb.AppendLine($"    {{");

        // Generate property assignments using GeneratePropertyAssignment to properly handle null-conditional operators
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
        sb.AppendLine("}");
        return sb.ToString();
    }
}
