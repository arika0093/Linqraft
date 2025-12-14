using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Discovery;

/// <summary>
/// Pattern matcher for SelectExpr method invocations.
/// Detects calls to the SelectExpr extension method in the syntax tree.
/// </summary>
internal class SelectExprMatcher : IPatternMatcher
{
    private const string SelectExprMethodName = "SelectExpr";

    /// <inheritdoc/>
    public IEnumerable<SyntaxNode> FindMatches(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(IsMatch);
    }

    /// <inheritdoc/>
    public bool IsMatch(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        // Check for method name ending with "SelectExpr"
        return GetMethodName(invocation.Expression) == SelectExprMethodName;
    }

    private static string? GetMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }
}
