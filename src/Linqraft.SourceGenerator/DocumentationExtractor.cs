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
        LinqraftCommentOutput outputMode
    )
    {
        if (symbol is null || outputMode == LinqraftCommentOutput.None)
        {
            return null;
        }

        var summary = ExtractSummary(symbol);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = ExtractSingleLineComment(symbol);
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new DocumentationInfo
        {
            Summary = summary,
            Remarks = outputMode == LinqraftCommentOutput.All ? $"From: <c>{symbol.Name}</c>" : null,
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

        var summary = ExtractSummary(symbol)
            ?? ExtractCommentAttribute(symbol)
            ?? ExtractDisplayAttribute(symbol)
            ?? ExtractSingleLineComment(symbol);

        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var remarks = outputMode == LinqraftCommentOutput.All
            ? BuildRemarks(symbol)
            : null;

        return new DocumentationInfo
        {
            Summary = summary,
            Remarks = remarks,
        };
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
                    ?? FindDocumentationSymbol(memberAccess.Expression, semanticModel, cancellationToken);
            case ConditionalAccessExpressionSyntax conditionalAccess:
                return FindDocumentationSymbol(conditionalAccess.WhenNotNull, semanticModel, cancellationToken)
                    ?? FindDocumentationSymbol(conditionalAccess.Expression, semanticModel, cancellationToken);
            case MemberBindingExpressionSyntax memberBinding:
                return semanticModel.GetSymbolInfo(memberBinding, cancellationToken).Symbol;
            case IdentifierNameSyntax identifier:
                return semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            case InvocationExpressionSyntax invocation:
                foreach (var lambda in invocation.ArgumentList.Arguments.Select(argument => argument.Expression).OfType<LambdaExpressionSyntax>())
                {
                    if (lambda.Body is ExpressionSyntax lambdaExpression)
                    {
                        var nested = FindDocumentationSymbol(lambdaExpression, semanticModel, cancellationToken);
                        if (nested is not null)
                        {
                            return nested;
                        }
                    }
                }

                return semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol;
            case ConditionalExpressionSyntax conditionalExpression:
                return FindDocumentationSymbol(conditionalExpression.WhenTrue, semanticModel, cancellationToken)
                    ?? FindDocumentationSymbol(conditionalExpression.WhenFalse, semanticModel, cancellationToken);
            case AnonymousObjectCreationExpressionSyntax anonymousObject:
                return anonymousObject.Initializers
                    .Select(initializer => FindDocumentationSymbol(initializer.Expression, semanticModel, cancellationToken))
                    .FirstOrDefault(symbol => symbol is not null);
            case ObjectCreationExpressionSyntax objectCreation:
                return objectCreation.Initializer?.Expressions
                    .Select(initializer => FindDocumentationSymbol(initializer, semanticModel, cancellationToken))
                    .FirstOrDefault(symbol => symbol is not null);
            case AssignmentExpressionSyntax assignment:
                return FindDocumentationSymbol(assignment.Right, semanticModel, cancellationToken);
            default:
                return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        }
    }

    private static string? ExtractSummary(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(xml);
            return document
                .Descendants("summary")
                .Select(summary => Normalize(summary.Value))
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractCommentAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.Name == "CommentAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static string? ExtractDisplayAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.Name == "DisplayAttribute")
            ?.NamedArguments.FirstOrDefault(argument => argument.Key == "Name")
            .Value.Value as string;
    }

    private static string? ExtractSingleLineComment(ISymbol symbol)
    {
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is null)
        {
            return null;
        }

        foreach (var trivia in syntax.GetLeadingTrivia())
        {
            if (trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia))
            {
                var comment = trivia.ToString().TrimStart('/').Trim();
                if (!string.IsNullOrWhiteSpace(comment))
                {
                    return comment;
                }
            }
        }

        return null;
    }

    private static string BuildRemarks(ISymbol symbol)
    {
        var parts = new List<string> { $"From: <c>{BuildSymbolPath(symbol)}</c>" };
        var attributes = symbol.GetAttributes()
            .Select(attribute => attribute.AttributeClass?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();

        if (attributes.Count != 0)
        {
            parts.Add($"Attributes: <c>[{string.Join("], [", attributes)}]</c>");
        }

        // TODO: The public docs do not define the exact remarks wording for every supported comment source.
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
        return string.Join(" ", value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
    }
}
