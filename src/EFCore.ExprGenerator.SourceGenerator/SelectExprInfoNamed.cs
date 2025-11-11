using System;
using System.Collections.Generic;
using System.Text;
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

    public override string GenerateDtoClasses(
        DtoStructure structure,
        List<string> dtoClasses,
        string namespaceName
    )
    {
        // nothing to do, as named types are already defined
        // but we need to return the class name
        return GetClassName(structure);
    }

    public override string GetClassName(DtoStructure structure) => structure.SourceTypeName;

    protected override string GenerateSelectExprMethod(string dtoName, DtoStructure structure)
    {
        // Use SourceType for the query source (not the DTO return type)
        var querySourceTypeFullName = SourceType.ToDisplayString(
            Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat
        );

        // For named types, extract the original object creation expression
        // from the lambda and use it as-is
        var originalExpression = ObjectCreation.ToString();

        var sb = new StringBuilder();
        sb.AppendLine(
            $$""""
                /// <summary>
                /// generated select expression method of {{dtoName}}
                /// </summary>
                public static IQueryable<{{dtoName}}> SelectExpr<TResult>(
                    this IQueryable<{{querySourceTypeFullName}}> query,
                    Func<{{querySourceTypeFullName}}, TResult> selector)
                {
                    return query.Select(s => {{originalExpression}});
                }
            """"
        );
        return sb.ToString();
    }
}
