using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Linqraft.Analyzer;

[
    ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LinqraftCompositeCodeFixProvider)),
    Shared
]
public sealed class LinqraftCompositeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            "LQRF001",
            "LQRS001",
            "LQRS002",
            "LQRS003",
            "LQRS004",
            "LQRS005",
            "LQRS006",
            "LQRW003",
            "LQRW004",
            "LQRE001",
            "LQRE002"
        );

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            switch (diagnostic.Id)
            {
                case "LQRS001":
                    RegisterSelectExprToTyped(context, node);
                    break;
                case "LQRS002":
                case "LQRS005":
                    RegisterSelectAnonymousFixes(context, node);
                    break;
                case "LQRS003":
                case "LQRS006":
                    RegisterSelectNamedFixes(context, node);
                    break;
                case "LQRS004":
                    RegisterTernaryFix(context, node);
                    break;
                case "LQRW003":
                    RegisterUnnecessaryCaptureFix(context, node, diagnostic);
                    break;
                case "LQRW004":
                    RegisterAnonymousCaptureFix(context, node);
                    break;
                case "LQRE001":
                    RegisterMissingCaptureFix(context, node, diagnostic);
                    break;
                case "LQRE002":
                    RegisterGroupByKeyFix(context, node);
                    break;
                case "LQRF001":
                    RegisterProducesResponseTypeFix(context, node);
                    break;
            }
        }
    }

    private static void RegisterSelectExprToTyped(CodeFixContext context, SyntaxNode node)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr<T, TDto>",
                cancellationToken =>
                    ConvertSelectExprToTypedAsync(context.Document, invocation, cancellationToken),
                "LQRS001"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterSelectAnonymousFixes(CodeFixContext context, SyntaxNode node)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr",
                cancellationToken =>
                    ConvertSelectAnonymousAsync(
                        context.Document,
                        invocation,
                        false,
                        cancellationToken
                    ),
                "LQRS002.Anonymous"
            ),
            context.Diagnostics
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr<T, TDto>",
                cancellationToken =>
                    ConvertSelectAnonymousAsync(
                        context.Document,
                        invocation,
                        true,
                        cancellationToken
                    ),
                "LQRS002.Explicit"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterSelectNamedFixes(CodeFixContext context, SyntaxNode node)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr<T, TDto>",
                cancellationToken =>
                    ConvertSelectNamedAsync(
                        context.Document,
                        invocation,
                        simplifyTernary: true,
                        keepNamedDto: false,
                        cancellationToken
                    ),
                "LQRS003.Explicit"
            ),
            context.Diagnostics
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr<T, TDto> (strict)",
                cancellationToken =>
                    ConvertSelectNamedAsync(
                        context.Document,
                        invocation,
                        simplifyTernary: false,
                        keepNamedDto: false,
                        cancellationToken
                    ),
                "LQRS003.Strict"
            ),
            context.Diagnostics
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr (use predefined classes)",
                cancellationToken =>
                    ConvertSelectNamedAsync(
                        context.Document,
                        invocation,
                        simplifyTernary: false,
                        keepNamedDto: true,
                        cancellationToken
                    ),
                "LQRS003.Predefined"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterTernaryFix(CodeFixContext context, SyntaxNode node)
    {
        var conditionalExpression = node.FirstAncestorOrSelf<ConditionalExpressionSyntax>();
        if (conditionalExpression is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Simplify null-check ternary",
                cancellationToken =>
                    SimplifyTernaryAsync(
                        context.Document,
                        conditionalExpression,
                        cancellationToken
                    ),
                "LQRS004"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterUnnecessaryCaptureFix(
        CodeFixContext context,
        SyntaxNode node,
        Diagnostic diagnostic
    )
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        var captureName = diagnostic.Properties.TryGetValue("CaptureName", out var value)
            ? value
            : null;
        if (string.IsNullOrWhiteSpace(captureName))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Remove capture '{captureName}'",
                cancellationToken =>
                    RemoveCaptureAsync(
                        context.Document,
                        invocation,
                        captureName!,
                        cancellationToken
                    ),
                $"LQRW003.{captureName}"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterAnonymousCaptureFix(CodeFixContext context, SyntaxNode node)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert capture to delegate pattern",
                cancellationToken =>
                    ConvertCaptureToDelegateAsync(context.Document, invocation, cancellationToken),
                "LQRW004"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterMissingCaptureFix(
        CodeFixContext context,
        SyntaxNode node,
        Diagnostic diagnostic
    )
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        var captureName = diagnostic.Properties.TryGetValue("CaptureName", out var value)
            ? value
            : null;
        if (string.IsNullOrWhiteSpace(captureName))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Add capture '{captureName}'",
                cancellationToken =>
                    AddCaptureAsync(context.Document, invocation, captureName!, cancellationToken),
                $"LQRE001.{captureName}"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterGroupByKeyFix(CodeFixContext context, SyntaxNode node)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use GroupByExpr",
                cancellationToken =>
                    ConvertGroupByToGroupByExprAsync(
                        context.Document,
                        invocation,
                        cancellationToken
                    ),
                "LQRE002"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterProducesResponseTypeFix(CodeFixContext context, SyntaxNode node)
    {
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add ProducesResponseType",
                cancellationToken =>
                    AddProducesResponseTypeAsync(context.Document, method, cancellationToken),
                "LQRF001"
            ),
            context.Diagnostics
        );
    }

    private static async Task<Document> ConvertSelectExprToTypedAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        return await ReplaceInvocationWithTypedSelectExprAsync(
                document,
                invocation,
                keepNamedProjection: false,
                simplifyTernary: false,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<Document> ConvertSelectAnonymousAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        bool explicitDto,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var updatedInvocation = RenameInvocation(invocation, "SelectExpr");
        var originalLambda = AnalyzerHelpers.GetSelectorLambda(invocation);
        var captureNames = originalLambda is null
            ? Array.Empty<string>()
            : GetMissingCaptureNames(invocation, originalLambda, semanticModel, cancellationToken);
        if (explicitDto)
        {
            if (originalLambda is null)
            {
                return document;
            }

            updatedInvocation = AddTypeArguments(
                updatedInvocation,
                GetSelectSourceTypeName(invocation, semanticModel, cancellationToken),
                GetSelectDtoTypeName(invocation, originalLambda, semanticModel, cancellationToken)
            );
        }

        var lambda = AnalyzerHelpers.GetSelectorLambda(updatedInvocation);
        if (
            lambda is not null
            && AnalyzerHelpers.GetLambdaExpressionBody(lambda)
                is AnonymousObjectCreationExpressionSyntax anonymousObject
        )
        {
            updatedInvocation = updatedInvocation.ReplaceNode(
                anonymousObject,
                RewriteProjectionExpression(
                    anonymousObject,
                    simplifyTernary: true,
                    convertNamedObjectsToAnonymous: false,
                    semanticModel,
                    cancellationToken
                )
            );
        }

        updatedInvocation = AddCaptureNames(updatedInvocation, captureNames);
        return WithFormattedSyntaxRoot(document, root.ReplaceNode(invocation, updatedInvocation));
    }

    private static async Task<Document> ConvertSelectNamedAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        bool simplifyTernary,
        bool keepNamedDto,
        CancellationToken cancellationToken
    )
    {
        if (keepNamedDto)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document
                .GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(false);
            var lambda = AnalyzerHelpers.GetSelectorLambda(invocation);
            if (root is null || semanticModel is null || lambda is null)
            {
                return document;
            }

            var updatedInvocation = AddCaptureNames(
                RenameInvocation(invocation, "SelectExpr"),
                GetMissingCaptureNames(invocation, lambda, semanticModel, cancellationToken)
            );
            return WithFormattedSyntaxRoot(
                document,
                root.ReplaceNode(invocation, updatedInvocation)
            );
        }

        return await ReplaceInvocationWithTypedSelectExprAsync(
                document,
                invocation,
                keepNamedProjection: false,
                simplifyTernary,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<Document> ReplaceInvocationWithTypedSelectExprAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        bool keepNamedProjection,
        bool simplifyTernary,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var lambda = AnalyzerHelpers.GetSelectorLambda(invocation);
        if (lambda is null)
        {
            return document;
        }

        var captureNames = GetMissingCaptureNames(
            invocation,
            lambda,
            semanticModel,
            cancellationToken
        );
        var updatedInvocation = RenameInvocation(invocation, "SelectExpr");
        updatedInvocation = AddTypeArguments(
            updatedInvocation,
            GetSelectSourceTypeName(invocation, semanticModel, cancellationToken),
            GetSelectDtoTypeName(invocation, lambda, semanticModel, cancellationToken)
        );

        var updatedLambda = AnalyzerHelpers.GetSelectorLambda(updatedInvocation);
        if (
            !keepNamedProjection
            && updatedLambda is not null
            && AnalyzerHelpers.GetLambdaExpressionBody(lambda)
                is ObjectCreationExpressionSyntax originalObjectCreation
            && AnalyzerHelpers.GetLambdaExpressionBody(updatedLambda)
                is ObjectCreationExpressionSyntax updatedObjectCreation
        )
        {
            var converted = RewriteProjectionExpression(
                originalObjectCreation,
                simplifyTernary,
                convertNamedObjectsToAnonymous: true,
                semanticModel,
                cancellationToken
            );
            updatedInvocation = updatedInvocation.ReplaceNode(updatedObjectCreation, converted);
        }

        updatedInvocation = AddCaptureNames(updatedInvocation, captureNames);
        return WithFormattedSyntaxRoot(document, root.ReplaceNode(invocation, updatedInvocation));
    }

    private static async Task<Document> SimplifyTernaryAsync(
        Document document,
        ConditionalExpressionSyntax conditionalExpression,
        CancellationToken cancellationToken
    )
    {
        var simplified = TrySimplifyConditionalExpression(conditionalExpression);
        if (simplified is null)
        {
            return document;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        return WithFormattedSyntaxRoot(
            document,
            root.ReplaceNode(conditionalExpression, simplified)
        );
    }

    private static async Task<Document> RemoveCaptureAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string captureName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var captureArgument = GetCaptureArgument(invocation);
        var captureExpressions = GetCaptureExpressions(captureArgument);
        if (captureArgument is null || captureExpressions.Count == 0)
        {
            return document;
        }

        var remaining = captureExpressions
            .Where(expression => AnalyzerHelpers.GetCaptureMemberName(expression) != captureName)
            .ToList();

        if (remaining.Count == 0)
        {
            var newArguments = invocation.ArgumentList.Arguments.Remove(captureArgument);
            var updatedInvocation = invocation.WithArgumentList(
                invocation.ArgumentList.WithArguments(newArguments)
            );
            return WithFormattedSyntaxRoot(
                document,
                root.ReplaceNode(invocation, updatedInvocation)
            );
        }

        var updatedInvocationWithCapture = invocation.ReplaceNode(
            captureArgument,
            CreateCaptureArgument(remaining)
        );
        return WithFormattedSyntaxRoot(
            document,
            root.ReplaceNode(invocation, updatedInvocationWithCapture)
        );
    }

    private static async Task<Document> AddCaptureAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string captureName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updatedInvocation = AddCaptureNames(invocation, [captureName]);
        return WithFormattedSyntaxRoot(document, root.ReplaceNode(invocation, updatedInvocation));
    }

    private static async Task<Document> ConvertCaptureToDelegateAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var captureArgument = GetCaptureArgument(invocation);
        var captureExpressions = GetCaptureExpressions(captureArgument);
        if (
            captureArgument?.Expression is not AnonymousObjectCreationExpressionSyntax
            || captureExpressions.Count == 0
        )
        {
            return document;
        }

        var updatedInvocation = invocation.ReplaceNode(
            captureArgument,
            CreateCaptureArgument(captureExpressions)
        );
        return WithFormattedSyntaxRoot(document, root.ReplaceNode(invocation, updatedInvocation));
    }

    private static async Task<Document> ConvertGroupByToGroupByExprAsync(
        Document document,
        InvocationExpressionSyntax selectExprInvocation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        if (selectExprInvocation.Expression is not MemberAccessExpressionSyntax selectExprAccess)
        {
            return document;
        }

        if (
            selectExprAccess.Expression is not InvocationExpressionSyntax groupByInvocation
            || AnalyzerHelpers.GetInvocationName(groupByInvocation.Expression) != "GroupBy"
        )
        {
            return document;
        }

        var receiver = (groupByInvocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        if (receiver is null)
        {
            return document;
        }

        var keyLambda = AnalyzerHelpers.GetSelectorLambda(groupByInvocation);
        if (keyLambda is null)
        {
            return document;
        }

        var projLambda = AnalyzerHelpers.GetSelectorLambda(selectExprInvocation);
        if (projLambda is null)
        {
            return document;
        }

        var newInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                SyntaxFactory.IdentifierName("GroupByExpr")
            ),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(
                    new[]
                    {
                        SyntaxFactory.Argument(keyLambda.WithoutTrivia()),
                        SyntaxFactory.Argument(projLambda.WithoutTrivia()),
                    }
                )
            )
        );

        return WithFormattedSyntaxRoot(
            document,
            root.ReplaceNode(selectExprInvocation, newInvocation)
        );
    }

    private static async Task<Document> AddProducesResponseTypeAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var selectExpr = method
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(AnalyzerHelpers.IsSelectExprInvocation);
        if (
            selectExpr is null
            || AnalyzerHelpers.GetInvocationNameSyntax(selectExpr.Expression)
                is not GenericNameSyntax genericName
            || genericName.TypeArgumentList.Arguments.Count < 2
        )
        {
            return document;
        }

        var dtoTypeSyntax = genericName.TypeArgumentList.Arguments[1];
        var dtoTypeSymbol = semanticModel.GetTypeInfo(dtoTypeSyntax, cancellationToken).Type;
        var (statusCode, responseType) = GetProducesResponseTypeMetadata(
            method,
            selectExpr,
            dtoTypeSymbol,
            semanticModel,
            cancellationToken
        );
        var namespaces = new HashSet<string>(StringComparer.Ordinal) { "Microsoft.AspNetCore.Mvc" };
        var responseTypeText = responseType?.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat
        );
        if (responseType is not null)
        {
            CollectNamespaces(responseType, namespaces);
        }

        responseTypeText ??= dtoTypeSymbol?.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat
        );
        if (!string.IsNullOrWhiteSpace(responseTypeText))
        {
            CollectNamespaces(dtoTypeSymbol, namespaces);
        }

        responseTypeText ??= dtoTypeSyntax.ToString();
        var attributeArguments = new List<AttributeArgumentSyntax>
        {
            SyntaxFactory.AttributeArgument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    SyntaxFactory.Literal(statusCode)
                )
            ),
            SyntaxFactory.AttributeArgument(
                nameEquals: SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName("Type")),
                nameColon: default,
                expression: SyntaxFactory.TypeOfExpression(
                    SyntaxFactory.ParseTypeName(responseTypeText)
                )
            ),
        };
        var attribute = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("ProducesResponseType"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SeparatedList(attributeArguments)
                    )
                )
            )
        );

        var updatedMethod = method.AddAttributeLists(attribute);
        var updatedRoot = root.ReplaceNode(method, updatedMethod);
        updatedRoot = EnsureUsings(updatedRoot, namespaces.ToArray());
        return WithFormattedSyntaxRoot(document, updatedRoot);
    }

    private static (int StatusCode, ITypeSymbol? ResponseType) GetProducesResponseTypeMetadata(
        MethodDeclarationSyntax method,
        InvocationExpressionSyntax selectExpr,
        ITypeSymbol? dtoType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        int? inferredStatusCode = null;
        foreach (var returnExpression in GetReturnExpressions(method))
        {
            if (
                !TryGetProducesResponseTypeCandidate(
                    returnExpression,
                    semanticModel,
                    cancellationToken,
                    out var statusCode,
                    out var payloadType
                )
            )
            {
                continue;
            }

            inferredStatusCode ??= statusCode;
            if (
                TryMatchProducesResponseType(
                    payloadType,
                    dtoType,
                    semanticModel.Compilation,
                    out var responseType
                )
            )
            {
                return (statusCode, responseType);
            }
        }

        return (
            inferredStatusCode ?? 200,
            GetFallbackProducesResponseType(selectExpr, dtoType, semanticModel, cancellationToken)
        );
    }

    private static ITypeSymbol? GetFallbackProducesResponseType(
        InvocationExpressionSyntax selectExpr,
        ITypeSymbol? dtoType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var payloadType = GetTypeInfo(
            GetOutermostExpression(selectExpr),
            semanticModel,
            cancellationToken
        );
        if (
            TryMatchProducesResponseType(
                payloadType,
                dtoType,
                semanticModel.Compilation,
                out var responseType
            )
        )
        {
            return responseType;
        }

        return dtoType;
    }

    private static IEnumerable<ExpressionSyntax> GetReturnExpressions(
        MethodDeclarationSyntax method
    )
    {
        if (method.ExpressionBody is not null)
        {
            yield return method.ExpressionBody.Expression;
        }

        if (method.Body is null)
        {
            yield break;
        }

        foreach (
            var expression in method
                .Body.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(returnStatement => returnStatement.Expression is not null)
                .Select(returnStatement => returnStatement.Expression!)
        )
        {
            yield return expression;
        }
    }

    private static bool TryGetProducesResponseTypeCandidate(
        ExpressionSyntax returnExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out int statusCode,
        out ITypeSymbol? payloadType
    )
    {
        returnExpression = UnwrapReturnExpression(returnExpression);
        if (returnExpression is not InvocationExpressionSyntax invocation)
        {
            statusCode = 0;
            payloadType = null;
            return false;
        }

        switch (AnalyzerHelpers.GetInvocationName(invocation.Expression))
        {
            case "Ok":
                statusCode = 200;
                payloadType = GetArgumentType(invocation, 0, semanticModel, cancellationToken);
                return true;
            case "Created":
            case "CreatedAtAction":
            case "CreatedAtRoute":
                statusCode = 201;
                payloadType = GetLastArgumentType(invocation, semanticModel, cancellationToken);
                return true;
            case "Accepted":
            case "AcceptedAtAction":
            case "AcceptedAtRoute":
                statusCode = 202;
                payloadType = GetLastArgumentType(invocation, semanticModel, cancellationToken);
                return true;
            case "BadRequest":
                statusCode = 400;
                payloadType = GetArgumentType(invocation, 0, semanticModel, cancellationToken);
                return true;
            case "Unauthorized":
                statusCode = 401;
                payloadType = GetArgumentType(invocation, 0, semanticModel, cancellationToken);
                return true;
            case "Forbid":
                statusCode = 403;
                payloadType = null;
                return true;
            case "NotFound":
                statusCode = 404;
                payloadType = GetArgumentType(invocation, 0, semanticModel, cancellationToken);
                return true;
            case "Conflict":
                statusCode = 409;
                payloadType = GetArgumentType(invocation, 0, semanticModel, cancellationToken);
                return true;
            case "StatusCode":
                var statusCodeArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
                if (statusCodeArgument is null)
                {
                    break;
                }

                var constantValue = semanticModel.GetConstantValue(
                    statusCodeArgument.Expression,
                    cancellationToken
                );
                if (!constantValue.HasValue || constantValue.Value is not int constantStatusCode)
                {
                    break;
                }

                statusCode = constantStatusCode;
                payloadType = GetArgumentType(invocation, 1, semanticModel, cancellationToken);
                return true;
        }

        statusCode = 0;
        payloadType = null;
        return false;
    }

    private static ExpressionSyntax UnwrapReturnExpression(ExpressionSyntax expression)
    {
        expression = UnwrapParentheses(expression);
        while (expression is AwaitExpressionSyntax awaitExpression)
        {
            expression = UnwrapParentheses(awaitExpression.Expression);
        }

        return expression;
    }

    private static ITypeSymbol? GetArgumentType(
        InvocationExpressionSyntax invocation,
        int argumentIndex,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (argumentIndex < 0 || argumentIndex >= invocation.ArgumentList.Arguments.Count)
        {
            return null;
        }

        return GetTypeInfo(
            invocation.ArgumentList.Arguments[argumentIndex].Expression,
            semanticModel,
            cancellationToken
        );
    }

    private static ITypeSymbol? GetLastArgumentType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        return GetArgumentType(
            invocation,
            invocation.ArgumentList.Arguments.Count - 1,
            semanticModel,
            cancellationToken
        );
    }

    private static ITypeSymbol? GetTypeInfo(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.Type ?? typeInfo.ConvertedType;
    }

    private static bool TryMatchProducesResponseType(
        ITypeSymbol? payloadType,
        ITypeSymbol? dtoType,
        Compilation compilation,
        out ITypeSymbol? responseType
    )
    {
        if (dtoType is null)
        {
            responseType = payloadType;
            return payloadType is not null;
        }

        if (payloadType is null)
        {
            responseType = null;
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(payloadType, dtoType))
        {
            responseType = dtoType;
            return true;
        }

        if (
            payloadType is IArrayTypeSymbol arrayType
            && SymbolEqualityComparer.Default.Equals(arrayType.ElementType, dtoType)
        )
        {
            responseType = arrayType;
            return true;
        }

        if (
            !TryGetSequenceElementType(payloadType, out var elementType)
            || !SymbolEqualityComparer.Default.Equals(elementType, dtoType)
        )
        {
            responseType = null;
            return false;
        }

        responseType = IsListType(payloadType)
            ? payloadType
            : CreateConstructedType(
                compilation,
                "System.Collections.Generic.IEnumerable`1",
                dtoType
            ) ?? payloadType;
        return true;
    }

    private static bool TryGetSequenceElementType(
        ITypeSymbol? sequenceType,
        out ITypeSymbol? elementType
    )
    {
        if (sequenceType is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (
            sequenceType is not INamedTypeSymbol namedType
            || sequenceType.SpecialType == SpecialType.System_String
        )
        {
            elementType = null;
            return false;
        }

        if (IsSequenceTypeDefinition(namedType))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        var matchingInterface = namedType.AllInterfaces.FirstOrDefault(IsSequenceTypeDefinition);
        if (matchingInterface is not null)
        {
            elementType = matchingInterface.TypeArguments[0];
            return true;
        }

        elementType = null;
        return false;
    }

    private static bool IsSequenceTypeDefinition(INamedTypeSymbol type)
    {
        return type.TypeArguments.Length == 1
            && type.OriginalDefinition.ToDisplayString()
                is "System.Collections.Generic.IEnumerable<T>"
                    or "System.Collections.Generic.IAsyncEnumerable<T>"
                    or "System.Linq.IQueryable<T>"
                    or "System.Linq.IOrderedQueryable<T>";
    }

    private static bool IsListType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType
            && namedType.OriginalDefinition.ToDisplayString()
                == "System.Collections.Generic.List<T>";
    }

    private static INamedTypeSymbol? CreateConstructedType(
        Compilation compilation,
        string metadataName,
        ITypeSymbol typeArgument
    )
    {
        return compilation.GetTypeByMetadataName(metadataName)?.Construct(typeArgument);
    }

    private static ExpressionSyntax GetOutermostExpression(ExpressionSyntax expression)
    {
        var current = expression;
        while (true)
        {
            if (
                current.Parent is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Expression == current
                && memberAccess.Parent is InvocationExpressionSyntax invocation
                && invocation.Expression == memberAccess
            )
            {
                current = invocation;
                continue;
            }

            if (current.Parent is AwaitExpressionSyntax awaitExpression)
            {
                current = awaitExpression;
                continue;
            }

            return current;
        }
    }

    private static void CollectNamespaces(ITypeSymbol? type, ISet<string> namespaces)
    {
        switch (type)
        {
            case null:
                return;
            case IArrayTypeSymbol arrayType:
                CollectNamespaces(arrayType.ElementType, namespaces);
                return;
            case INamedTypeSymbol namedType:
                if (!namedType.ContainingNamespace.IsGlobalNamespace)
                {
                    namespaces.Add(namedType.ContainingNamespace.ToDisplayString());
                }

                foreach (var typeArgument in namedType.TypeArguments)
                {
                    CollectNamespaces(typeArgument, namespaces);
                }

                return;
        }
    }

    private static InvocationExpressionSyntax RenameInvocation(
        InvocationExpressionSyntax invocation,
        string newName
    )
    {
        return invocation.WithExpression(
            invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(
                    SyntaxFactory.IdentifierName(newName)
                ),
                _ => invocation.Expression,
            }
        );
    }

    private static InvocationExpressionSyntax AddTypeArguments(
        InvocationExpressionSyntax invocation,
        string sourceType,
        string dtoName
    )
    {
        var genericName = (SimpleNameSyntax)
            SyntaxFactory.ParseName($"SelectExpr<{sourceType}, {dtoName}>");

        return invocation.WithExpression(
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.WithName(genericName)
                : invocation.Expression
        );
    }

    private static string[] GetMissingCaptureNames(
        InvocationExpressionSyntax invocation,
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var existingCaptureNames = new HashSet<string>(
            AnalyzerHelpers.GetCaptureNames(invocation),
            StringComparer.Ordinal
        );
        return AnalyzerHelpers
            .CollectOuterReferences(lambda, semanticModel, cancellationToken)
            .Distinct(SymbolEqualityComparer.Default)
            .Where(reference => existingCaptureNames.Add(reference.Name))
            .Select(reference => reference.Name)
            .ToArray();
    }

    private static InvocationExpressionSyntax AddCaptureNames(
        InvocationExpressionSyntax invocation,
        IEnumerable<string> captureNames
    )
    {
        var pendingCaptureNames = captureNames
            .Where(captureName => !string.IsNullOrWhiteSpace(captureName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (pendingCaptureNames.Length == 0)
        {
            return invocation;
        }

        var captureArgument = GetCaptureArgument(invocation);
        var captureExpressions = GetCaptureExpressions(captureArgument);
        if (captureExpressions.Count != 0)
        {
            var existingCaptureNames = new HashSet<string>(
                captureExpressions.Select(AnalyzerHelpers.GetCaptureMemberName),
                StringComparer.Ordinal
            );
            var updatedCaptureExpressions = captureExpressions.ToList();
            foreach (var captureName in pendingCaptureNames)
            {
                if (!existingCaptureNames.Add(captureName))
                {
                    continue;
                }

                updatedCaptureExpressions.Add(SyntaxFactory.IdentifierName(captureName));
            }

            if (captureArgument is null)
            {
                return invocation;
            }

            return invocation.ReplaceNode(
                captureArgument,
                CreateCaptureArgument(updatedCaptureExpressions)
            );
        }

        return invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(
                invocation.ArgumentList.Arguments.Add(CreateCaptureArgument(pendingCaptureNames))
            )
        );
    }

    private static ArgumentSyntax? GetCaptureArgument(InvocationExpressionSyntax invocation)
    {
        return AnalyzerHelpers.GetCaptureArgument(invocation);
    }

    private static IReadOnlyList<ExpressionSyntax> GetCaptureExpressions(
        ArgumentSyntax? captureArgument
    )
    {
        return captureArgument is null
            ? []
            : AnalyzerHelpers.GetCaptureExpressions(captureArgument);
    }

    private static ArgumentSyntax CreateCaptureArgument(IEnumerable<string> captureNames)
    {
        return CreateCaptureArgument(
            captureNames.Select(captureName =>
                (ExpressionSyntax)SyntaxFactory.IdentifierName(captureName)
            )
        );
    }

    private static ArgumentSyntax CreateCaptureArgument(
        IEnumerable<ExpressionSyntax> captureExpressions
    )
    {
        var expressions = captureExpressions
            .Select(expression => expression.WithoutTrivia())
            .Where(expression => !string.IsNullOrWhiteSpace(expression.ToString()))
            .ToArray();
        if (expressions.Length == 0)
        {
            throw new InvalidOperationException(
                "Capture expressions cannot be empty. This indicates an internal code fix error."
            );
        }

        var captureBody = expressions.Length switch
        {
            1 => expressions[0],
            _ => SyntaxFactory.TupleExpression(
                SyntaxFactory.SeparatedList(
                    expressions.Select(expression => SyntaxFactory.Argument(expression))
                )
            ),
        };

        return SyntaxFactory.Argument(
            SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("capture")),
            default,
            SyntaxFactory.ParenthesizedLambdaExpression(SyntaxFactory.ParameterList(), captureBody)
        );
    }

    private static string GetSelectSourceTypeName(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var receiver = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
        var receiverType = receiver is null
            ? null
            : semanticModel.GetTypeInfo(receiver, cancellationToken).Type;
        if (receiverType is INamedTypeSymbol namedType)
        {
            var element =
                namedType.TypeArguments.FirstOrDefault()
                ?? namedType
                    .AllInterfaces.FirstOrDefault(interfaceType =>
                        interfaceType.TypeArguments.Length == 1
                    )
                    ?.TypeArguments[0];
            if (element is not null)
            {
                return element.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }
        }

        return "TSource";
    }

    private static string GetSelectDtoTypeName(
        InvocationExpressionSyntax invocation,
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (
            AnalyzerHelpers.GetLambdaExpressionBody(lambda)
            is ObjectCreationExpressionSyntax objectCreation
        )
        {
            var typeSymbol = semanticModel.GetTypeInfo(objectCreation.Type, cancellationToken).Type;
            if (typeSymbol is not null)
            {
                return typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }

            return objectCreation.Type.ToString();
        }

        var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var methodSymbol = method is null
            ? null
            : semanticModel.GetDeclaredSymbol(method, cancellationToken);
        var returnType = methodSymbol?.ReturnType as INamedTypeSymbol;
        if (returnType?.TypeArguments.Length == 1)
        {
            return returnType
                .TypeArguments[0]
                .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return AnalyzerHelpers.GenerateDtoName(invocation);
    }

    private static ExpressionSyntax RewriteProjectionExpression(
        ExpressionSyntax expression,
        bool simplifyTernary,
        bool convertNamedObjectsToAnonymous,
        SemanticModel? semanticModel = null,
        CancellationToken cancellationToken = default
    )
    {
        var rewriter = new ProjectionExpressionRewriter(
            simplifyTernary,
            convertNamedObjectsToAnonymous,
            semanticModel,
            cancellationToken
        );
        return (ExpressionSyntax)(rewriter.Visit(expression) ?? expression);
    }

    private static ExpressionSyntax? TrySimplifyConditionalExpression(
        ConditionalExpressionSyntax conditionalExpression
    )
    {
        if (
            !TryGetSimplifiableBranch(
                conditionalExpression,
                out var valueBranch,
                out var checkedPaths
            )
        )
        {
            return null;
        }

        return valueBranch switch
        {
            AnonymousObjectCreationExpressionSyntax anonymousObject => SimplifyAnonymousObject(
                anonymousObject,
                checkedPaths
            ),
            ObjectCreationExpressionSyntax objectCreation => SimplifyNamedObject(
                objectCreation,
                checkedPaths
            ),
            _ => SyntaxFactory.ParseExpression(SimplifyValueBranch(valueBranch, checkedPaths)),
        };
    }

    private static ExpressionSyntax SimplifyAnonymousObject(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        IReadOnlyList<string> checkedPaths
    )
    {
        var members = anonymousObject
            .Initializers.Select(initializer =>
                AppendValueWithContinuation(
                    $"{AnalyzerHelpers.GetAnonymousMemberName(initializer)} = ",
                    SimplifyValueBranch(initializer.Expression, checkedPaths)
                )
            )
            .ToList();
        return SyntaxFactory.ParseExpression(BuildInitializerExpression("new", members));
    }

    private static ExpressionSyntax SimplifyNamedObject(
        ObjectCreationExpressionSyntax objectCreation,
        IReadOnlyList<string> checkedPaths
    )
    {
        var header = objectCreation.ArgumentList is null
            ? $"new {objectCreation.Type}"
            : $"new {objectCreation.Type}{objectCreation.ArgumentList}";
        var members = objectCreation
            .Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
            .Select(assignment =>
                AppendValueWithContinuation(
                    $"{assignment.Left} = ",
                    SimplifyValueBranch(assignment.Right, checkedPaths)
                )
            )
            .ToList();
        if (members is null || members.Count == 0)
        {
            return SyntaxFactory.ParseExpression(header);
        }

        return SyntaxFactory.ParseExpression(BuildInitializerExpression(header, members));
    }

    private static bool TryGetSimplifiableBranch(
        ConditionalExpressionSyntax conditionalExpression,
        out ExpressionSyntax valueBranch,
        out IReadOnlyList<string> checkedPaths
    )
    {
        var collectedPaths = new List<string>();
        if (
            IsNullLike(conditionalExpression.WhenFalse)
            && TryCollectNullGuardPaths(
                conditionalExpression.Condition,
                valueWhenConditionTrue: true,
                collectedPaths
            )
        )
        {
            valueBranch = conditionalExpression.WhenTrue;
            checkedPaths = collectedPaths;
            return true;
        }

        collectedPaths = new List<string>();
        if (
            IsNullLike(conditionalExpression.WhenTrue)
            && TryCollectNullGuardPaths(
                conditionalExpression.Condition,
                valueWhenConditionTrue: false,
                collectedPaths
            )
        )
        {
            valueBranch = conditionalExpression.WhenFalse;
            checkedPaths = collectedPaths;
            return true;
        }

        valueBranch = conditionalExpression;
        checkedPaths = Array.Empty<string>();
        return false;
    }

    private static bool TryCollectNullGuardPaths(
        ExpressionSyntax condition,
        bool valueWhenConditionTrue,
        ICollection<string> checkedPaths
    )
    {
        condition = UnwrapParentheses(condition);
        if (condition is not BinaryExpressionSyntax binaryExpression)
        {
            return false;
        }

        if (
            valueWhenConditionTrue && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression)
            || !valueWhenConditionTrue && binaryExpression.IsKind(SyntaxKind.LogicalOrExpression)
        )
        {
            return TryCollectNullGuardPaths(
                    binaryExpression.Left,
                    valueWhenConditionTrue,
                    checkedPaths
                )
                && TryCollectNullGuardPaths(
                    binaryExpression.Right,
                    valueWhenConditionTrue,
                    checkedPaths
                );
        }

        if (!TryGetNullCheckedPath(binaryExpression, valueWhenConditionTrue, out var checkedPath))
        {
            return false;
        }

        checkedPaths.Add(checkedPath);
        return true;
    }

    private static bool TryGetNullCheckedPath(
        BinaryExpressionSyntax binaryExpression,
        bool valueWhenConditionTrue,
        out string checkedPath
    )
    {
        var matchesExpectedOperator = valueWhenConditionTrue
            ? binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)
            : binaryExpression.IsKind(SyntaxKind.EqualsExpression);
        if (!matchesExpectedOperator)
        {
            checkedPath = string.Empty;
            return false;
        }

        if (IsNullLike(binaryExpression.Right))
        {
            checkedPath = UnwrapParentheses(binaryExpression.Left).ToString();
            return !string.IsNullOrWhiteSpace(checkedPath);
        }

        if (IsNullLike(binaryExpression.Left))
        {
            checkedPath = UnwrapParentheses(binaryExpression.Right).ToString();
            return !string.IsNullOrWhiteSpace(checkedPath);
        }

        checkedPath = string.Empty;
        return false;
    }

    private static string SimplifyValueBranch(
        ExpressionSyntax expression,
        IReadOnlyList<string> checkedPaths
    )
    {
        expression = UnwrapParentheses(expression);
        if (expression is CastExpressionSyntax castExpression)
        {
            var rewrittenInner = ApplyNullConditionals(
                castExpression.Expression.ToString(),
                checkedPaths
            );
            if (CanDropNullableCast(castExpression.Type, rewrittenInner))
            {
                return rewrittenInner;
            }

            return $"({castExpression.Type}){rewrittenInner}";
        }

        return ApplyNullConditionals(expression.ToString(), checkedPaths);
    }

    private static bool CanDropNullableCast(TypeSyntax type, string expressionText)
    {
        return expressionText.Contains("?.", StringComparison.Ordinal)
            && (
                type is NullableTypeSyntax
                || type.ToString().Contains("Nullable<", StringComparison.Ordinal)
            );
    }

    private static string ApplyNullConditionals(
        string expressionText,
        IReadOnlyList<string> checkedPaths
    )
    {
        var rewritten = expressionText;
        foreach (
            var checkedPath in checkedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(path => path.Length)
        )
        {
            rewritten = ApplyNullConditional(rewritten, checkedPath);
        }

        return rewritten;
    }

    private static string ApplyNullConditional(string expressionText, string checkedPath)
    {
        var needle = checkedPath + ".";
        var replacement = checkedPath + "?.";
        var rewritten = expressionText;
        while (true)
        {
            var index = rewritten.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return rewritten;
            }

            rewritten = rewritten.Remove(index, needle.Length).Insert(index, replacement);
        }
    }

    private static bool IsNullLike(ExpressionSyntax expression)
    {
        expression = UnwrapParentheses(expression);
        return expression switch
        {
            LiteralExpressionSyntax literalExpression
                when literalExpression.IsKind(SyntaxKind.NullLiteralExpression) => true,
            CastExpressionSyntax castExpression => IsNullLike(castExpression.Expression),
            _ => false,
        };
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            expression = parenthesizedExpression.Expression;
        }

        return expression;
    }

    private static string BuildInitializerExpression(string header, IReadOnlyList<string> items)
    {
        if (!ShouldExpandInitializer(items))
        {
            return items.Count == 0
                ? $"{header} {{ }}"
                : $"{header} {{ {string.Join(", ", items)} }}";
        }

        var lines = new List<string> { $"{header} {{" };
        foreach (var item in items)
        {
            var itemLines = SplitLines(item);
            for (var index = 0; index < itemLines.Length; index++)
            {
                var suffix = index == itemLines.Length - 1 ? "," : string.Empty;
                lines.Add($"    {itemLines[index]}{suffix}");
            }
        }

        lines.Add("}");
        return string.Join("\n", lines);
    }

    private static bool ShouldExpandInitializer(IReadOnlyList<string> items)
    {
        return items.Count > 1 || items.Any(ContainsLineBreak);
    }

    private static string AppendValueWithContinuation(string prefix, string value)
    {
        var lines = SplitLines(value);
        if (lines.Length == 0)
        {
            return prefix;
        }

        if (lines.Length == 1)
        {
            return prefix + lines[0];
        }

        var formattedLines = new List<string> { prefix + lines[0] };
        formattedLines.AddRange(lines.Skip(1).Select(IndentAllLines));
        return string.Join("\n", formattedLines);
    }

    private static string IndentAllLines(string value)
    {
        var prefix = "    ";
        return string.Join("\n", SplitLines(value).Select(line => prefix + line));
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
    }

    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static Document WithFormattedSyntaxRoot(Document document, SyntaxNode root)
    {
        using var workspace = new AdhocWorkspace();
        return document.WithSyntaxRoot(Formatter.Format(root, workspace));
    }

    private sealed class ProjectionExpressionRewriter(
        bool simplifyTernary,
        bool convertNamedObjectsToAnonymous,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken
    ) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            var rewritten = (ConditionalExpressionSyntax)base.VisitConditionalExpression(node)!;
            if (!simplifyTernary)
            {
                return rewritten;
            }

            return TrySimplifyConditionalExpression(rewritten) ?? rewritten;
        }

        public override SyntaxNode? VisitObjectCreationExpression(
            ObjectCreationExpressionSyntax node
        )
        {
            var rewritten = (ObjectCreationExpressionSyntax)
                base.VisitObjectCreationExpression(node)!;
            if (!convertNamedObjectsToAnonymous)
            {
                return rewritten;
            }

            var targetType =
                semanticModel?.GetTypeInfo(node, cancellationToken).Type as INamedTypeSymbol;
            return ConvertObjectCreationToAnonymous(
                rewritten,
                targetType,
                semanticModel,
                node.SpanStart
            );
        }

        private static ExpressionSyntax ConvertObjectCreationToAnonymous(
            ObjectCreationExpressionSyntax objectCreation,
            INamedTypeSymbol? targetType,
            SemanticModel? semanticModel,
            int position
        )
        {
            if (
                objectCreation.ArgumentList is { Arguments.Count: > 0 }
                || objectCreation.Initializer is null
            )
            {
                return objectCreation;
            }

            var members = objectCreation
                .Initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                .Select(assignment =>
                    AppendValueWithContinuation(
                        $"{assignment.Left} = ",
                        AddAnonymousTargetTypeCastIfNeeded(
                                assignment.Right,
                                targetType is null
                                    ? null
                                    : GetObjectInitializerMemberType(targetType, assignment.Left),
                                semanticModel,
                                position
                            )
                            .ToString()
                    )
                )
                .ToList();

            return SyntaxFactory.ParseExpression(BuildInitializerExpression("new", members));
        }

        private static ExpressionSyntax AddAnonymousTargetTypeCastIfNeeded(
            ExpressionSyntax expression,
            ITypeSymbol? targetType,
            SemanticModel? semanticModel,
            int position
        )
        {
            expression = UnwrapParentheses(expression);
            if (
                targetType is null
                || expression is CastExpressionSyntax
                || expression is not ConditionalExpressionSyntax conditionalExpression
                || (
                    !IsNullLike(conditionalExpression.WhenTrue)
                    && !IsNullLike(conditionalExpression.WhenFalse)
                )
                || !NeedsTargetTypeCast(targetType)
            )
            {
                return expression;
            }

            var targetTypeText = semanticModel is null
                ? targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                : targetType.ToMinimalDisplayString(
                    semanticModel,
                    position,
                    SymbolDisplayFormat.MinimallyQualifiedFormat
                );
            return SyntaxFactory.CastExpression(
                SyntaxFactory.ParseTypeName(targetTypeText),
                SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia())
            );
        }

        private static ITypeSymbol? GetObjectInitializerMemberType(
            INamedTypeSymbol objectType,
            ExpressionSyntax assignmentTarget
        )
        {
            var memberName = GetAssignedMemberName(assignmentTarget);
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            for (
                INamedTypeSymbol? current = objectType;
                current is not null;
                current = current.BaseType
            )
            {
                var member = current.GetMembers(memberName).FirstOrDefault();
                switch (member)
                {
                    case IPropertySymbol propertySymbol:
                        return propertySymbol.Type;
                    case IFieldSymbol fieldSymbol:
                        return fieldSymbol.Type;
                }
            }

            return null;
        }

        private static bool NeedsTargetTypeCast(ITypeSymbol targetType)
        {
            return targetType.IsValueType || IsNullableValueType(targetType);
        }

        private static bool IsNullableValueType(ITypeSymbol type)
        {
            return type is INamedTypeSymbol namedType
                && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        private static string GetAssignedMemberName(ExpressionSyntax assignmentTarget)
        {
            return assignmentTarget switch
            {
                IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess =>
                    memberAccess.Name.Identifier.ValueText,
                _ => string.Empty,
            };
        }
    }

    private static SyntaxNode EnsureUsings(SyntaxNode root, params string[] namespaces)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return root;
        }

        var existing = new HashSet<string>(
            compilationUnit
                .Usings.Select(usingDirective => usingDirective.Name?.ToString())
                .OfType<string>(),
            StringComparer.Ordinal
        );
        var missingUsings = namespaces
            .Where(namespaceName =>
                !string.IsNullOrWhiteSpace(namespaceName) && !existing.Contains(namespaceName)
            )
            .Select(namespaceName =>
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            )
            .ToArray();

        return missingUsings.Length == 0
            ? compilationUnit
            : compilationUnit.WithUsings(compilationUnit.Usings.AddRange(missingUsings));
    }
}
