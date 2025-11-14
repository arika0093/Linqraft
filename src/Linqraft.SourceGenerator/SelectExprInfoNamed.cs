using System;
using System.Collections.Generic;
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
        // For named types, extract the original object creation expression
        // from the lambda and use it as-is
        var originalExpression = ObjectCreation.ToString();
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
        sb.AppendLine($"    var converted = matchedQuery.Select({LambdaParameterName} => {originalExpression});");
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
