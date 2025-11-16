using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// SelectExprInfo for anonymous type Select expressions
/// </summary>
public record SelectExprInfoAnonymous : SelectExprInfo
{
    /// <summary>
    /// The anonymous object creation expression
    /// </summary>
    public required AnonymousObjectCreationExpressionSyntax AnonymousObject { get; init; }

    /// <summary>
    /// Generates DTO classes (anonymous types don't generate separate classes)
    /// </summary>
    public override List<GenerateDtoClassInfo> GenerateDtoClasses() => [];

    /// <summary>
    /// Generates the DTO structure for unique ID generation
    /// </summary>
    protected override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeAnonymousType(AnonymousObject, SemanticModel, SourceType)!;
    }

    /// <summary>
    /// Gets the DTO class name (empty for anonymous types)
    /// </summary>
    protected override string GetClassName(DtoStructure structure) => "";

    /// <summary>
    /// Gets the parent DTO class name (empty for anonymous types)
    /// </summary>
    protected override string GetParentDtoClassName(DtoStructure structure) => "";

    /// <summary>
    /// Gets the namespace where DTOs will be placed
    /// Anonymous types don't generate separate DTOs, but return caller namespace for consistency
    /// </summary>
    protected override string GetDtoNamespace() => CallerNamespace;

    // Generate SelectExpr method
    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var sourceTypeFullName = structure.SourceTypeFullName;
        var returnTypePrefix = GetReturnTypePrefix();
        var sb = new StringBuilder();

        var id = GetUniqueId();
        sb.AppendLine(GenerateMethodHeaderPart("anonymous type", location));
        sb.AppendLine($"public static {returnTypePrefix}<TResult> SelectExpr_{id}<T, TResult>(");
        sb.AppendLine($"    this {returnTypePrefix}<T> query,");
        sb.AppendLine($"    Func<T, TResult> selector");
        sb.AppendLine($")");
        sb.AppendLine($"{{");
        sb.AppendLine(
            $"    var matchedQuery = query as object as {returnTypePrefix}<{sourceTypeFullName}>;"
        );
        sb.AppendLine($"    var converted = matchedQuery.Select({LambdaParameterName} => new");
        sb.AppendLine($"    {{");

        // Generate property assignments
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignment(prop, 8);
                return $"        {prop.Name} = {assignment}";
            })
            .ToList();
        sb.AppendLine(string.Join($",\n", propertyAssignments));
        sb.AppendLine("    });");
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine("}");
        sb.AppendLine();
        return sb.ToString();
    }
}
