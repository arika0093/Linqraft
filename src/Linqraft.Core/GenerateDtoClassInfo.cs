using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

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
    /// Gets the fully qualified name of the DTO class
    /// </summary>
    public string FullName =>
        ParentClasses.Count > 0
            ? $"{Namespace}.{string.Join(".", ParentClasses)}.{ClassName}"
            : $"{Namespace}.{ClassName}";

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
        var propertyAccessor = GetPropertyAccessorString(configuration.GetEffectivePropertyAccessor());

        // Determine the required keyword
        var requiredKeyword = configuration.HasRequired ? "required " : "";

        // Build nested parent classes if they exist
        if (ParentClasses.Count > 0)
        {
            for (int i = 0; i < ParentClasses.Count; i++)
            {
                var indent = new string(' ', i * 4);
                // Use the parent class accessibility if available, otherwise default to public
                var parentAccessibility =
                    i < ParentAccessibilities.Count ? ParentAccessibilities[i] : "public";
                sb.AppendLine($"{indent}{parentAccessibility} partial class {ParentClasses[i]}");
                sb.AppendLine($"{indent}{{");
            }
        }

        // Build the actual DTO class/record
        var classIndent = new string(' ', ParentClasses.Count * 4);
        sb.AppendLine($"{classIndent}{Accessibility} partial {typeKeyword} {ClassName}");
        sb.AppendLine($"{classIndent}{{");

        foreach (var prop in Structure.Properties)
        {
            var propertyType = prop.TypeName;

            // For nested structures, recursively generate DTOs (add first)
            if (prop.NestedStructure is not null)
            {
                var nestStructure = prop.NestedStructure;

                // Extract the base collection type (e.g., IEnumerable from IEnumerable<T>)
                var baseType = propertyType;
                if (propertyType.Contains("<"))
                {
                    baseType = propertyType[..propertyType.IndexOf("<")];
                }

                // Try to find nested class info by full name match
                var nestedClassName =
                    $"{nestStructure.SourceTypeName}Dto_{nestStructure.GetUniqueId()}";
                var containedNestClasses = NestedClasses.FirstOrDefault(nc =>
                    nc.ClassName == nestedClassName
                );

                if (containedNestClasses is not null)
                {
                    propertyType = $"{baseType}<{containedNestClasses.FullName}>";
                }
                else
                {
                    // Fallback: use generated class name directly
                    propertyType = $"{baseType}<{Namespace}.{nestedClassName}>";
                }
            }

            // Add nullable annotation if the property is nullable
            if (prop.IsNullable && !propertyType.EndsWith("?"))
            {
                propertyType = $"{propertyType}?";
            }

            sb.AppendLine(
                $"{classIndent}    public {requiredKeyword}{propertyType} {prop.Name} {{ {propertyAccessor} }}"
            );
        }
        sb.AppendLine($"{classIndent}}}");

        // Close parent classes if they exist
        if (ParentClasses.Count > 0)
        {
            for (int i = ParentClasses.Count - 1; i >= 0; i--)
            {
                var indent = new string(' ', i * 4);
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
}
