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
    /// Uses dynamic to access anonymous key properties in the Select.
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

        // Get the element type from IGrouping<TKey, TElement>
        var elementType = RoslynTypeHelper.GetIGroupingElementType(SourceType);
        var elementTypeName = elementType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object";

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
                sb.AppendLine("    dynamic captureObj = captureParam;");
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

            // Use AsEnumerable to allow dynamic access, then Select with cast for each grouping element
            sb.AppendLine($"    var converted = query.AsEnumerable().Select({LambdaParameterName} =>");
            sb.AppendLine("    {");
            sb.AppendLine($"        var groupingElements = {LambdaParameterName}.Cast<{elementTypeName}>();");
            sb.AppendLine($"        return new {dtoName}");
        }
        else
        {
            // Generate method without capture parameter using generic IGrouping signature
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TKey, TElement, TResult>("
            );
            sb.AppendLine($"    this {returnTypePrefix}<global::System.Linq.IGrouping<TKey, TElement>> query, Func<global::System.Linq.IGrouping<TKey, TElement>, TResult> selector)");
            sb.AppendLine("{");
            // Use AsEnumerable to allow dynamic access, then Select with cast for each grouping element
            sb.AppendLine($"    var converted = query.AsEnumerable().Select({LambdaParameterName} =>");
            sb.AppendLine("    {");
            sb.AppendLine($"        var groupingElements = {LambdaParameterName}.Cast<{elementTypeName}>();");
            sb.AppendLine($"        return new {dtoName}");
        }

        sb.AppendLine("        {");

        // Generate property assignments with dynamic access for Key properties
        // but use groupingElements for element access
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignmentForIGroupingWithCast(prop, CodeFormatter.IndentSize * 3);
                return $"{CodeFormatter.Indent(3)}{prop.Name} = {assignment}";
            })
            .ToList();
        sb.AppendLine(string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments));

        sb.AppendLine("        };");
        sb.AppendLine("    }).AsQueryable();");
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Generates property assignment code for IGrouping with anonymous key and element cast.
    /// Converts x.Key.PropertyName to (PropertyType)((dynamic)x.Key).PropertyName for anonymous key properties.
    /// Converts x.Sum(e => e.Value) to groupingElements.Sum(e => e.Value) for element aggregations.
    /// </summary>
    private string GeneratePropertyAssignmentForIGroupingWithCast(DtoProperty property, int indents)
    {
        // Get the base assignment using the standard method
        var assignment = GeneratePropertyAssignment(property, indents);

        // Check if assignment accesses Key properties and needs dynamic cast
        var keyAccessPattern = $"{LambdaParameterName}.Key.";
        var needsDynamicCast = assignment.Contains(keyAccessPattern);

        if (needsDynamicCast)
        {
            // Replace x.Key.PropertyName patterns with ((dynamic)x.Key).PropertyName
            // This handles the case where TKey is an anonymous type
            assignment = assignment.Replace(
                keyAccessPattern,
                $"((dynamic){LambdaParameterName}.Key)."
            );

            // Cast the result back to the expected type to ensure anonymous type compatibility
            // Dynamic access returns object, but we need the original type for anonymous type matching
            var propertyTypeName = property.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            assignment = $"({propertyTypeName}){assignment}";
        }

        // Replace direct calls on the grouping (like g.Sum, g.Count, g.FirstOrDefault)
        // with calls on groupingElements
        var groupingMethodPatterns = new[] { ".Sum(", ".Count(", ".FirstOrDefault(", ".LastOrDefault(", ".First(", ".Last(", ".Any(", ".All(", ".Average(", ".Min(", ".Max(" };
        foreach (var pattern in groupingMethodPatterns)
        {
            var fullPattern = $"{LambdaParameterName}{pattern}";
            if (assignment.Contains(fullPattern))
            {
                assignment = assignment.Replace(fullPattern, $"groupingElements{pattern}");
            }
        }

        // Handle Count() without arguments - it could be g.Count()
        if (assignment.Contains($"{LambdaParameterName}.Count()"))
        {
            assignment = assignment.Replace($"{LambdaParameterName}.Count()", "groupingElements.Count()");
        }

        return assignment;
    }
}
