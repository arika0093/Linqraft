using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.Analyzer;

/// <summary>
/// Helper methods for formatting code fixes with consistent line endings
/// </summary>
public static class CodeFixFormattingHelper
{
    /// <summary>
    /// Formats the document and normalizes all line endings to LF
    /// </summary>
    /// <param name="document">The document to format</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The document with normalized line endings</returns>
    public static async Task<Document> FormatAndNormalizeLineEndingsAsync(
        Document document,
        CancellationToken cancellationToken = default
    )
    {
        // Get the document text and preserve encoding
        var originalText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var encoding = originalText.Encoding; // Preserve original encoding (even if null)

        // First, normalize all line endings to LF before formatting
        var normalizedText = originalText.ToString().Replace("\r\n", "\n");
        var normalizedDocument = document.WithText(
            encoding != null
                ? SourceText.From(normalizedText, encoding)
                : SourceText.From(normalizedText)
        );

        // Format the document to fix indentation
        var formattedDocument = await Formatter.FormatAsync(
            normalizedDocument,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);

        // Final pass: normalize line endings again (in case formatter introduced CRLF)
        var finalText = await formattedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var finalNormalizedText = finalText.ToString().Replace("\r\n", "\n");

        return formattedDocument.WithText(
            encoding != null
                ? SourceText.From(finalNormalizedText, encoding)
                : SourceText.From(finalNormalizedText)
        );
    }
}
