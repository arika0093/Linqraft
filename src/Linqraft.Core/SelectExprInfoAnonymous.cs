using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
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
        return DtoStructure.AnalyzeAnonymousType(
            AnonymousObject,
            SemanticModel,
            SourceType,
            configuration: Configuration
        )!;
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

    // Get expression type string (for documentation)
    protected override string GetExprTypeString() => "anonymous";

    /// <summary>
    /// Generates static field declarations for pre-built expressions (if enabled)
    /// </summary>
    public override string? GenerateStaticFields()
    {
        // Anonymous types cannot use pre-built expressions because we don't know the result type at compile time
        // The result type is inferred from the lambda and is an compiler-generated anonymous type
        return null;
    }

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
        
        // Anonymous types cannot use pre-built expressions (result type is unknown)
        // so we always use inline lambda
        
        sb.AppendLine(GenerateMethodHeaderPart("anonymous type", location));

        // Determine if we have capture parameters
        var hasCapture = CaptureArgumentExpression != null && CaptureArgumentType != null;

        if (hasCapture)
        {
            // Generate method with capture parameter that creates closure variables
            // Extract property names and values from the capture object to create properly-typed closure variables
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>("
            );
            sb.AppendLine(
                $"    this {returnTypePrefix}<TIn> query, Func<TIn, TResult> selector, object captureParam)"
            );
            sb.AppendLine($"{{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{sourceTypeFullName}>;"
            );

            // For anonymous types, use dynamic to extract properties as closure variables
            // This allows the lambda to reference them with the correct types (closure will capture the typed values)
            var isAnonymousType =
                CaptureArgumentType != null && CaptureArgumentType.IsAnonymousType;
            if (isAnonymousType && CaptureArgumentType != null)
            {
                // For anonymous types, get the properties and create closure variables using dynamic
                var properties = CaptureArgumentType.GetMembers().OfType<IPropertySymbol>();
                sb.AppendLine($"    dynamic captureObj = captureParam;");
                foreach (var prop in properties)
                {
                    var propTypeName = prop.Type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    sb.AppendLine($"    {propTypeName} {prop.Name} = captureObj.{prop.Name};");
                }
            }
            else
            {
                // For non-anonymous types, just cast it
                var captureTypeName =
                    CaptureArgumentType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    ?? "object";
                sb.AppendLine($"    var capture = ({captureTypeName})captureParam;");
            }

            // Anonymous types always use inline lambda (cannot use pre-built expressions)
            sb.AppendLine($"    var converted = matchedQuery.Select({LambdaParameterName} => new");
        }
        else
        {
            // Generate method without capture parameter
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>("
            );
            sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, TResult> selector)");
            sb.AppendLine($"{{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{sourceTypeFullName}>;"
            );
            
            // Anonymous types always use inline lambda (cannot use pre-built expressions)
            sb.AppendLine($"    var converted = matchedQuery.Select({LambdaParameterName} => new");
        }

        sb.AppendLine($"    {{");

        // Generate property assignments
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignment(prop, CodeFormatter.IndentSize * 2);
                return $"{CodeFormatter.Indent(2)}{prop.Name} = {assignment}";
            })
            .ToList();
        sb.AppendLine(string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments));
        sb.AppendLine("    });");
        
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine("}");
        sb.AppendLine();
        return sb.ToString();
    }
}
