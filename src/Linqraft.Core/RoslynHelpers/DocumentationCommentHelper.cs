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
        public string? SourceReference
        {
            get;
            set => field = value?.Replace(" ", "");
        }

        /// <summary>
        /// Returns true if there is any documentation to output
        /// </summary>
        public bool HasDocumentation =>
            !string.IsNullOrEmpty(Summary) || !string.IsNullOrEmpty(SourceReference);
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
        return new DocumentationInfo { Summary = summary, SourceReference = typeSymbol.Name };
    }

    /// <summary>
    /// Extracts documentation information from a property symbol
    /// </summary>
    /// <param name="propertySymbol">The property symbol to extract documentation from</param>
    /// <param name="containingTypeName">The name of the containing type for source reference (used in display path)</param>
    /// <returns>Documentation information</returns>
    public static DocumentationInfo GetPropertyDocumentation(
        IPropertySymbol? propertySymbol,
        string? containingTypeName = null
    )
    {
        if (propertySymbol == null)
            return new DocumentationInfo();

        var summary = GetSummaryFromSymbol(propertySymbol);
        // For source reference (display path), use the provided containing type name
        var displayTypeName = containingTypeName ?? propertySymbol.ContainingType?.Name ?? "";
        var sourceRef = string.IsNullOrEmpty(displayTypeName)
            ? propertySymbol.Name
            : $"{displayTypeName}.{propertySymbol.Name}";

        return new DocumentationInfo { Summary = summary, SourceReference = sourceRef };
    }

    /// <summary>
    /// Extracts documentation information from a field symbol
    /// </summary>
    /// <param name="fieldSymbol">The field symbol to extract documentation from</param>
    /// <param name="containingTypeName">The name of the containing type for source reference (used in display path)</param>
    /// <returns>Documentation information</returns>
    public static DocumentationInfo GetFieldDocumentation(
        IFieldSymbol? fieldSymbol,
        string? containingTypeName = null
    )
    {
        if (fieldSymbol == null)
            return new DocumentationInfo();

        var summary = GetSummaryFromSymbol(fieldSymbol);

        // For source reference (display path), use the provided containing type name
        var displayTypeName = containingTypeName ?? fieldSymbol.ContainingType?.Name ?? "";
        var sourceRef = string.IsNullOrEmpty(displayTypeName)
            ? fieldSymbol.Name
            : $"{displayTypeName}.{fieldSymbol.Name}";

        return new DocumentationInfo { Summary = summary, SourceReference = sourceRef };
    }

    /// <summary>
    /// Gets the summary text from a symbol by checking:
    /// 1. XML documentation (/// <summary>)
    /// 2. [Comment("...")] attribute
    /// 3. [Display(Name="...")] attribute
    /// 4. Regular comments (// comment) from leading trivia
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
                a.AttributeClass?.Name == "CommentAttribute" || a.AttributeClass?.Name == "Comment"
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
                a.AttributeClass?.Name == "DisplayAttribute" || a.AttributeClass?.Name == "Display"
            );
        if (displayAttr != null)
        {
            // Check named argument "Name"
            var nameArg = displayAttr.NamedArguments.FirstOrDefault(na => na.Key == "Name");
            if (nameArg.Value.Value is string displayName && !string.IsNullOrEmpty(displayName))
                return displayName;
        }

        // 4. Try to get from regular comments (// comment) in leading trivia
        var regularComment = ExtractRegularComment(symbol);
        if (!string.IsNullOrEmpty(regularComment))
            return regularComment;

        return null;
    }

    /// <summary>
    /// Extracts regular single-line comments (// comment) from the symbol's syntax
    /// </summary>
    private static string? ExtractRegularComment(ISymbol symbol)
    {
        // Get the declaring syntax reference
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            return null;

        var syntax = syntaxRef.GetSyntax();
        if (syntax == null)
            return null;

        // Get leading trivia (comments before the declaration)
        var leadingTrivia = syntax.GetLeadingTrivia();
        var comments = new List<string>();

        foreach (var trivia in leadingTrivia)
        {
            // Check for single-line comments
            if (trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia))
            {
                var commentText = trivia.ToString();
                // Remove the // prefix and trim
                if (commentText.StartsWith("//"))
                {
                    var text = commentText[2..].Trim();
                    if (!string.IsNullOrEmpty(text))
                        comments.Add(text);
                }
            }
        }

        // Return concatenated comments if any found
        return comments.Count > 0 ? string.Join(" ", comments) : null;
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
            // Clean up the content: remove leading whitespace and trim, join without extra spaces
            var lines = content
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line));
            return string.Join("", lines);
        }

        return null;
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
                    var member = namedType
                        .GetMembers()
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
                remarksParts.Add($"From: <c>{EscapeXmlContent(info.SourceReference!)}</c>");
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
    /// Escapes special characters for XML content
    /// </summary>
    private static string EscapeXmlContent(string content)
    {
        return content.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
