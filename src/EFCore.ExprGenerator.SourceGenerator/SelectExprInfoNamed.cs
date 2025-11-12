using System;
using System.Collections.Generic;
using System.Text;
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
        GetClassName(structure);

    public override string GetClassName(DtoStructure structure) => structure.SourceTypeName;

    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var namespaceName = GetNamespaceString();
        var dtoFullName = $"global::{namespaceName}.{GetClassName(structure)}";
        // Use SourceType for the query source (not the DTO return type)
        var querySourceTypeFullName = SourceType.ToDisplayString(
            Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat
        );

        // For named types, extract the original object creation expression
        // from the lambda and use it as-is
        var originalExpression = ObjectCreation.ToString();

        var sb = new StringBuilder();

        sb.AppendLine(GenerateMethodHeaderPart(dtoName, location));
        sb.AppendLine($"    public static IQueryable<{dtoFullName}> SelectExpr<TResult>(");
        sb.AppendLine($"        this IQueryable<{querySourceTypeFullName}> query,");
        sb.AppendLine($"        Func<{querySourceTypeFullName}, TResult> selector)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return query.Select(s => {originalExpression});");
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
