using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Linqraft.Core.RoslynHelpers;
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
        // Check if we need IGrouping-specific handling
        if (IsIGroupingWithAnonymousKey())
        {
            return GenerateSelectExprMethodForIGrouping(dtoName, structure, location);
        }

        return GenerateSelectExprMethodStandard(dtoName, structure, location);
    }

    /// <summary>
    /// Generates SelectExpr method for standard cases (non-IGrouping with anonymous key)
    /// </summary>
    private string GenerateSelectExprMethodStandard(
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
            sb.AppendLine(
                $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoName}"
            );
        }

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
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Generates SelectExpr method specifically for IGrouping with anonymous key types.
    /// Uses generic TKey and TElement type parameters instead of expanding the anonymous type.
    /// For IGrouping, we pass through the selector directly without transformation.
    /// </summary>
    private string GenerateSelectExprMethodForIGrouping(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var returnTypePrefix = GetReturnTypePrefix();
        var sb = new StringBuilder();

        var id = GetUniqueId();
        sb.AppendLine(GenerateMethodHeaderPart($"{dtoName} (IGrouping)", location));

        // Determine if we have capture parameters
        var hasCapture = CaptureArgumentExpression != null && CaptureArgumentType != null;

        if (hasCapture)
        {
            // Generate method with capture parameter using generic IGrouping signature
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TKey, TElement, TResult>("
            );
            sb.AppendLine(
                $"    this {returnTypePrefix}<global::System.Linq.IGrouping<TKey, TElement>> query, Func<global::System.Linq.IGrouping<TKey, TElement>, TResult> selector, object captureParam)"
            );
            sb.AppendLine("{");

            // For anonymous capture types, use dynamic to extract properties as closure variables
            var isAnonymousType =
                CaptureArgumentType != null && CaptureArgumentType.IsAnonymousType;
            if (isAnonymousType && CaptureArgumentType != null)
            {
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
                var captureTypeName =
                    CaptureArgumentType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    ?? "object";
                sb.AppendLine($"    var capture = ({captureTypeName})captureParam;");
            }

            // For IGrouping, pass through the selector directly - no need to transform
            // Use Select on the query and convert back to IQueryable
            sb.AppendLine($"    return query.Select(selector).AsQueryable();");
        }
        else
        {
            // Generate method without capture parameter using generic IGrouping signature
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TKey, TElement, TResult>("
            );
            sb.AppendLine($"    this {returnTypePrefix}<global::System.Linq.IGrouping<TKey, TElement>> query, Func<global::System.Linq.IGrouping<TKey, TElement>, TResult> selector)");
            sb.AppendLine("{");
            // For IGrouping, pass through the selector directly - no need to transform
            // Use Select on the query and convert back to IQueryable
            sb.AppendLine($"    return query.Select(selector).AsQueryable();");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
