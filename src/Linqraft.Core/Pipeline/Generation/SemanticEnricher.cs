using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Enriches generated syntax trees with semantic information.
/// Converts type references to fully qualified names where needed.
/// </summary>
internal class SemanticEnricher
{
    private readonly SemanticModel _semanticModel;
    private readonly Compilation _compilation;

    /// <summary>
    /// Creates a new SemanticEnricher with the specified semantic model and compilation.
    /// </summary>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="compilation">The compilation for additional type information</param>
    public SemanticEnricher(SemanticModel semanticModel, Compilation compilation)
    {
        _semanticModel = semanticModel;
        _compilation = compilation;
    }

    /// <summary>
    /// Enriches the syntax node by converting type references to fully qualified names.
    /// </summary>
    /// <param name="node">The syntax node to enrich</param>
    /// <returns>The enriched syntax node</returns>
    public SyntaxNode Enrich(SyntaxNode node)
    {
        var rewriter = new FullyQualifyingRewriter(_semanticModel, _compilation);
        return rewriter.Visit(node);
    }

    private class FullyQualifyingRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly Compilation _compilation;

        public FullyQualifyingRewriter(SemanticModel semanticModel, Compilation compilation)
        {
            _semanticModel = semanticModel;
            _compilation = compilation;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Try to get symbol info and convert to fully qualified name if needed
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol is ITypeSymbol typeSymbol && ShouldFullyQualify(typeSymbol))
            {
                var fullyQualifiedName = typeSymbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                return SyntaxFactory.ParseTypeName(fullyQualifiedName);
            }

            return base.VisitIdentifierName(node);
        }

        private static bool ShouldFullyQualify(ITypeSymbol typeSymbol)
        {
            // Don't fully qualify built-in types (int, string, etc.)
            if (typeSymbol.SpecialType != SpecialType.None)
                return false;

            // Don't fully qualify System namespace types (optional)
            if (typeSymbol.ContainingNamespace?.ToDisplayString().StartsWith("System") == true)
                return false;

            return true;
        }
    }
}
