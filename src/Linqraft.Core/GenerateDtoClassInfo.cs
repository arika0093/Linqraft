using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Linqraft.Core.RoslynHelpers;

namespace Linqraft.Core;

/// <summary>
/// Contains information needed to generate a DTO class
/// </summary>
public class GenerateDtoClassInfo
{
    /// <summary>
    /// The structure of the DTO (properties and their types)
    /// </summary>
    public required DtoStructure Structure { get; set; }

    /// <summary>
    /// The accessibility modifier for the DTO class (e.g., "public", "internal")
    /// </summary>
    public required string Accessibility { get; set; }

    /// <summary>
    /// The simple name of the DTO class
    /// </summary>
    public required string ClassName { get; set; }

    /// <summary>
    /// The namespace where the DTO class will be placed
    /// </summary>
    public required string Namespace { get; set; }

    /// <summary>
    /// Information about nested DTO classes that this DTO depends on
    /// </summary>
    public required ImmutableList<GenerateDtoClassInfo> NestedClasses { get; set; }

    /// <summary>
    /// Parent class names in order from outermost to innermost (empty if DTO is not nested)
    /// </summary>
    public List<string> ParentClasses { get; set; } = [];

    /// <summary>
    /// Parent class accessibilities in order from outermost to innermost (empty if DTO is not nested)
    /// Must have the same length as ParentClasses
    /// </summary>
    public List<string> ParentAccessibilities { get; set; } = [];

    /// <summary>
    /// Set of property names that already exist in a predefined partial class (should not be generated)
    /// </summary>
    public HashSet<string> ExistingProperties { get; set; } = new();

    /// <summary>
    /// Gets the fully qualified name of the DTO class
    /// </summary>
    public string FullName
    {
        get
        {
            if (string.IsNullOrEmpty(Namespace))
            {
                // Global namespace: no namespace prefix
                return ParentClasses.Count > 0
                    ? $"{string.Join(".", ParentClasses)}.{ClassName}"
                    : ClassName;
            }

            return ParentClasses.Count > 0
                ? $"{Namespace}.{string.Join(".", ParentClasses)}.{ClassName}"
                : $"{Namespace}.{ClassName}";
        }
    }

    /// <summary>
    /// Builds the C# source code for this DTO class
    /// </summary>
    /// <param name="configuration">Configuration for code generation</param>
    /// <returns>The generated C# code as a string</returns>
    public string BuildCode(LinqraftConfiguration configuration)
    {
        var sb = new StringBuilder();

        // Determine if we're generating a record or class
        var typeKeyword = configuration.RecordGenerate ? "record" : "class";

        // Determine the property accessor pattern
        var propertyAccessor = GetPropertyAccessorString(
            configuration.GetEffectivePropertyAccessor()
        );

        // Determine the required keyword
        var requiredKeyword = configuration.HasRequired ? "required " : "";

        // Build nested parent classes if they exist
        if (ParentClasses.Count > 0)
        {
            for (int i = 0; i < ParentClasses.Count; i++)
            {
                var indent = CodeFormatter.Indent(i);
                // Use the parent class accessibility if available, otherwise default to public
                var parentAccessibility =
                    i < ParentAccessibilities.Count ? ParentAccessibilities[i] : "public";
                sb.AppendLine($"{indent}{parentAccessibility} partial class {ParentClasses[i]}");
                sb.AppendLine($"{indent}{{");
            }
        }

        // Build the actual DTO class/record
        var classIndent = CodeFormatter.Indent(ParentClasses.Count);
        sb.AppendLine($"{classIndent}{Accessibility} partial {typeKeyword} {ClassName}");
        sb.AppendLine($"{classIndent}{{");

        foreach (var prop in Structure.Properties)
        {
            // Skip properties that already exist in the predefined partial class
            if (ExistingProperties.Contains(prop.Name))
            {
                continue;
            }

            var propertyType = prop.TypeName;

            // For nested structures, recursively generate DTOs (add first)
            if (prop.NestedStructure is not null)
            {
                var nestStructure = prop.NestedStructure;

                // Try to find nested class info by full name match
                // Use BestName (which prefers HintName if available) for better class naming (issue #155)
                var nestedClassName = $"{nestStructure.BestName}Dto_{nestStructure.GetUniqueId()}";
                var containedNestClasses = NestedClasses.FirstOrDefault(nc =>
                    nc.ClassName == nestedClassName
                );

                string nestedDtoFullName;
                if (containedNestClasses != null)
                {
                    nestedDtoFullName = containedNestClasses.FullName;
                }
                else
                {
                    // Fallback: construct the full name based on the current namespace
                    nestedDtoFullName = string.IsNullOrEmpty(Namespace)
                        ? nestedClassName
                        : $"{Namespace}.{nestedClassName}";
                }

                // Handle nullable types: temporarily remove the ? suffix if present
                // This is needed for ternary operators like: p.Child != null ? new { ... } : null
                // which produce types like: global::<anonymous type: ...>?
                var isTypeNullable = RoslynTypeHelper.IsNullableTypeByString(propertyType);
                var typeWithoutNullable = isTypeNullable
                    ? RoslynTypeHelper.RemoveNullableSuffixFromString(propertyType)
                    : propertyType;

                // Determine whether to re-apply nullable marker
                // Only re-apply if both the original type was nullable AND prop.IsNullable is true
                // This handles the case where a collection with nullable access uses Enumerable.Empty as fallback
                // and should not be nullable (prop.IsNullable is set to false in DtoProperty.AnalyzeExpression)
                var shouldReapplyNullable = isTypeNullable && prop.IsNullable;

                // Check if this is a direct anonymous type (not wrapped in a collection)
                // Anonymous types from Roslyn look like: global::<anonymous type: ...>
                // Collection of anonymous types look like: List<anonymous type> or IEnumerable<anonymous type>
                if (typeWithoutNullable.StartsWith("global::<anonymous"))
                {
                    // Direct anonymous type (e.g., from .Select(...).FirstOrDefault())
                    // Replace the entire anonymous type with the generated DTO class name
                    propertyType = $"global::{nestedDtoFullName}";
                    // Re-apply nullable marker if it was present in the original type and should remain nullable
                    if (shouldReapplyNullable)
                    {
                        propertyType = $"{propertyType}?";
                    }
                }
                else if (RoslynTypeHelper.IsGenericTypeByString(typeWithoutNullable))
                {
                    // Collection type (e.g., List<...>, IEnumerable<...>)
                    // Extract the base collection type and replace the element type
                    var baseType = typeWithoutNullable[..typeWithoutNullable.IndexOf("<")];
                    propertyType = $"{baseType}<{nestedDtoFullName}>";
                    // Re-apply nullable marker if it was present in the original type and should remain nullable
                    if (shouldReapplyNullable)
                    {
                        propertyType = $"{propertyType}?";
                    }
                }
                else
                {
                    // Single item, non-anonymous type (shouldn't happen often, but handle it)
                    propertyType = $"global::{nestedDtoFullName}";
                    // Re-apply nullable marker if it was present in the original type and should remain nullable
                    if (shouldReapplyNullable)
                    {
                        propertyType = $"{propertyType}?";
                    }
                }
            }

            // Add nullable annotation if the property is nullable and not already marked
            if (prop.IsNullable && !RoslynTypeHelper.IsNullableTypeByString(propertyType))
            {
                propertyType = $"{propertyType}?";
            }

            // Use property-specific accessibility if available, otherwise default to public
            var propAccessibility = prop.Accessibility ?? "public";

            // Only use 'required' if property is at least as visible as the class
            // This prevents CS9032 error (required member cannot be less visible than containing type)
            var propRequiredKeyword = ShouldUseRequired(
                configuration,
                propAccessibility,
                Accessibility
            )
                ? "required "
                : "";

            sb.AppendLine(
                $"{classIndent}    {propAccessibility} {propRequiredKeyword}{propertyType} {prop.Name} {{ {propertyAccessor} }}"
            );
        }
        sb.AppendLine($"{classIndent}}}");

        // Close parent classes if they exist
        if (ParentClasses.Count > 0)
        {
            for (int i = ParentClasses.Count - 1; i >= 0; i--)
            {
                var indent = CodeFormatter.Indent(i);
                sb.AppendLine($"{indent}}}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the property accessor string for the given accessor type
    /// </summary>
    private static string GetPropertyAccessorString(PropertyAccessor accessor)
    {
        return accessor switch
        {
            PropertyAccessor.GetAndSet => "get; set;",
            PropertyAccessor.GetAndInit => "get; init;",
            PropertyAccessor.GetAndInternalSet => "get; internal set;",
            _ => "get; set;", // Default fallback
        };
    }

    /// <summary>
    /// Determines if the 'required' keyword should be used for a property based on visibility
    /// Required members cannot be less visible than the containing type
    /// </summary>
    private static bool ShouldUseRequired(
        LinqraftConfiguration configuration,
        string propertyAccessibility,
        string classAccessibility
    )
    {
        // If required is not configured, don't use it
        if (!configuration.HasRequired)
            return false;

        // Get visibility levels (higher = more visible)
        var propLevel = GetAccessibilityLevel(propertyAccessibility);
        var classLevel = GetAccessibilityLevel(classAccessibility);

        // Property must be at least as visible as the class
        return propLevel >= classLevel;
    }

    /// <summary>
    /// Gets the visibility level of an accessibility modifier (higher = more visible)
    /// </summary>
    private static int GetAccessibilityLevel(string accessibility)
    {
        return accessibility switch
        {
            "public" => 5,
            "protected internal" => 4,
            "protected" => 3,
            "internal" => 2,
            "private protected" => 1,
            "private" => 0,
            _ => 5, // Default to public
        };
    }
}
