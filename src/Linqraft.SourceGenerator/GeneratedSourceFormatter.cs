using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Linqraft.SourceGenerator;

internal static class GeneratedSourceFormatter
{
    private static readonly CSharpParseOptions ParseOptions = new(
        languageVersion: LanguageVersion.Preview,
        documentationMode: DocumentationMode.Parse
    );

    public static string FormatCompilationUnit(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();
        return root.NormalizeWhitespace(eol: "\n", indentation: "    ").ToFullString();
    }

    public static string FormatGeneratedSource(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder(source.Length + 64);
        var scopeIndent = 0;
        var lastNonEmptyIndent = 0;
        var lastTrimmed = string.Empty;
        var lastQuestionIndent = -1;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                builder.Append('\n');
                continue;
            }

            var leadingCloseCount = CountLeadingClosingBraces(trimmed);
            var baseIndent = System.Math.Max(0, scopeIndent - leadingCloseCount);
            var effectiveIndent = baseIndent;
            if (trimmed[0] is '.' or '[')
            {
                effectiveIndent =
                    lastTrimmed.Length != 0 && (lastTrimmed[0] is '.' or '[' or '}')
                        ? lastNonEmptyIndent
                        : lastNonEmptyIndent + 1;
            }
            else if (trimmed[0] == '?')
            {
                effectiveIndent = lastNonEmptyIndent + 1;
                lastQuestionIndent = effectiveIndent;
            }
            else if (trimmed[0] == ':')
            {
                effectiveIndent =
                    lastQuestionIndent >= 0 ? lastQuestionIndent : lastNonEmptyIndent + 1;
                lastQuestionIndent = -1;
            }

            builder.Append(' ', effectiveIndent * 4);
            builder.AppendLine(trimmed);

            lastTrimmed = trimmed;
            lastNonEmptyIndent = effectiveIndent;
            scopeIndent = System.Math.Max(
                0,
                baseIndent
                    + CountOpenBraces(trimmed)
                    - CountCloseBraces(trimmed)
                    + leadingCloseCount
            );
        }

        return builder.ToString();
    }

    private static int CountLeadingClosingBraces(string text)
    {
        var count = 0;
        while (count < text.Length && text[count] == '}')
        {
            count++;
        }

        return count;
    }

    private static int CountOpenBraces(string text)
    {
        return text.Count(character => character == '{');
    }

    private static int CountCloseBraces(string text)
    {
        return text.Count(character => character == '}');
    }
}
