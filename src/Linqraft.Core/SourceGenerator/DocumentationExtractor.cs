using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Documentation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

internal static class DocumentationExtractor
{
    public static DocumentationInfo? GetTypeDocumentation(
        INamedTypeSymbol? symbol,
        LinqraftCommentOutput outputMode,
        CancellationToken cancellationToken = default
    )
    {
        if (symbol is null || outputMode == LinqraftCommentOutput.None)
        {
            return null;
        }

        var summary = ExtractSummary(symbol, cancellationToken);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = ExtractSingleLineComment(symbol, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new DocumentationInfo
        {
            Summary = summary,
            Remarks =
                outputMode == LinqraftCommentOutput.All ? $"From: <c>{symbol.Name}</c>" : null,
        };
    }

    public static DocumentationInfo? GetExpressionDocumentation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        LinqraftCommentOutput outputMode,
        CancellationToken cancellationToken
    )
    {
        if (outputMode == LinqraftCommentOutput.None)
        {
            return null;
        }

        var symbol = FindDocumentationSymbol(expression, semanticModel, cancellationToken);
        if (symbol is null)
        {
            return null;
        }

        var summary =
            ExtractSummary(symbol, cancellationToken)
            ?? ExtractCommentAttribute(symbol)
            ?? ExtractDisplayAttribute(symbol)
            ?? ExtractSingleLineComment(symbol, cancellationToken);

        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var remarks = outputMode == LinqraftCommentOutput.All ? BuildRemarks(symbol) : null;

        return new DocumentationInfo { Summary = summary, Remarks = remarks };
    }

    private static ISymbol? FindDocumentationSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol
                    ?? FindDocumentationSymbol(
                        memberAccess.Expression,
                        semanticModel,
                        cancellationToken
                    );
            case ConditionalAccessExpressionSyntax conditionalAccess:
                return FindDocumentationSymbol(
                        conditionalAccess.WhenNotNull,
                        semanticModel,
                        cancellationToken
                    )
                    ?? FindDocumentationSymbol(
                        conditionalAccess.Expression,
                        semanticModel,
                        cancellationToken
                    );
            case MemberBindingExpressionSyntax memberBinding:
                return semanticModel.GetSymbolInfo(memberBinding, cancellationToken).Symbol;
            case IdentifierNameSyntax identifier:
                return semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            case InvocationExpressionSyntax invocation:
                foreach (
                    var lambda in invocation
                        .ArgumentList.Arguments.Select(argument => argument.Expression)
                        .OfType<LambdaExpressionSyntax>()
                )
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (lambda.Body is ExpressionSyntax lambdaExpression)
                    {
                        var nested = FindDocumentationSymbol(
                            lambdaExpression,
                            semanticModel,
                            cancellationToken
                        );
                        if (nested is not null)
                        {
                            return nested;
                        }
                    }
                }

                return semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol;
            case ConditionalExpressionSyntax conditionalExpression:
                return FindDocumentationSymbol(
                        conditionalExpression.WhenTrue,
                        semanticModel,
                        cancellationToken
                    )
                    ?? FindDocumentationSymbol(
                        conditionalExpression.WhenFalse,
                        semanticModel,
                        cancellationToken
                    );
            case AnonymousObjectCreationExpressionSyntax anonymousObject:
                return anonymousObject
                    .Initializers.Select(initializer =>
                        FindDocumentationSymbol(
                            initializer.Expression,
                            semanticModel,
                            cancellationToken
                        )
                    )
                    .FirstOrDefault(symbol => symbol is not null);
            case ObjectCreationExpressionSyntax objectCreation:
                return objectCreation
                    .Initializer?.Expressions.Select(initializer =>
                        FindDocumentationSymbol(initializer, semanticModel, cancellationToken)
                    )
                    .FirstOrDefault(symbol => symbol is not null);
            case AssignmentExpressionSyntax assignment:
                return FindDocumentationSymbol(assignment.Right, semanticModel, cancellationToken);
            default:
                return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        }
    }

    private static string? ExtractSummary(
        ISymbol symbol,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(xml);
            foreach (var summary in document.Descendants("summary"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = Normalize(summary.Value);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractCommentAttribute(ISymbol symbol)
    {
        return symbol
                .GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.Name == "CommentAttribute")
                ?.ConstructorArguments.FirstOrDefault()
                .Value as string;
    }

    private static string? ExtractDisplayAttribute(ISymbol symbol)
    {
        return symbol
                .GetAttributes()
                .FirstOrDefault(attribute => attribute.AttributeClass?.Name == "DisplayAttribute")
                ?.NamedArguments.FirstOrDefault(argument => argument.Key == "Name")
                .Value.Value as string;
    }

    private static string? ExtractSingleLineComment(
        ISymbol symbol,
        CancellationToken cancellationToken = default
    )
    {
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        if (syntax is null)
        {
            return null;
        }

        var comment = syntax
            .GetLeadingTrivia()
            .Where(trivia =>
                trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia)
            )
            .Select(trivia => trivia.ToString().TrimStart('/').Trim())
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        return string.IsNullOrWhiteSpace(comment) ? null : comment;
    }

    private static string BuildRemarks(ISymbol symbol)
    {
        var parts = new List<string> { $"From: <c>{BuildSymbolPath(symbol)}</c>" };
        var attributes = symbol
            .GetAttributes()
            .Select(attribute => attribute.AttributeClass?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();

        if (attributes.Count != 0)
        {
            parts.Add($"Attributes: <c>[{string.Join("], [", attributes)}]</c>");
        }

        // The public docs do not define the exact remarks wording for every supported comment source.
        return string.Join("\n", parts);
    }

    private static string BuildSymbolPath(ISymbol symbol)
    {
        if (symbol.ContainingType is null)
        {
            return symbol.Name;
        }

        return $"{symbol.ContainingType.Name}.{symbol.Name}";
    }

    private static string Normalize(string value)
    {
        return string.Join(
            " ",
            value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries)
        );
    }
}
