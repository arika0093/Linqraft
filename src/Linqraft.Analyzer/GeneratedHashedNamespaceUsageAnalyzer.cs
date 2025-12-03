using System.Text.RegularExpressions;
using Linqraft.Core.AnalyzerHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects and warns when using hash-based auto-generated namespace patterns like "using Foo.Bar.Generated_HashValue".
/// This helps prevent users from explicitly depending on auto-generated DTOs which may change on regeneration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GeneratedHashedNamespaceUsageAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRW001";

    /// <summary>
    /// Pattern to match Generated_XXXXXXXX where XXXXXXXX is an alphanumeric hash (at least 8 characters).
    /// This matches the format used when LinqraftNestedDtoUseHashNamespace is enabled.
    /// </summary>
    private static readonly Regex GeneratedHashPattern = new(
        @"Generated_[A-Z0-9]{8,}",
        RegexOptions.Compiled
    );

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "Using auto-generated hash-based namespace",
        "Using directive references auto-generated namespace '{0}'. Auto-generated DTOs may change on regeneration. Consider using explicit DTO types instead.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using directives that reference hash-based auto-generated namespaces (Generated_XXXXXXXX) may lead to compilation errors when DTOs are regenerated. Consider using explicit DTO types instead.",
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{AnalyzerId}.md"
    );

    protected override string DiagnosticId => AnalyzerId;
    protected override LocalizableString Title => RuleInstance.Title;
    protected override LocalizableString MessageFormat => RuleInstance.MessageFormat;
    protected override LocalizableString Description => RuleInstance.Description;
    protected override string Category => "Usage";
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    protected override DiagnosticDescriptor Rule => RuleInstance;

    protected override void RegisterActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
    }

    private void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;

        // Get the namespace name from the using directive
        var namespaceName = usingDirective.Name?.ToString();
        if (string.IsNullOrEmpty(namespaceName))
        {
            return;
        }

        // Check if the namespace contains the Generated_XXXXXXXX pattern
        var match = GeneratedHashPattern.Match(namespaceName);
        if (match.Success)
        {
            // Report diagnostic on the using directive
            var diagnostic = Diagnostic.Create(
                RuleInstance,
                usingDirective.GetLocation(),
                match.Value
            );
            context.ReportDiagnostic(diagnostic);
        }
    }
}
