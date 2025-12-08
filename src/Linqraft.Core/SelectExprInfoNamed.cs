using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
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
        return DtoStructure.AnalyzeNamedType(
            ObjectCreation,
            SemanticModel,
            SourceType,
            configuration: Configuration
        )!;
    }

    /// <summary>
    /// Gets the DTO class name (uses the source type name)
    /// </summary>
    protected override string GetClassName(DtoStructure structure) => structure.SourceTypeName;

    /// <summary>
    /// Gets the parent DTO class name (fully qualified)
    /// </summary>
    protected override string GetParentDtoClassName(DtoStructure structure) =>
        structure.SourceTypeFullName;

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
        
        // Check if we should use pre-built expressions (only for IQueryable, not IEnumerable)
        var usePrebuildExpression = Configuration.UsePrebuildExpression && !IsEnumerableInvocation();
        
        // Generate static field for cached expression if pre-build is enabled
        if (usePrebuildExpression)
        {
            var (fieldDecl, _) = ExpressionTreeBuilder.GenerateExpressionTreeField(
                querySourceTypeFullName,
                dtoName,
                id
            );
            sb.AppendLine(fieldDecl);
            sb.AppendLine();
        }
        
        sb.AppendLine(GenerateMethodHeaderPart(dtoName, location));

        // Determine if we have capture parameters
        var hasCapture = CaptureArgumentExpression != null && CaptureArgumentType != null;

        if (hasCapture)
        {
            // Generate method with capture parameter that creates closure variables
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>("
            );
            sb.AppendLine(
                $"    this {returnTypePrefix}<TIn> query, Func<TIn, TResult> selector, object captureParam)"
            );
            sb.AppendLine("{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{querySourceTypeFullName}>;"
            );

            // For anonymous types, use dynamic to extract properties as closure variables
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

            // Note: Pre-built expressions don't work well with captures because the closure
            // variables would be captured at compile time, not at runtime. So we disable
            // pre-built expressions when captures are used.
            sb.AppendLine(
                $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoName}"
            );
        }
        else
        {
            // Generate method without capture parameter
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>("
            );
            sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, TResult> selector)");
            sb.AppendLine("{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{querySourceTypeFullName}>;"
            );
            
            // Use pre-built expression if enabled
            if (usePrebuildExpression)
            {
                var (_, fieldName) = ExpressionTreeBuilder.GenerateExpressionTreeField(
                    querySourceTypeFullName,
                    dtoName,
                    id
                );
                
                // Build the lambda body
                var lambdaBodyBuilder = new StringBuilder();
                lambdaBodyBuilder.AppendLine($"new {dtoName}");
                lambdaBodyBuilder.AppendLine($"    {{");
                var propertyAssignments = structure
                    .Properties.Select(prop =>
                    {
                        var assignment = GeneratePropertyAssignment(prop, CodeFormatter.IndentSize * 2);
                        return $"{CodeFormatter.Indent(2)}{prop.Name} = {assignment}";
                    })
                    .ToList();
                lambdaBodyBuilder.AppendLine(string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments));
                lambdaBodyBuilder.Append("    }");
                
                // Generate the expression initialization code
                var initCode = ExpressionTreeBuilder.GenerateNamedExpressionTreeInitialization(
                    lambdaBodyBuilder.ToString(),
                    querySourceTypeFullName,
                    dtoName,
                    LambdaParameterName,
                    fieldName
                );
                sb.Append(initCode);
                
                // Use the cached expression
                sb.AppendLine($"    var converted = matchedQuery.Select({fieldName}!);");
            }
            else
            {
                sb.AppendLine(
                    $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoName}"
                );
            }
        }

        // Only generate the lambda body if we're not using pre-built expressions (or if we have captures)
        if (!usePrebuildExpression || hasCapture)
        {
            sb.AppendLine($"    {{");

            // Generate property assignments using GeneratePropertyAssignment to properly handle null-conditional operators
            var propertyAssignments = structure
                .Properties.Select(prop =>
                {
                    var assignment = GeneratePropertyAssignment(prop, CodeFormatter.IndentSize * 2);
                    return $"{CodeFormatter.Indent(2)}{prop.Name} = {assignment}";
                })
                .ToList();
            sb.AppendLine(string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments));

            sb.AppendLine($"    }});");
        }
        
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
