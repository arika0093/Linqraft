using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// SelectExprInfo for explicit DTO name Select expressions (SelectExpr&lt;TIn, TResult&gt; form)
/// </summary>
public record SelectExprInfoExplicitDto : SelectExprInfo
{
    /// <summary>
    /// The anonymous object creation expression containing the property selections
    /// </summary>
    public required AnonymousObjectCreationExpressionSyntax AnonymousObject { get; init; }

    /// <summary>
    /// The explicit name for the DTO (from TResult type parameter)
    /// </summary>
    public required string ExplicitDtoName { get; init; }

    /// <summary>
    /// The target namespace where the DTO will be generated
    /// </summary>
    public required string TargetNamespace { get; init; }

    /// <summary>
    /// Parent class names in order from outermost to innermost (empty if DTO is not nested)
    /// </summary>
    public required List<string> ParentClasses { get; init; }

    /// <summary>
    /// The ITypeSymbol of the TResult type (for extracting accessibility)
    /// </summary>
    public required ITypeSymbol TResultType { get; init; }

    /// <summary>
    /// Generates DTO classes (including nested DTOs)
    /// </summary>
    public override List<GenerateDtoClassInfo> GenerateDtoClasses()
    {
        var structure = GenerateDtoStructure();
        var parentClassName = GetParentDtoClassName(structure);
        return GenerateDtoClasses(structure, parentClassName);
    }

    private List<GenerateDtoClassInfo> GenerateDtoClasses(
        DtoStructure structure,
        string? overrideClassName = null,
        List<string>? nestedParentClasses = null,
        List<string>? nestedParentAccessibilities = null,
        bool isExplicitFromNestedSelectExpr = false
    )
    {
        var result = new List<GenerateDtoClassInfo>();
        // Extract the actual accessibility from TResultType
        var accessibility = GetAccessibilityString(TResultType);
        var className = overrideClassName ?? GetClassName(structure);

        // Determine parent classes for nested DTOs
        var currentParentClasses = nestedParentClasses ?? ParentClasses;
        var currentParentAccessibilities =
            nestedParentAccessibilities ?? GetParentAccessibilities();

        // Get existing properties from the TResultType (only for the main DTO, not nested)
        var existingProperties = new HashSet<string>();
        var isMainDto = overrideClassName == ExplicitDtoName;
        // DTOs from nested SelectExpr with explicit type should also be treated as explicit root DTOs
        var isExplicitDto = isMainDto || isExplicitFromNestedSelectExpr;
        if (isMainDto)
        {
            // This is the main DTO, check for existing properties
            var properties = TResultType.GetMembers().OfType<IPropertySymbol>();
            foreach (var property in properties)
            {
                existingProperties.Add(property.Name);
            }
        }

        // Nested DTOs are placed at the same level as the current DTO, not inside it
        // So they share the same parent classes
        foreach (var prop in structure.Properties)
        {
            // Only generate nested DTO classes for anonymous types, not for named types
            // Named types should preserve the original type, not create DTOs
            if (prop.NestedStructure is not null && !prop.IsNestedFromNamedType)
            {
                // Check if this property has an explicit DTO type name from a nested SelectExpr
                // If so, use that name instead of auto-generating one
                string? explicitDtoClassName = null;
                bool propHasExplicitNestedSelectExpr = false;
                if (!string.IsNullOrEmpty(prop.ExplicitNestedDtoTypeName))
                {
                    const string GlobalPrefix = "global::";

                    // Remove "global::" prefix if present before extracting class name
                    var typeName = prop.ExplicitNestedDtoTypeName!;
                    if (typeName.StartsWith(GlobalPrefix))
                    {
                        typeName = typeName.Substring(GlobalPrefix.Length);
                    }

                    // Extract just the class name from the fully qualified name
                    // e.g., "Linqraft.Tests.NestedItem207Dto" -> "NestedItem207Dto"
                    var lastDotIndex = typeName.LastIndexOf('.');
                    if (lastDotIndex >= 0)
                    {
                        explicitDtoClassName = typeName.Substring(lastDotIndex + 1);
                    }
                    else
                    {
                        explicitDtoClassName = typeName;
                    }
                    propHasExplicitNestedSelectExpr = true;
                }

                // Recursively generate nested DTO classes with the same parent info
                result.AddRange(
                    GenerateDtoClasses(
                        prop.NestedStructure,
                        overrideClassName: explicitDtoClassName,
                        nestedParentClasses: currentParentClasses,
                        nestedParentAccessibilities: currentParentAccessibilities,
                        isExplicitFromNestedSelectExpr: propHasExplicitNestedSelectExpr
                    )
                );
            }
        }
        // Generate current DTO class
        // Use GetActualDtoNamespace() to handle global namespace correctly
        var actualNamespace = GetActualDtoNamespace();

        // When NestedDtoUseHashNamespace option is enabled, child DTOs are placed in
        // a hash-named sub-namespace (e.g., LinqraftGenerated_{Hash}) WITHOUT parent class nesting
        // However, DTOs with explicit names from nested SelectExpr should NOT use hash namespace
        // and should maintain their parent class structure
        List<string> finalParentClasses = currentParentClasses;
        List<string> finalParentAccessibilities = currentParentAccessibilities;
        
        if (!isExplicitDto && Configuration?.NestedDtoUseHashNamespace == true)
        {
            var hash = structure.GetUniqueId();
            actualNamespace = string.IsNullOrEmpty(actualNamespace)
                ? $"LinqraftGenerated_{hash}"
                : $"{actualNamespace}.LinqraftGenerated_{hash}";
            
            // Implicit DTOs in hash namespace should NOT be nested inside parent classes
            // They are managed by the hash, so they don't need to exist within a class
            finalParentClasses = [];
            finalParentAccessibilities = [];
        }

        var dtoClassInfo = new GenerateDtoClassInfo
        {
            Accessibility = accessibility,
            Namespace = actualNamespace,
            ClassName = className,
            Structure = structure,
            NestedClasses = [.. result],
            ParentClasses = finalParentClasses,
            ParentAccessibilities = finalParentAccessibilities,
            ExistingProperties = existingProperties,
            IsExplicitRootDto = isExplicitDto, // Mark explicit DTOs (main or from nested SelectExpr) to avoid adding the attribute
        };
        result.Add(dtoClassInfo);
        return result;
    }

    /// <summary>
    /// Gets parent class accessibilities from TResultType
    /// </summary>
    private List<string> GetParentAccessibilities()
    {
        var accessibilities = new List<string>();
        var currentType = TResultType.ContainingType;

        // Traverse up the containing types to get all parent accessibilities
        while (currentType != null)
        {
            accessibilities.Insert(0, GetAccessibilityString(currentType));
            currentType = currentType.ContainingType;
        }

        return accessibilities;
    }

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
    /// Extracts property accessibilities from the TResult type (if it exists as a partial class)
    /// </summary>
    private Dictionary<string, string> ExtractPropertyAccessibilities()
    {
        var accessibilities = new Dictionary<string, string>();

        // Get all properties from the TResultType
        var properties = TResultType.GetMembers().OfType<IPropertySymbol>();

        foreach (var property in properties)
        {
            var accessibility = GetAccessibilityString(property);
            accessibilities[property.Name] = accessibility;
        }

        return accessibilities;
    }

    /// <summary>
    /// Gets the accessibility string from a property symbol
    /// </summary>
    private string GetAccessibilityString(IPropertySymbol propertySymbol)
    {
        return propertySymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "public", // Default to public
        };
    }

    /// <summary>
    /// Gets the DTO class name
    /// Uses BestName (which prefers HintName if available) for better class naming (issue #155)
    /// When NestedDtoUseHashNamespace is enabled, the class name is just "{BestName}Dto"
    /// Otherwise, it includes the hash suffix: "{BestName}Dto_{hash}"
    /// </summary>
    protected override string GetClassName(DtoStructure structure)
    {
        if (Configuration?.NestedDtoUseHashNamespace == true)
        {
            return $"{structure.BestName}Dto";
        }
        return $"{structure.BestName}Dto_{structure.GetUniqueId()}";
    }

    /// <summary>
    /// Gets the parent DTO class name
    /// </summary>
    protected override string GetParentDtoClassName(DtoStructure structure) => ExplicitDtoName;

    /// <summary>
    /// Gets the namespace where DTOs will be placed
    /// </summary>
    protected override string GetDtoNamespace() => GetActualDtoNamespace();

    // Get expression type string (for documentation)
    protected override string GetExprTypeString() => "explicit";

    /// <summary>
    /// Gets the full name for a nested DTO class using the structure.
    /// When NestedDtoUseHashNamespace is enabled, includes the LinqraftGenerated_{hash} namespace
    /// WITHOUT parent class nesting (implicit DTOs are managed by hash, not class hierarchy).
    /// </summary>
    protected override string GetNestedDtoFullNameFromStructure(DtoStructure nestedStructure)
    {
        var className = GetClassName(nestedStructure);
        if (string.IsNullOrEmpty(className))
            return "";

        var actualNamespace = GetActualDtoNamespace();

        // When NestedDtoUseHashNamespace option is enabled, include LinqraftGenerated_{hash} in namespace
        // Implicit DTOs should NOT be nested inside parent classes - they are placed directly
        // in the LinqraftGenerated_{hash} namespace
        if (Configuration?.NestedDtoUseHashNamespace == true)
        {
            var hash = nestedStructure.GetUniqueId();
            var generatedNamespace = string.IsNullOrEmpty(actualNamespace)
                ? $"LinqraftGenerated_{hash}"
                : $"{actualNamespace}.LinqraftGenerated_{hash}";

            // Implicit DTOs in hash namespace should NOT include parent classes
            return $"global::{generatedNamespace}.{className}";
        }

        // Default behavior: use GetNestedDtoFullName (includes parent classes)
        return GetNestedDtoFullName(className);
    }

    // Get the full name for a nested DTO class (including parent classes)
    protected override string GetNestedDtoFullName(string nestedClassName)
    {
        var actualNamespace = GetActualDtoNamespace();

        // Handle global namespace case
        if (string.IsNullOrEmpty(actualNamespace))
        {
            // Global namespace: no namespace prefix
            if (ParentClasses.Count > 0)
            {
                return $"global::{string.Join(".", ParentClasses)}.{nestedClassName}";
            }
            return $"global::{nestedClassName}";
        }

        // Regular namespace case
        if (ParentClasses.Count > 0)
        {
            return $"global::{actualNamespace}.{string.Join(".", ParentClasses)}.{nestedClassName}";
        }
        return $"global::{actualNamespace}.{nestedClassName}";
    }

    /// <summary>
    /// Gets the actual namespace where the DTO will be placed
    /// This mirrors the logic in SelectExprGroups.TargetNamespace getter
    /// </summary>
    private string GetActualDtoNamespace()
    {
        // Determine if this is a global namespace (same logic as SelectExprGroups.IsGlobalNamespace)
        var sourceNamespace = GetNamespaceString();
        var isGlobalNamespace =
            string.IsNullOrEmpty(sourceNamespace) || sourceNamespace.Contains("<");

        if (isGlobalNamespace)
        {
            return Configuration?.GlobalNamespace ?? "";
        }
        return TargetNamespace;
    }

    /// <summary>
    /// Generates the SelectExpr method code
    /// </summary>
    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        var sourceTypeFullName = structure.SourceTypeFullName;
        var actualNamespace = GetActualDtoNamespace();

        // Build full DTO name with parent classes if nested
        string dtoFullName;
        if (string.IsNullOrEmpty(actualNamespace))
        {
            // Global namespace: no namespace prefix
            dtoFullName =
                ParentClasses.Count > 0
                    ? $"global::{string.Join(".", ParentClasses)}.{dtoName}"
                    : $"global::{dtoName}";
        }
        else
        {
            // Regular namespace case
            dtoFullName =
                ParentClasses.Count > 0
                    ? $"global::{actualNamespace}.{string.Join(".", ParentClasses)}.{dtoName}"
                    : $"global::{actualNamespace}.{dtoName}";
        }

        var returnTypePrefix = GetReturnTypePrefix();
        var sb = new StringBuilder();

        var id = GetUniqueId();
        
        // Check if we should use pre-built expressions (only for IQueryable, not IEnumerable)
        var usePrebuildExpression = Configuration.UsePrebuildExpression && !IsEnumerableInvocation();
        
        // Generate static field for cached expression if pre-build is enabled
        if (usePrebuildExpression)
        {
            var (fieldDecl, _) = ExpressionTreeBuilder.GenerateExpressionTreeField(
                sourceTypeFullName,
                dtoFullName,
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
                $"    this {returnTypePrefix}<TIn> query, Func<TIn, object> selector, object captureParam)"
            );
            sb.AppendLine($"{{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{sourceTypeFullName}>;"
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
                $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoFullName}"
            );
        }
        else
        {
            // Generate method without capture parameter
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>("
            );
            sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, object> selector)");
            sb.AppendLine($"{{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{sourceTypeFullName}>;"
            );
            
            // Use pre-built expression if enabled
            if (usePrebuildExpression)
            {
                var (_, fieldName) = ExpressionTreeBuilder.GenerateExpressionTreeField(
                    sourceTypeFullName,
                    dtoFullName,
                    id
                );
                
                // Build the lambda body
                var lambdaBodyBuilder = new StringBuilder();
                lambdaBodyBuilder.AppendLine($"new {dtoFullName}");
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
                    sourceTypeFullName,
                    dtoFullName,
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
                    $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoFullName}"
                );
            }
        }

        // Only generate the lambda body if we're not using pre-built expressions (or if we have captures)
        if (!usePrebuildExpression || hasCapture)
        {
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

            sb.AppendLine($"    }});");
        }
        
        sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        sb.AppendLine($"}}");
        return sb.ToString();
    }
}
