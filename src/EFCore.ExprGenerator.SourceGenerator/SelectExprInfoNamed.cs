using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCore.ExprGenerator;

/// <summary>
/// Record for named Select expression information
/// </summary>
internal record SelectExprInfoNamed : SelectExprInfo
{
    public required ObjectCreationExpressionSyntax ObjectCreation { get; init; }

    public override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeNamedType(ObjectCreation, SemanticModel, SourceType)!;
    }

    public override string GenerateDtoClasses(DtoStructure structure, List<string> dtoClasses) =>
        structure.SourceTypeName; // Return the original DTO type name, no ID needed

    public override string GetClassName(DtoStructure structure) => structure.SourceTypeName; // Return the original DTO type name

    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var namespaceName = GetNamespaceString();
        // For named types, use the original DTO type name (passed as dtoName parameter)
        var dtoFullName = $"global::{namespaceName}.{dtoName}";
        // Use SourceType for the query source (not the DTO return type)
        var querySourceTypeFullName = SourceType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );

        // For named types, extract the original object creation expression
        // from the lambda and use it as-is
        var originalExpression = ObjectCreation.ToString();

        var sb = new StringBuilder();

        var id = GetUniqueId();
        sb.AppendLine(GenerateMethodHeaderPart(dtoName, location));
        sb.AppendLine($"    public static IQueryable<TResult> SelectExpr_{id}<T, TResult>(");
        sb.AppendLine($"        this IQueryable<T> query,");
        sb.AppendLine($"        Func<T, TResult> selector)");
        sb.AppendLine("    {");
        sb.AppendLine(
            $"        var matchedQuery = query as object as IQueryable<{querySourceTypeFullName}>;"
        );
        sb.AppendLine($"        var converted = matchedQuery.Select(s => {originalExpression});");
        sb.AppendLine($"        return converted as object as IQueryable<TResult>;");
        sb.AppendLine("    }");

        return sb.ToString();
    }

    protected override string GetUsingNamespaceString()
    {
        return $"""
            {base.GetUsingNamespaceString()}
            using {GetNamespaceString()};
            """;
    }
}
