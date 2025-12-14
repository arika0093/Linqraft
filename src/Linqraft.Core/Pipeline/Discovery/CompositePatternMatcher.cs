using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Discovery;

/// <summary>
/// A composite pattern matcher that combines multiple matchers.
/// Useful for matching multiple patterns in a single pass.
/// </summary>
internal class CompositePatternMatcher : IPatternMatcher
{
    private readonly List<IPatternMatcher> _matchers;

    /// <summary>
    /// Creates a new composite pattern matcher with the specified matchers.
    /// </summary>
    /// <param name="matchers">The matchers to combine</param>
    public CompositePatternMatcher(params IPatternMatcher[] matchers)
    {
        _matchers = matchers.ToList();
    }

    /// <summary>
    /// Creates a new composite pattern matcher with the specified matchers.
    /// </summary>
    /// <param name="matchers">The matchers to combine</param>
    public CompositePatternMatcher(IEnumerable<IPatternMatcher> matchers)
    {
        _matchers = matchers.ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<SyntaxNode> FindMatches(SyntaxNode root)
    {
        return _matchers.SelectMany(m => m.FindMatches(root)).Distinct();
    }

    /// <inheritdoc/>
    public bool IsMatch(SyntaxNode node)
    {
        return _matchers.Any(m => m.IsMatch(node));
    }
}
