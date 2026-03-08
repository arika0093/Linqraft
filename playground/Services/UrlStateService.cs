using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Linqraft.Core;
using Linqraft.Playground.Models;
using Microsoft.AspNetCore.Components;

namespace Linqraft.Playground.Services;

/// <summary>
/// Service for managing URL state for sharing playground code.
/// Handles serialization, compression, and URL encoding of the current codebase.
/// </summary>
public class UrlStateService(NavigationManager navigationManager)
{
    private static JsonSerializerOptions JsonSerializeOptions =>
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const string StateParameterName = "state";

    // Maximum URL length for GitHub issue creation.
    // GitHub's server limit is around 8192, but IE11 has a 2083 limit, and most modern browsers support 8000+.
    // Using 8000 as a safe cross-browser limit.
    private const int MaxUrlLength = 8000;

    /// <summary>
    /// Serializable state object for URL sharing
    /// </summary>
    public class PlaygroundState
    {
        public List<FileState> Files { get; set; } = [];
        public LinqraftConfiguration Configuration { get; set; } = new();
    }

    /// <summary>
    /// Serializable file state
    /// </summary>
    public class FileState
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public bool IsHidden { get; set; }
    }

    /// <summary>
    /// Generates a shareable URL with the current state encoded in the query parameter
    /// </summary>
    public string GenerateShareableUrl(List<ProjectFile> files, LinqraftConfiguration configuration)
    {
        var state = new PlaygroundState
        {
            Files = files
                .Select(f => new FileState
                {
                    Name = f.Name,
                    Path = f.Path,
                    Content = f.Content,
                    IsHidden = f.IsHidden,
                })
                .ToList(),
            Configuration = configuration,
        };

        var json = JsonSerializer.Serialize(state, JsonSerializeOptions);

        var compressed = CompressString(json);
        var encoded = Base64UrlEncode(compressed);

        var baseUri = navigationManager.BaseUri;
        return $"{baseUri}playground?{StateParameterName}={encoded}";
    }

    /// <summary>
    /// Generates a URL for creating a GitHub issue with the current state.
    /// If the URL would be too long, a simplified version with only the Playground link is generated.
    /// </summary>
    public string GenerateGitHubIssueUrl(
        List<ProjectFile> files,
        LinqraftConfiguration configuration,
        string generatedExpression = "",
        string generatedDtoClass = "",
        string issueTitle = ""
    )
    {
        var shareableUrl = GenerateShareableUrl(files, configuration);
        var encodedTitle = Uri.EscapeDataString(
            issueTitle.Length > 0 ? issueTitle : "Issue from Playground"
        );

        // Try to generate full issue body first
        var fullIssueBody = GenerateIssueBody(
            files,
            shareableUrl,
            generatedExpression,
            generatedDtoClass
        );
        var fullEncodedBody = Uri.EscapeDataString(fullIssueBody);
        var fullUrl =
            $"https://github.com/arika0093/Linqraft/issues/new?title={encodedTitle}&body={fullEncodedBody}&labels=generator";

        // If URL is within limits, return the full version
        if (fullUrl.Length <= MaxUrlLength)
        {
            return fullUrl;
        }

        // URL is too long, generate simplified version with only Playground link
        var simplifiedIssueBody = GenerateSimplifiedIssueBody(shareableUrl);
        var simplifiedEncodedBody = Uri.EscapeDataString(simplifiedIssueBody);

        return $"https://github.com/arika0093/Linqraft/issues/new?title={encodedTitle}&body={simplifiedEncodedBody}&labels=generator";
    }

    /// <summary>
    /// Tries to restore state from the current URL's query parameter
    /// </summary>
    public PlaygroundState? TryRestoreFromUrl()
    {
        var uri = new Uri(navigationManager.Uri);
        var stateParam = GetQueryParameter(uri.Query, StateParameterName);

        if (string.IsNullOrEmpty(stateParam))
        {
            return null;
        }

        try
        {
            var compressed = Base64UrlDecode(stateParam);
            var json = DecompressString(compressed);
            return JsonSerializer.Deserialize<PlaygroundState>(json, JsonSerializeOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
        {
            // Invalid state parameter (malformed base64, invalid JSON, or corrupted gzip data)
            return null;
        }
    }

    /// <summary>
    /// Checks if the current URL contains a state parameter
    /// </summary>
    public bool HasStateInUrl()
    {
        var uri = new Uri(navigationManager.Uri);
        return !string.IsNullOrEmpty(GetQueryParameter(uri.Query, StateParameterName));
    }

    /// <summary>
    /// Simple query string parameter extraction without System.Web dependency
    /// </summary>
    private static string? GetQueryParameter(string query, string parameterName)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        // Remove leading '?' if present
        var queryString = query.StartsWith("?") ? query.Substring(1) : query;

        foreach (var pair in queryString.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == parameterName)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static string GenerateIssueBody(
        List<ProjectFile> files,
        string shareableUrl,
        string generatedExpression,
        string generatedDtoClass
    )
    {
        var sb = new StringBuilder();

        // Additional Context section (new placeholder section)
        var codeSection = string.Join(
            "\n\n",
            files.Where(f => !f.IsHidden).Select(f => $"**{f.Name}**\n```csharp\n{f.Content}\n```")
        );
        var generatedExpressionSection = string.IsNullOrWhiteSpace(generatedExpression)
            ? "// No expression generated"
            : generatedExpression;
        var generatedDtoClassSection = string.IsNullOrWhiteSpace(generatedDtoClass)
            ? "// No DTO class generated"
            : generatedDtoClass;

        return $"""
            ## Description

            <!-- Describe your issue here -->

            ## Reproduction

            <details>
            <summary>Code</summary>

            {codeSection}

            </details>

            ## Result

            <details>
            <summary>Generated Expression</summary>

            ```csharp
            {generatedExpressionSection}
            ```

            </details>

            <details>
            <summary>Generated DTO Class</summary>

            ```csharp
            {generatedDtoClassSection}
            ```

            </details>

            ## Expected Behavior

            <!-- What did you expect to happen? -->

            ## Additional Context

            <!-- Add any other context about the problem here -->

            ## Playground Link

            [Open in Playground]({shareableUrl})
            """;
    }

    /// Generates a simplified issue body when the code is too large for URL inclusion.
    /// Only includes the Playground link with instructions to paste code manually.
    /// </summary>
    private static string GenerateSimplifiedIssueBody(string shareableUrl)
    {
        return $"""
            ## Description

            <!-- Describe your issue here -->

            ## Reproduction

            <details>
            <summary>Code</summary>

            ```csharp
            <!-- Paste your code here from the Playground -->
            ```

            </details>

            ## Result

            <details>
            <summary>Generated Expression</summary>

            ```csharp
            <!-- Please paste the generated expression output from the Playground -->
            ```

            </details>

            <details>
            <summary>Generated DTO Class</summary>

            ```csharp
            <!-- Please paste the generated dto output from the Playground -->
            ```

            ## Expected Behavior

            <!-- What did you expect to happen? -->

            ## Additional Context

            <!-- Add any other context about the problem here -->

            ## Playground Link

            [Open in Playground]({shareableUrl})
            """;
    }

    private static byte[] CompressString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    private static string DecompressString(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string encoded)
    {
        // Add padding back
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }
        return Convert.FromBase64String(padded);
    }
}
