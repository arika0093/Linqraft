using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Discovery;

/// <summary>
/// Interface for pattern matchers that find specific syntax patterns in the syntax tree.
/// Used in the Discovery phase of the pipeline.
/// </summary>
internal interface IPatternMatcher
{
    /// <summary>
    /// Finds all syntax nodes in the tree that match this pattern.
    /// </summary>
    /// <param name="root">The root syntax node to search</param>
    /// <returns>All matching syntax nodes</returns>
    IEnumerable<SyntaxNode> FindMatches(SyntaxNode root);

    /// <summary>
    /// Checks if a specific node matches this pattern.
    /// </summary>
    /// <param name="node">The node to check</param>
    /// <returns>True if the node matches the pattern</returns>
    bool IsMatch(SyntaxNode node);
}
