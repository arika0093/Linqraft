using Microsoft.JSInterop;

namespace Linqraft.Playground.Services;

/// <summary>
/// Service for syntax highlighting using Shiki library via JavaScript interop.
/// Replaces the custom CSharpSyntaxHighlighter for static code display.
/// Note: SemanticHighlightingService is still used by EditorPane for enhanced Monaco editor highlighting.
/// </summary>
public class ShikiHighlightService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _initialized = false;

    public ShikiHighlightService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Initialize Shiki (lazy initialization on first use).
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        try
        {
            await _jsRuntime.InvokeVoidAsync("shikiInterop.initialize");
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize Shiki: {ex.Message}");
            // Continue anyway - highlightCode will handle fallback
        }
    }

    /// <summary>
    /// Highlight C# code and return HTML markup.
    /// </summary>
    /// <param name="code">The C# code to highlight</param>
    /// <param name="language">Language identifier (default: csharp)</param>
    /// <param name="theme">Theme name (default: dark-plus)</param>
    /// <returns>HTML string with syntax highlighting</returns>
    public async Task<string> HighlightAsync(
        string code,
        string language = "csharp",
        string theme = "dark-plus"
    )
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        await EnsureInitializedAsync();

        try
        {
            var html = await _jsRuntime.InvokeAsync<string>(
                "shikiInterop.highlightCode",
                code,
                language,
                theme
            );
            return html;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to highlight code: {ex.Message}");
            // Fallback to plain pre/code block
            return $"<pre><code>{System.Net.WebUtility.HtmlEncode(code)}</code></pre>";
        }
    }
}
