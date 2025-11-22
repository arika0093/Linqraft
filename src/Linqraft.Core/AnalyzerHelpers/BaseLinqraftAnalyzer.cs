using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Base class for Linqraft analyzers, providing common functionality
/// and ensuring consistent diagnostic configuration
/// </summary>
public abstract class BaseLinqraftAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for this analyzer (e.g., "LQRF001")
    /// </summary>
    protected abstract string DiagnosticId { get; }

    /// <summary>
    /// The title of the diagnostic
    /// </summary>
    protected abstract LocalizableString Title { get; }

    /// <summary>
    /// The message format for the diagnostic
    /// </summary>
    protected abstract LocalizableString MessageFormat { get; }

    /// <summary>
    /// The description of the diagnostic
    /// </summary>
    protected abstract LocalizableString Description { get; }

    /// <summary>
    /// The category of the diagnostic (default: "Design")
    /// </summary>
    protected virtual string Category => "Design";

    /// <summary>
    /// The severity of the diagnostic (default: Hidden)
    /// </summary>
    protected virtual DiagnosticSeverity Severity => DiagnosticSeverity.Hidden;

    /// <summary>
    /// Whether the diagnostic is enabled by default (default: true)
    /// </summary>
    protected virtual bool IsEnabledByDefault => true;

    /// <summary>
    /// The help link URI format (default: GitHub docs)
    /// </summary>
    protected virtual string HelpLinkUriFormat =>
        "https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{0}.md";

    private DiagnosticDescriptor? _rule;

    /// <summary>
    /// The diagnostic rule for this analyzer
    /// </summary>
    protected DiagnosticDescriptor Rule
    {
        get
        {
            if (_rule == null)
            {
                _rule = new DiagnosticDescriptor(
                    DiagnosticId,
                    Title,
                    MessageFormat,
                    Category,
                    Severity,
                    IsEnabledByDefault,
                    description: Description,
                    helpLinkUri: string.Format(HelpLinkUriFormat, DiagnosticId)
                );
            }
            return _rule;
        }
    }

    /// <summary>
    /// The supported diagnostics for this analyzer
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <summary>
    /// Initialize the analyzer with standard configuration
    /// </summary>
    public override void Initialize(AnalysisContext context)
    {
        // Standard configuration for all Linqraft analyzers
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Allow derived classes to register their own actions
        RegisterActions(context);
    }

    /// <summary>
    /// Register analysis actions for this analyzer
    /// </summary>
    /// <param name="context">The analysis context</param>
    protected abstract void RegisterActions(AnalysisContext context);

    /// <summary>
    /// Report a diagnostic at the specified location
    /// </summary>
    /// <param name="context">The syntax node analysis context</param>
    /// <param name="location">The location of the diagnostic</param>
    /// <param name="messageArgs">Optional arguments for the message format</param>
    protected void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        Location location,
        params object[] messageArgs
    )
    {
        var diagnostic = Diagnostic.Create(Rule, location, messageArgs);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Report a diagnostic for a syntax node
    /// </summary>
    /// <param name="context">The syntax node analysis context</param>
    /// <param name="node">The syntax node</param>
    /// <param name="messageArgs">Optional arguments for the message format</param>
    protected void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        SyntaxNode node,
        params object[] messageArgs
    )
    {
        ReportDiagnostic(context, node.GetLocation(), messageArgs);
    }
}
