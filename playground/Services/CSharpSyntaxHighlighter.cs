using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Linqraft.Playground.Services;

/// <summary>
/// Lightweight C# syntax highlighter using regex patterns.
/// Simple and fast for highlighting code samples with VSCode-style classes.
/// </summary>
public partial class CSharpSyntaxHighlighter
{
    // C# keywords
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
        "add",
        "and",
        "alias",
        "ascending",
        "async",
        "await",
        "by",
        "descending",
        "dynamic",
        "equals",
        "from",
        "get",
        "global",
        "group",
        "init",
        "into",
        "join",
        "let",
        "managed",
        "nameof",
        "nint",
        "not",
        "notnull",
        "nuint",
        "on",
        "or",
        "orderby",
        "partial",
        "record",
        "remove",
        "required",
        "scoped",
        "select",
        "set",
        "unmanaged",
        "value",
        "var",
        "when",
        "where",
        "with",
        "yield",
    };

    // Known types (PascalCase) that commonly appear in samples
    private static readonly HashSet<string> CommonTypes = new(StringComparer.Ordinal)
    {
        "String",
        "Int32",
        "Int64",
        "Boolean",
        "DateTime",
        "Guid",
        "List",
        "Dictionary",
        "IEnumerable",
        "IQueryable",
        "IList",
        "Task",
        "Func",
        "Action",
        "Nullable",
        "Order",
        "OrderDto",
        "Customer",
        "Product",
        "OrderItem",
        "Address",
    };

    // Known methods that commonly appear in LINQ samples
    private static readonly HashSet<string> CommonMethods = new(StringComparer.Ordinal)
    {
        "Select",
        "SelectExpr",
        "Where",
        "OrderBy",
        "GroupBy",
        "Join",
        "ToList",
        "ToArray",
        "FirstOrDefault",
        "SingleOrDefault",
        "Any",
        "All",
        "Count",
        "Sum",
        "Average",
        "Max",
        "Min",
        "AsQueryable",
        "Include",
        "ThenInclude",
    };

    /// <summary>
    /// Highlights C# code and returns HTML with VSCode-style classes.
    /// </summary>
    public string Highlight(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var result = new StringBuilder();
        var index = 0;

        while (index < code.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(code[index]))
            {
                result.Append(code[index]);
                index++;
                continue;
            }

            // Single-line comment
            if (index + 1 < code.Length && code[index] == '/' && code[index + 1] == '/')
            {
                var start = index;
                while (index < code.Length && code[index] != '\n')
                    index++;
                var comment = WebUtility.HtmlEncode(code[start..index].Trim());
                result.Append($"<span class=\"token-comment\">{comment}</span>");
                // Include the newline if present
                if (index < code.Length && code[index] == '\n')
                {
                    result.Append('\n');
                    index++;
                }
                continue;
            }

            // Multi-line comment
            if (index + 1 < code.Length && code[index] == '/' && code[index + 1] == '*')
            {
                var start = index;
                index += 2;
                while (index + 1 < code.Length && !(code[index] == '*' && code[index + 1] == '/'))
                    index++;
                if (index + 1 < code.Length)
                    index += 2;
                var comment = WebUtility.HtmlEncode(code[start..index].Trim());
                result.Append($"<span class=\"token-comment\">{comment}</span>");
                continue;
            }

            // String literals
            if (code[index] == '"')
            {
                var start = index;
                index++;
                while (index < code.Length && code[index] != '"')
                {
                    if (code[index] == '\\' && index + 1 < code.Length)
                        index += 2;
                    else
                        index++;
                }
                if (index < code.Length)
                    index++;
                var str = WebUtility.HtmlEncode(code[start..index].Trim());
                result.Append($"<span class=\"token-string\">{str}</span>");
                continue;
            }

            // Character literals
            if (code[index] == '\'')
            {
                var start = index;
                index++;
                while (index < code.Length && code[index] != '\'')
                {
                    if (code[index] == '\\' && index + 1 < code.Length)
                        index += 2;
                    else
                        index++;
                }
                if (index < code.Length)
                    index++;
                var ch = WebUtility.HtmlEncode(code[start..index].Trim());
                result.Append($"<span class=\"token-string\">{ch}</span>");
                continue;
            }

            // Numbers
            if (char.IsDigit(code[index]))
            {
                var start = index;
                while (
                    index < code.Length
                    && (
                        char.IsDigit(code[index])
                        || code[index] == '.'
                        || code[index] == 'f'
                        || code[index] == 'd'
                        || code[index] == 'm'
                    )
                )
                    index++;
                var num = WebUtility.HtmlEncode(code[start..index].Trim());
                result.Append($"<span class=\"token-number\">{num}</span>");
                continue;
            }

            // Identifiers and keywords
            if (char.IsLetter(code[index]) || code[index] == '_' || code[index] == '@')
            {
                var start = index;
                if (code[index] == '@')
                    index++;
                while (
                    index < code.Length && (char.IsLetterOrDigit(code[index]) || code[index] == '_')
                )
                    index++;
                var identifier = code[start..index].Trim();
                var encoded = WebUtility.HtmlEncode(identifier);

                // Check what comes after the identifier
                var nextNonWhitespace = index;
                while (
                    nextNonWhitespace < code.Length && char.IsWhiteSpace(code[nextNonWhitespace])
                )
                    nextNonWhitespace++;

                // Check what came before the identifier (for detecting properties after dot)
                var prevNonWhitespace = start - 1;
                while (prevNonWhitespace >= 0 && char.IsWhiteSpace(code[prevNonWhitespace]))
                    prevNonWhitespace--;
                var afterDot = prevNonWhitespace >= 0 && code[prevNonWhitespace] == '.';
                var beforeEquals =
                    nextNonWhitespace < code.Length && code[nextNonWhitespace] == '=';

                if (Keywords.Contains(identifier))
                {
                    result.Append($"<span class=\"token-keyword\">{encoded}</span>");
                }
                else if (
                    CommonMethods.Contains(identifier)
                    || (nextNonWhitespace < code.Length && code[nextNonWhitespace] == '(')
                )
                {
                    result.Append($"<span class=\"token-method\">{encoded}</span>");
                }
                else if (afterDot && char.IsUpper(identifier[identifier.StartsWith('@') ? 1 : 0]))
                {
                    // PascalCase after dot is a property (e.g., o.Id, o.Customer)
                    result.Append($"<span class=\"token-property\">{encoded}</span>");
                }
                else if (beforeEquals)
                {
                    // Identifier before = is a property assignment (e.g., CustomerName = ...)
                    result.Append($"<span class=\"token-property\">{encoded}</span>");
                }
                else if (
                    CommonTypes.Contains(identifier)
                    || (nextNonWhitespace < code.Length && code[nextNonWhitespace] == '<')
                )
                {
                    // Known types or generic types (e.g., List<T>, Order)
                    result.Append($"<span class=\"token-class\">{encoded}</span>");
                }
                else if (!afterDot && char.IsUpper(identifier[identifier.StartsWith('@') ? 1 : 0]))
                {
                    // PascalCase not after dot is likely a type (e.g., Order, OrderDto)
                    result.Append($"<span class=\"token-class\">{encoded}</span>");
                }
                else
                {
                    // camelCase or lowercase (e.g., o, query, dbContext)
                    result.Append($"<span class=\"token-variable\">{encoded}</span>");
                }
                continue;
            }

            // Operators and punctuation
            var currentChar = code[index];
            var isPunctuation = "{}[]();,.?:".Contains(currentChar);
            var encoded2 = WebUtility.HtmlEncode(currentChar.ToString().Trim());

            if (isPunctuation)
            {
                result.Append($"<span class=\"token-punctuation\">{encoded2}</span>");
            }
            else
            {
                result.Append($"<span class=\"token-operator\">{encoded2}</span>");
            }
            index++;
        }

        return result.ToString();
    }
}
