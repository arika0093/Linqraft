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

    /// <summary>
    /// LQ002: SelectExpr can be enhanced with additional features
    /// </summary>
    public static readonly DiagnosticDescriptor EnhanceSelectExpr = new(
        id: "LQ002",
        title: "SelectExpr can be enhanced",
        messageFormat: "Consider enhancing this SelectExpr with auto-generated DTO or separate file",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "SelectExpr can be enhanced with auto-generated DTO types or separate file definitions.");
}
