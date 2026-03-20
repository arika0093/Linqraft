using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Linqraft.SourceGenerator;

internal static class GeneratedSourceFormatter
{
    public static string FormatGeneratedSource(
        string source,
        CancellationToken cancellationToken = default
    )
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder(source.Length + 64);
        var groupingIndentStack = new Stack<int>();
        var scopeIndent = 0;
        var lastNonEmptyIndent = 0;
        var lastTrimmed = string.Empty;
        var lastQuestionIndent = -1;
        var previousWasBlank = false;

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawLine = lines[index];
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                if (previousWasBlank)
                {
                    continue;
                }

                builder.Append('\n');
                previousWasBlank = true;
                continue;
            }

            previousWasBlank = false;
            var leadingCloseCount = CountLeadingClosingBraces(trimmed, cancellationToken);
            var leadingGroupCloseCount = CountLeadingGroupingClosures(trimmed, cancellationToken);
            var baseIndent = System.Math.Max(0, scopeIndent - leadingCloseCount);
            var effectiveIndent = baseIndent;
            var nextTrimmed = GetNextNonEmptyTrimmedLine(lines, index + 1, cancellationToken);
            if (trimmed[0] == '.')
            {
                effectiveIndent =
                    lastTrimmed.Length != 0 && (lastTrimmed[0] is '.' or '[' or '}')
                        ? lastNonEmptyIndent
                        : lastNonEmptyIndent + 1;
            }
            else if (trimmed[0] == '[')
            {
                if (IsAttributeContext(lastTrimmed))
                {
                    effectiveIndent = baseIndent;
                }
                else if (lastTrimmed.Length != 0 && (lastTrimmed[0] is '.' or '[' or '}'))
                {
                    effectiveIndent = lastNonEmptyIndent;
                }
                else
                {
                    effectiveIndent = lastNonEmptyIndent + 1;
                }
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
            else if (leadingGroupCloseCount > 0 && groupingIndentStack.Count > 0)
            {
                effectiveIndent = groupingIndentStack.Peek();
            }
            else if (
                groupingIndentStack.Count > 0
                && trimmed[0] is not '.' and not '[' and not '?' and not ':' and not ')' and not ']'
            )
            {
                effectiveIndent = System.Math.Max(effectiveIndent, groupingIndentStack.Peek() + 1);
            }

            builder.Append(' ', effectiveIndent * 4);
            builder.AppendLine(trimmed);

            lastTrimmed = trimmed;
            lastNonEmptyIndent = effectiveIndent;
            var openCount = CountOpenBraces(trimmed);
            var trailingCloseCount = CountCloseBraces(trimmed) - leadingCloseCount;
            scopeIndent =
                trimmed[0] is '.' or '?' or ':' && openCount == 0 && trailingCloseCount == 0
                    ? System.Math.Max(0, effectiveIndent - 1)
                    : System.Math.Max(0, effectiveIndent + openCount - trailingCloseCount);

            if (
                leadingCloseCount > 0
                && StartsWithChainClosure(trimmed, leadingCloseCount)
                && !IsContinuationLine(nextTrimmed)
            )
            {
                scopeIndent = System.Math.Max(0, scopeIndent - 1);
            }

            PopGroupingIndents(groupingIndentStack, leadingGroupCloseCount, cancellationToken);
            if (EndsWithGroupingOpener(trimmed))
            {
                groupingIndentStack.Push(effectiveIndent);
            }
        }

        return builder.ToString();
    }

    private static string GetNextNonEmptyTrimmedLine(
        string[] lines,
        int startIndex,
        CancellationToken cancellationToken = default
    )
    {
        for (var index = startIndex; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = lines[index].Trim();
            if (trimmed.Length != 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static bool StartsWithChainClosure(string trimmed, int leadingCloseCount)
    {
        if (leadingCloseCount >= trimmed.Length)
        {
            return false;
        }

        return trimmed[leadingCloseCount] is ')' or ']'
            && trimmed.EndsWith(",", System.StringComparison.Ordinal);
    }

    private static bool IsContinuationLine(string trimmed)
    {
        return trimmed.Length != 0 && trimmed[0] is '.' or '[' or '?' or ':';
    }

    private static bool IsAttributeContext(string trimmed)
    {
        return trimmed.StartsWith("//", System.StringComparison.Ordinal)
            || trimmed.StartsWith("///", System.StringComparison.Ordinal)
            || trimmed.StartsWith("[", System.StringComparison.Ordinal)
            || trimmed == "{";
    }

    private static int CountLeadingClosingBraces(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        var count = 0;
        while (count < text.Length && text[count] == '}')
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
        }

        return count;
    }

    private static int CountLeadingGroupingClosures(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        var count = 0;
        while (count < text.Length && (text[count] == ')' || text[count] == ']'))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static bool EndsWithGroupingOpener(string trimmed)
    {
        return trimmed.Length != 0 && trimmed[^1] is '(' or '[';
    }

    private static void PopGroupingIndents(
        Stack<int> groupingIndentStack,
        int count,
        CancellationToken cancellationToken = default
    )
    {
        for (var index = 0; index < count && groupingIndentStack.Count > 0; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            groupingIndentStack.Pop();
        }
    }
}
