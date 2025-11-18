using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// SelectExprInfo for named (predefined DTO) Select expressions
/// </summary>
public record SelectExprInfoNamed : SelectExprInfo
{
    /// <summary>
    /// The object creation expression for the named type
    /// </summary>
    public required ObjectCreationExpressionSyntax ObjectCreation { get; init; }

    /// <summary>
    /// Generates DTO classes (predefined types don't generate new classes)
    /// </summary>
    public override List<GenerateDtoClassInfo> GenerateDtoClasses() => [];

    /// <summary>
    /// Generates the DTO structure for unique ID generation
    /// </summary>
    protected override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeNamedType(ObjectCreation, SemanticModel, SourceType)!;
    }

    /// <summary>
    /// Gets the DTO class name (uses the source type name)
    /// </summary>
    protected override string GetClassName(DtoStructure structure) => structure.SourceTypeName;

    /// <summary>
    /// Gets the parent DTO class name
    /// </summary>
    protected override string GetParentDtoClassName(DtoStructure structure) =>
        GetClassName(structure);

    /// <summary>
    /// Gets the namespace where DTOs will be placed
    /// Named types use the DTO's own namespace
    /// </summary>
    protected override string GetDtoNamespace() =>
        SourceType.ContainingNamespace?.ToDisplayString() ?? CallerNamespace;

    // Get expression type string (for documentation)
    protected override string GetExprTypeString() => "predefined";

    /// <summary>
    /// Generates the SelectExpr method code
    /// </summary>
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

        // Determine if we have capture parameters
        var hasCapture = CaptureParameterName != null && CaptureArgumentExpression != null;

        if (hasCapture)
        {
            // Generate method with capture parameter using dynamic to access properties
            sb.AppendLine($"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult, TCapture>(");
            sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, TCapture, TResult> selector, TCapture captureParam)");
            sb.AppendLine("{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{querySourceTypeFullName}>;"
            );

            // Cast the capture parameter to dynamic so property access works
            sb.AppendLine($"    dynamic {CaptureParameterName} = captureParam;");
            sb.AppendLine(
                $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoName}"
            );
        }
        else
        {
            // Generate method without capture parameter
            sb.AppendLine($"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>(");
            sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, TResult> selector)");
            sb.AppendLine("{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{querySourceTypeFullName}>;"
            );
            sb.AppendLine(
                $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoName}"
            );
        }

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
