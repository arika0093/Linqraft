using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Linqraft;

internal class GenerateDtoClassInfo
{
    public required DtoStructure Structure { get; set; }

    public required string Accessibility { get; set; }

    public required string ClassName { get; set; }

    public required string Namespace { get; set; }

    public required ImmutableList<GenerateDtoClassInfo> NestedClasses { get; set; }

    public string FullName => $"{Namespace}.{ClassName}";

    public string BuildCode()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Accessibility} partial class {ClassName}");
        sb.AppendLine("{");

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

            sb.AppendLine($"    public required {propertyType} {prop.Name} {{ get; set; }}");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
