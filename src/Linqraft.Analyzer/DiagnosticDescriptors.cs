using Microsoft.CodeAnalysis;

namespace Linqraft.Analyzer;

/// <summary>
/// Diagnostic descriptors for Linqraft analyzers
/// </summary>
public static class DiagnosticDescriptors
{
    private const string Category = "Usage";

    /// <summary>
    /// LQ001: Select query can be converted to SelectExpr
    /// </summary>
    public static readonly DiagnosticDescriptor SelectToSelectExpr = new(
        id: "LQ001",
        title: "Use SelectExpr instead of Select",
        messageFormat: "Consider using SelectExpr for better performance and type safety",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "SelectExpr provides compile-time expression tree generation and better performance.");
}
