using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Discovery;

/// <summary>
/// Pattern matcher for LinqraftMappingGenerate attribute declarations.
/// Detects classes or methods marked with [LinqraftMappingGenerate].
/// </summary>
internal class MappingAttributeMatcher : IPatternMatcher
{
    private const string AttributeName = "LinqraftMappingGenerate";
    private const string AttributeFullName = "LinqraftMappingGenerateAttribute";

    /// <inheritdoc/>
    public IEnumerable<SyntaxNode> FindMatches(SyntaxNode root)
    {
        return root.DescendantNodes()
            .Where(IsMatch);
    }

    /// <inheritdoc/>
    public bool IsMatch(SyntaxNode node)
    {
        var attributeLists = node switch
        {
            MethodDeclarationSyntax method => method.AttributeLists,
            ClassDeclarationSyntax classDecl => classDecl.AttributeLists,
            _ => default
        };

        if (attributeLists == default || attributeLists.Count == 0)
            return false;

        return attributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => IsLinqraftMappingGenerateAttribute(attr));
    }

    private static bool IsLinqraftMappingGenerateAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        return name == AttributeName
            || name == AttributeFullName
            || name.EndsWith($".{AttributeName}")
            || name.EndsWith($".{AttributeFullName}");
    }
}
