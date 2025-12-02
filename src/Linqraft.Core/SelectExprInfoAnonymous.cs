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

    // Generate SelectExpr method
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
        var sourceTypeFullName = structure.SourceTypeFullName;
        var returnTypePrefix = GetReturnTypePrefix();
        var sb = new StringBuilder();

        var id = GetUniqueId();
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
            sb.AppendLine("{");
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
                // For non-anonymous types, just cast it
                var captureTypeName =
                    CaptureArgumentType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    ?? "object";
                sb.AppendLine($"    var capture = ({captureTypeName})captureParam;");
            }

            sb.AppendLine($"    var converted = matchedQuery.Select({LambdaParameterName} => new");
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
                $"    var matchedQuery = query as object as {returnTypePrefix}<{sourceTypeFullName}>;"
            );
            sb.AppendLine($"    var converted = matchedQuery.Select({LambdaParameterName} => new");
        }

        sb.AppendLine("    {");

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
        sb.AppendLine(GenerateMethodHeaderPart("anonymous type (IGrouping)", location));

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
            sb.AppendLine("        return new");
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
            sb.AppendLine("        return new");
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
        sb.AppendLine();
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
