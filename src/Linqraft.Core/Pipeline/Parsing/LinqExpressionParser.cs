using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.SyntaxHelpers;

namespace Linqraft.Core.Pipeline.Parsing;

/// <summary>
/// Parser for extracting LINQ expression information from syntax trees.
/// </summary>
internal class LinqExpressionParser
{
    private readonly SemanticModel _semanticModel;
    private readonly Func<ExpressionSyntax, ITypeSymbol, string> _fullyQualifyExpression;

    /// <summary>
    /// Creates a new LINQ expression parser.
    /// </summary>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="fullyQualifyExpression">Function to fully qualify an expression</param>
    public LinqExpressionParser(
        SemanticModel semanticModel, 
        Func<ExpressionSyntax, ITypeSymbol, string> fullyQualifyExpression)
    {
        _semanticModel = semanticModel;
        _fullyQualifyExpression = fullyQualifyExpression;
    }

    /// <summary>
    /// Extracts LINQ expression information for a Select call.
    /// </summary>
    /// <param name="syntax">The expression syntax containing the Select call</param>
    /// <param name="sourceType">The source type for qualification</param>
    /// <returns>The extracted LINQ expression information, or null if extraction fails</returns>
    public LinqExpressionInfo? ExtractSelectInfo(ExpressionSyntax syntax, ITypeSymbol sourceType)
    {
        return ExtractLinqInfo(syntax, "Select", sourceType);
    }

    /// <summary>
    /// Extracts LINQ expression information for a SelectExpr call.
    /// </summary>
    /// <param name="syntax">The expression syntax containing the SelectExpr call</param>
    /// <param name="sourceType">The source type for qualification</param>
    /// <returns>The extracted LINQ expression information, or null if extraction fails</returns>
    public LinqExpressionInfo? ExtractSelectExprInfo(ExpressionSyntax syntax, ITypeSymbol sourceType)
    {
        return ExtractLinqInfo(syntax, "SelectExpr", sourceType);
    }

    /// <summary>
    /// Extracts LINQ expression information for a SelectMany call.
    /// </summary>
    /// <param name="syntax">The expression syntax containing the SelectMany call</param>
    /// <param name="sourceType">The source type for qualification</param>
    /// <returns>The extracted LINQ expression information, or null if extraction fails</returns>
    public LinqExpressionInfo? ExtractSelectManyInfo(ExpressionSyntax syntax, ITypeSymbol sourceType)
    {
        return ExtractLinqInfo(syntax, "SelectMany", sourceType);
    }

    private LinqExpressionInfo? ExtractLinqInfo(ExpressionSyntax syntax, string methodName, ITypeSymbol sourceType)
    {
        var info = LinqMethodHelper.ExtractLinqInvocationInfo(syntax, methodName);
        if (info is null)
            return null;

        // Fully qualify static references in base expression
        var fullyQualifiedBaseExpression = FullyQualifyBaseExpression(info, sourceType);

        // Apply comment removal to base expression
        var cleanedBaseExpression = RemoveComments(
                SyntaxFactory.ParseExpression(fullyQualifiedBaseExpression)
            )
            .ToString();

        // Apply comment removal to null check expression if present
        string? cleanedNullCheckExpression = null;
        if (info.NullCheckExpression is not null)
        {
            cleanedNullCheckExpression = RemoveComments(
                    SyntaxFactory.ParseExpression(info.NullCheckExpression)
                )
                .ToString();
        }

        // Fully qualify static references in chained methods
        var fullyQualifiedChainedMethods = FullyQualifyChainedMethods(info, sourceType);

        return new LinqExpressionInfo
        {
            BaseExpression = cleanedBaseExpression,
            ParamName = info.ParameterName,
            ChainedMethods = fullyQualifiedChainedMethods,
            HasNullableAccess = info.HasNullableAccess,
            CoalescingDefaultValue = info.CoalescingDefaultValue,
            NullCheckExpression = cleanedNullCheckExpression,
        };
    }

    /// <summary>
    /// Fully qualifies static, const, and enum references in the base expression.
    /// </summary>
    private string FullyQualifyBaseExpression(LinqMethodHelper.LinqInvocationInfo info, ITypeSymbol sourceType)
    {
        if (info.BaseInvocations is null || info.BaseInvocations.Count == 0)
        {
            return info.BaseExpression;
        }

        var result = info.BaseExpression;
        var replacements = new List<(string Original, string Replacement, int Start)>();

        var baseExprSyntax = info.Invocation.Expression;
        int baseExprStart = 0;
        if (baseExprSyntax is MemberAccessExpressionSyntax linqMember)
        {
            baseExprStart = linqMember.Expression.SpanStart;
        }

        foreach (var invocation in info.BaseInvocations)
        {
            // Calculate position relative to base expression
            int argListStart = invocation.ArgumentList.SpanStart - baseExprStart;

            var processedArguments = new List<string>();
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                var processedArg = _fullyQualifyExpression(argument.Expression, sourceType);
                processedArguments.Add(processedArg);
            }

            var originalArgs = invocation.ArgumentList.ToString();
            var fullyQualifiedArgs = $"({string.Join(", ", processedArguments)})";

            if (originalArgs != fullyQualifiedArgs)
            {
                replacements.Add((originalArgs, fullyQualifiedArgs, argListStart));
            }
        }

        // Apply replacements in reverse order (by position)
        var orderedReplacements = replacements.OrderByDescending(r => r.Start).ToList();

        foreach (var (original, replacement, start) in orderedReplacements)
        {
            if (start >= 0 && start + original.Length <= result.Length)
            {
                var substring = result.Substring(start, original.Length);
                if (substring == original)
                {
                    result =
                        result.Substring(0, start)
                        + replacement
                        + result.Substring(start + original.Length);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Fully qualifies static references in chained method invocations.
    /// </summary>
    private string FullyQualifyChainedMethods(LinqMethodHelper.LinqInvocationInfo info, ITypeSymbol sourceType)
    {
        if (info.ChainedInvocations is null || info.ChainedInvocations.Count == 0)
        {
            return info.ChainedMethods;
        }

        var result = new System.Text.StringBuilder();

        foreach (var invocation in info.ChainedInvocations)
        {
            string methodName;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                methodName = memberAccess.Name.Identifier.Text;
            }
            else if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
            {
                methodName = memberBinding.Name.Identifier.Text;
            }
            else
            {
                result.Append($".{invocation}");
                continue;
            }

            var processedArguments = new List<string>();
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                var processedArg = _fullyQualifyExpression(argument.Expression, sourceType);
                processedArguments.Add(processedArg);
            }

            result.Append($".{methodName}({string.Join(", ", processedArguments)})");
        }

        return result.ToString();
    }

    /// <summary>
    /// Removes comments from a syntax node while preserving other trivia (whitespace, etc).
    /// </summary>
    private static T RemoveComments<T>(T node) where T : SyntaxNode
    {
        return (T)node.ReplaceTrivia(
            node.DescendantTrivia(descendIntoTrivia: true),
            (originalTrivia, _) =>
            {
                // Remove single-line and multi-line comments
                if (
                    originalTrivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    || originalTrivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                )
                {
                    return default;
                }
                return originalTrivia;
            }
        );
    }
}
