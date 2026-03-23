using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Rewrites anonymous-object creations to use resolved generated DTO types.
/// </summary>
internal sealed class AnonymousReplacementRewriter : CSharpSyntaxRewriter
{
    private readonly IReadOnlyDictionary<TextSpan, string> _replacementTypes;

    /// <summary>
    /// Initializes a new instance of the AnonymousReplacementRewriter class.
    /// </summary>
    public AnonymousReplacementRewriter(IReadOnlyDictionary<TextSpan, string> replacementTypes)
    {
        _replacementTypes = replacementTypes;
    }

    /// <summary>
    /// Rewrites anonymous-object creation expressions that have resolved replacement types.
    /// </summary>
    public override SyntaxNode? VisitAnonymousObjectCreationExpression(
        AnonymousObjectCreationExpressionSyntax node
    )
    {
        if (!_replacementTypes.TryGetValue(node.Span, out var targetType))
        {
            return base.VisitAnonymousObjectCreationExpression(node);
        }

        var assignments = node.Initializers.Select(initializer =>
        {
            var visited = Visit(initializer.Expression);
            var value = visited as ExpressionSyntax ?? initializer.Expression;
            var memberName = GetMemberName(initializer);
            return $"{memberName} = {value}";
        });

        var text = $"new {targetType} {{ {string.Join(", ", assignments)} }}";
        return SyntaxFactory.ParseExpression(text).WithTriviaFrom(node);
    }

    /// <summary>
    /// Gets the member name emitted for an anonymous-object initializer.
    /// </summary>
    private static string GetMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        return initializer.NameEquals?.Name.Identifier.ValueText
            ?? AnonymousMemberNameResolver.Get(initializer.Expression);
    }
}
