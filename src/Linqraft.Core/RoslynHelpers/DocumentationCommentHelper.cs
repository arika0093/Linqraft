using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.RoslynHelpers;

/// <summary>
/// Helper class for extracting documentation comments from Roslyn symbols.
/// Supports XML documentation, Comment attribute, Display attribute, and other data annotation attributes.
/// </summary>
public static class DocumentationCommentHelper
{
    /// <summary>
    /// Represents documentation information extracted from a symbol
    /// </summary>
    public record DocumentationInfo
    {
        /// <summary>
        /// The summary text extracted from XML documentation, Comment attribute, or Display attribute
        /// </summary>
        public string? Summary { get; init; }

        /// <summary>
        /// The source expression reference (e.g., "TestData.Id")
        /// </summary>
        public string? SourceReference { get; init; }

        /// <summary>
        /// The source symbol reference for generating see cref (e.g., "TestData.Id")
        /// </summary>
        public string? SourceCref { get; init; }

        /// <summary>
        /// List of attribute names (e.g., ["Key", "Required", "StringLength(100)"])
        /// </summary>
        public List<string> Attributes { get; init; } = new();

        /// <summary>
        /// Returns true if there is any documentation to output
        /// </summary>
        public bool HasDocumentation =>
            !string.IsNullOrEmpty(Summary) ||
            !string.IsNullOrEmpty(SourceReference) ||
            Attributes.Count > 0;
    }

    /// <summary>
    /// Extracts documentation information from a type symbol (for class-level comments)
    /// </summary>
    /// <param name="typeSymbol">The type symbol to extract documentation from</param>
    /// <returns>Documentation information</returns>
    public static DocumentationInfo GetTypeDocumentation(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
            return new DocumentationInfo();

        var summary = GetSummaryFromSymbol(typeSymbol);
        var attributes = GetDataAnnotationAttributes(typeSymbol);

        return new DocumentationInfo
        {
            Summary = summary,
            SourceReference = typeSymbol.Name,
            SourceCref = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Attributes = attributes,
        };
    }

    /// <summary>
    /// Extracts documentation information from a property symbol
    /// </summary>
    /// <param name="propertySymbol">The property symbol to extract documentation from</param>
    /// <param name="containingTypeName">The name of the containing type for source reference</param>
    /// <returns>Documentation information</returns>
    public static DocumentationInfo GetPropertyDocumentation(
        IPropertySymbol? propertySymbol,
        string? containingTypeName = null
    )
    {
        if (propertySymbol == null)
            return new DocumentationInfo();

        var summary = GetSummaryFromSymbol(propertySymbol);
        var attributes = GetDataAnnotationAttributes(propertySymbol);

        var typeName = containingTypeName ?? propertySymbol.ContainingType?.Name ?? "";
        var sourceRef = string.IsNullOrEmpty(typeName)
            ? propertySymbol.Name
            : $"{typeName}.{propertySymbol.Name}";

        return new DocumentationInfo
        {
            Summary = summary,
            SourceReference = sourceRef,
            SourceCref = sourceRef,
            Attributes = attributes,
        };
    }

    /// <summary>
    /// Extracts documentation information from a field symbol
    /// </summary>
    /// <param name="fieldSymbol">The field symbol to extract documentation from</param>
    /// <param name="containingTypeName">The name of the containing type for source reference</param>
    /// <returns>Documentation information</returns>
    public static DocumentationInfo GetFieldDocumentation(
        IFieldSymbol? fieldSymbol,
        string? containingTypeName = null
    )
    {
        if (fieldSymbol == null)
            return new DocumentationInfo();

        var summary = GetSummaryFromSymbol(fieldSymbol);
        var attributes = GetDataAnnotationAttributes(fieldSymbol);

        var typeName = containingTypeName ?? fieldSymbol.ContainingType?.Name ?? "";
        var sourceRef = string.IsNullOrEmpty(typeName)
            ? fieldSymbol.Name
            : $"{typeName}.{fieldSymbol.Name}";

        return new DocumentationInfo
        {
            Summary = summary,
            SourceReference = sourceRef,
            SourceCref = sourceRef,
            Attributes = attributes,
        };
    }

    /// <summary>
    /// Gets the summary text from a symbol by checking:
    /// 1. XML documentation (/// <summary>)
    /// 2. [Comment("...")] attribute
    /// 3. [Display(Name="...")] attribute
    /// </summary>
    private static string? GetSummaryFromSymbol(ISymbol symbol)
    {
        // 1. Try to get XML documentation summary
        var xmlComment = symbol.GetDocumentationCommentXml();
        if (!string.IsNullOrEmpty(xmlComment))
        {
            var summary = ExtractSummaryFromXml(xmlComment!);
            if (!string.IsNullOrEmpty(summary))
                return summary;
        }

        // 2. Try to get from [Comment("...")] attribute
        var commentAttr = symbol
            .GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.Name == "CommentAttribute" ||
                a.AttributeClass?.Name == "Comment"
            );
        if (commentAttr?.ConstructorArguments.Length > 0)
        {
            var commentValue = commentAttr.ConstructorArguments[0].Value?.ToString();
            if (!string.IsNullOrEmpty(commentValue))
                return commentValue;
        }

        // 3. Try to get from [Display(Name="...")] attribute
        var displayAttr = symbol
            .GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.Name == "DisplayAttribute" ||
                a.AttributeClass?.Name == "Display"
            );
        if (displayAttr != null)
        {
            // Check named argument "Name"
            var nameArg = displayAttr.NamedArguments.FirstOrDefault(na => na.Key == "Name");
            if (nameArg.Value.Value is string displayName && !string.IsNullOrEmpty(displayName))
                return displayName;
        }

        return null;
    }

    /// <summary>
    /// Extracts the summary text from XML documentation comment
    /// </summary>
    private static string? ExtractSummaryFromXml(string xmlComment)
    {
        // Match <summary>...</summary> and extract content
        var match = Regex.Match(
            xmlComment,
            @"<summary>\s*(.*?)\s*</summary>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            var content = match.Groups[1].Value;
            // Clean up the content: remove leading whitespace and trim
            var lines = content.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line));
            return string.Join(" ", lines);
        }

        return null;
    }

    /// <summary>
    /// Gets data annotation attributes from a symbol that should be included in documentation
    /// </summary>
    private static List<string> GetDataAnnotationAttributes(ISymbol symbol)
    {
        var result = new List<string>();
        var attributes = symbol.GetAttributes();

        // Attributes to include (commonly used data annotations)
        var includedAttributes = new HashSet<string>
        {
            // Validation attributes
            "Key", "KeyAttribute",
            "Required", "RequiredAttribute",
            "StringLength", "StringLengthAttribute",
            "MaxLength", "MaxLengthAttribute",
            "MinLength", "MinLengthAttribute",
            "Range", "RangeAttribute",
            "RegularExpression", "RegularExpressionAttribute",
            "EmailAddress", "EmailAddressAttribute",
            "Phone", "PhoneAttribute",
            "Url", "UrlAttribute",
            "CreditCard", "CreditCardAttribute",
            "Compare", "CompareAttribute",
            // Database attributes
            "Column", "ColumnAttribute",
            "Table", "TableAttribute",
            "ForeignKey", "ForeignKeyAttribute",
            "NotMapped", "NotMappedAttribute",
            "DatabaseGenerated", "DatabaseGeneratedAttribute",
            "Timestamp", "TimestampAttribute",
            "ConcurrencyCheck", "ConcurrencyCheckAttribute",
            // JSON serialization attributes
            "JsonPropertyName", "JsonPropertyNameAttribute",
            "JsonIgnore", "JsonIgnoreAttribute",
        };

        foreach (var attr in attributes)
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName == null)
                continue;

            // Skip Comment and Display attributes (they're used for summary)
            if (attrName is "Comment" or "CommentAttribute" or "Display" or "DisplayAttribute")
                continue;

            // Check if this is an included attribute
            if (!includedAttributes.Contains(attrName))
                continue;

            // Build attribute string with arguments
            var attrString = BuildAttributeString(attr);
            if (!string.IsNullOrEmpty(attrString))
                result.Add(attrString);
        }

        return result;
    }

    /// <summary>
    /// Builds a string representation of an attribute with its arguments
    /// </summary>
    private static string BuildAttributeString(AttributeData attr)
    {
        var name = attr.AttributeClass?.Name;
        if (name == null)
            return "";

        // Remove "Attribute" suffix for cleaner output
        if (name.EndsWith("Attribute"))
            name = name.Substring(0, name.Length - 9);

        // Build argument list
        var args = new List<string>();

        // Add constructor arguments
        foreach (var arg in attr.ConstructorArguments)
        {
            args.Add(FormatTypedConstant(arg));
        }

        // Add named arguments
        foreach (var namedArg in attr.NamedArguments)
        {
            args.Add($"{namedArg.Key} = {FormatTypedConstant(namedArg.Value)}");
        }

        if (args.Count > 0)
            return $"{name}({string.Join(", ", args)})";

        return name;
    }

    /// <summary>
    /// Formats a TypedConstant for display in attribute arguments
    /// </summary>
    private static string FormatTypedConstant(TypedConstant constant)
    {
        if (constant.IsNull)
            return "null";

        switch (constant.Kind)
        {
            case TypedConstantKind.Primitive:
                if (constant.Value is string strValue)
                    return $"\"{strValue}\"";
                if (constant.Value is bool boolValue)
                    return boolValue ? "true" : "false";
                return constant.Value?.ToString() ?? "null";

            case TypedConstantKind.Enum:
                // Format as EnumType.EnumValue
                var enumType = constant.Type?.Name ?? "";
                var enumValue = constant.Value?.ToString() ?? "";
                // Try to get the enum member name
                if (constant.Type is INamedTypeSymbol namedType)
                {
                    var member = namedType.GetMembers()
                        .OfType<IFieldSymbol>()
                        .FirstOrDefault(f =>
                            f.HasConstantValue && f.ConstantValue?.Equals(constant.Value) == true
                        );
                    if (member != null)
                        return $"{enumType}.{member.Name}";
                }
                return $"{enumType}.{enumValue}";

            case TypedConstantKind.Type:
                return $"typeof({(constant.Value as ITypeSymbol)?.ToDisplayString() ?? "object"})";

            case TypedConstantKind.Array:
                var elements = constant.Values.Select(FormatTypedConstant);
                return $"new[] {{ {string.Join(", ", elements)} }}";

            default:
                return constant.Value?.ToString() ?? "null";
        }
    }

    /// <summary>
    /// Builds XML documentation comment for a class or property
    /// </summary>
    /// <param name="info">The documentation information</param>
    /// <param name="indent">The indentation string</param>
    /// <param name="mode">The comment output mode</param>
    /// <returns>The XML documentation comment string, or empty if no documentation</returns>
    public static string BuildXmlDocumentation(
        DocumentationInfo info,
        string indent,
        CommentOutputMode mode
    )
    {
        if (mode == CommentOutputMode.None)
            return "";

        var sb = new StringBuilder();
        var hasContent = false;

        // Add summary if present
        if (!string.IsNullOrEmpty(info.Summary))
        {
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// {EscapeXmlContent(info.Summary!)}");
            sb.AppendLine($"{indent}/// </summary>");
            hasContent = true;
        }

        // Add remarks with source reference and attributes (only in All mode)
        if (mode == CommentOutputMode.All)
        {
            var remarksParts = new List<string>();

            // Add source reference
            if (!string.IsNullOrEmpty(info.SourceReference))
            {
                // Only use cref if the source reference is a simple identifier path
                // (no special characters like ?, (, ), ..., etc.)
                var canUseCref = !string.IsNullOrEmpty(info.SourceCref)
                    && IsSimpleIdentifierPath(info.SourceCref!);

                if (canUseCref)
                {
                    remarksParts.Add(
                        $"From: <see cref=\"{EscapeXmlContent(info.SourceCref!)}\"><c>{EscapeXmlContent(info.SourceReference!)}</c></see>"
                    );
                }
                else
                {
                    remarksParts.Add($"From: <c>{EscapeXmlContent(info.SourceReference!)}</c>");
                }
            }

            // Add attributes
            if (info.Attributes.Count > 0)
            {
                var attributeStr = string.Join(", ", info.Attributes);
                remarksParts.Add($"Attributes: <c>[{EscapeXmlContent(attributeStr)}]</c>");
            }

            if (remarksParts.Count > 0)
            {
                sb.AppendLine($"{indent}/// <remarks>");
                for (int i = 0; i < remarksParts.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.AppendLine($"{indent}/// <br/>");
                    }
                    sb.AppendLine($"{indent}/// {remarksParts[i]}");
                }
                sb.AppendLine($"{indent}/// </remarks>");
                hasContent = true;
            }
        }

        return hasContent ? sb.ToString() : "";
    }

    /// <summary>
    /// Checks if a string is a simple identifier path (e.g., "ClassName.PropertyName")
    /// Returns false if it contains special characters like ?, (, ), ..., etc.
    /// </summary>
    private static bool IsSimpleIdentifierPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // A simple identifier path should match: ClassName or ClassName.PropertyName
        // It should only contain letters, digits, underscores, and dots (as separators)
        return Regex.IsMatch(path, @"^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$");
    }

    /// <summary>
    /// Escapes special characters for XML content
    /// </summary>
    private static string EscapeXmlContent(string content)
    {
        return content
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
