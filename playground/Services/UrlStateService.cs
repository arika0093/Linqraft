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

    // Maximum URL length for GitHub issue creation (GitHub's limit is around 8192, but browsers have lower limits)
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

        // Description section
        sb.AppendLine("## Description");
        sb.AppendLine("");
        sb.AppendLine("<!-- Describe your issue here -->");
        sb.AppendLine("");

        // Reproduction section (without playground link)
        sb.AppendLine("## Reproduction");
        sb.AppendLine("");
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Code</summary>");
        sb.AppendLine("");

        foreach (var file in files.Where(f => !f.IsHidden))
        {
            sb.AppendLine($"**{file.Name}**");
            sb.AppendLine("```csharp");
            sb.AppendLine(file.Content);
            sb.AppendLine("```");
            sb.AppendLine("");
        }

        sb.AppendLine("</details>");
        sb.AppendLine("");

        // Result section (new section with generated output)
        sb.AppendLine("## Result");
        sb.AppendLine("");
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Generated Expression</summary>");
        sb.AppendLine("");
        sb.AppendLine("```csharp");
        sb.AppendLine(
            string.IsNullOrWhiteSpace(generatedExpression)
                ? "// No expression generated"
                : generatedExpression
        );
        sb.AppendLine("```");
        sb.AppendLine("");
        sb.AppendLine("</details>");
        sb.AppendLine("");
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Generated DTO Class</summary>");
        sb.AppendLine("");
        sb.AppendLine("```csharp");
        sb.AppendLine(
            string.IsNullOrWhiteSpace(generatedDtoClass)
                ? "// No DTO class generated"
                : generatedDtoClass
        );
        sb.AppendLine("```");
        sb.AppendLine("");
        sb.AppendLine("</details>");
        sb.AppendLine("");

        // Expected Behavior section
        sb.AppendLine("## Expected Behavior");
        sb.AppendLine("");
        sb.AppendLine("<!-- What did you expect to happen? -->");
        sb.AppendLine("");

        // Additional Context section (new placeholder section)
        sb.AppendLine("## Additional Context");
        sb.AppendLine("");
        sb.AppendLine("<!-- Add any other context about the problem here -->");
        sb.AppendLine("");

        // Playground Link section (moved from Reproduction)
        sb.AppendLine("## Playground Link");
        sb.AppendLine("");
        sb.AppendLine($"[Open in Playground]({shareableUrl})");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a simplified issue body when the code is too large for URL inclusion.
    /// Only includes the Playground link with instructions to paste code manually.
    /// </summary>
    private static string GenerateSimplifiedIssueBody(string shareableUrl)
    {
        var sb = new StringBuilder();

        // Description section
        sb.AppendLine("## Description");
        sb.AppendLine("");
        sb.AppendLine("<!-- Describe your issue here -->");
        sb.AppendLine("");

        // Reproduction section with instructions
        sb.AppendLine("## Reproduction");
        sb.AppendLine("");
        sb.AppendLine(
            "The code is too large to include directly in this issue. "
                + "Please copy and paste the code from the Playground link below into this section."
        );
        sb.AppendLine("");
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Code</summary>");
        sb.AppendLine("");
        sb.AppendLine("<!-- Paste your code here from the Playground -->");
        sb.AppendLine("");
        sb.AppendLine("</details>");
        sb.AppendLine("");

        // Result section with instructions
        sb.AppendLine("## Result");
        sb.AppendLine("");
        sb.AppendLine(
            "Please copy the generated output from the Playground if needed."
        );
        sb.AppendLine("");

        // Expected Behavior section
        sb.AppendLine("## Expected Behavior");
        sb.AppendLine("");
        sb.AppendLine("<!-- What did you expect to happen? -->");
        sb.AppendLine("");

        // Additional Context section
        sb.AppendLine("## Additional Context");
        sb.AppendLine("");
        sb.AppendLine("<!-- Add any other context about the problem here -->");
        sb.AppendLine("");

        // Playground Link section
        sb.AppendLine("## Playground Link");
        sb.AppendLine("");
        sb.AppendLine($"[Open in Playground]({shareableUrl})");

        return sb.ToString();
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
