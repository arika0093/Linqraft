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
            "LQRF002",
            "LQRS001",
            "LQRS002",
            "LQRS003",
            "LQRS004",
            "LQRS005",
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
                    RegisterSelectAnonymousFixes(context, node);
                    break;
                case "LQRS003":
                    RegisterSelectNamedFixes(context, node);
                    break;
                case "LQRS004":
                    RegisterTernaryFix(context, node);
                    break;
                case "LQRS005":
                    RegisterUnnecessaryCaptureFix(context, node, diagnostic);
                    break;
                case "LQRE001":
                    RegisterMissingCaptureFix(context, node, diagnostic);
                    break;
                case "LQRE002":
                    RegisterGroupByKeyFix(context, node);
                    break;
                case "LQRF002":
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
                $"LQRS005.{captureName}"
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
                "Convert GroupBy key to named type",
                cancellationToken =>
                    ConvertGroupByKeyAsync(context.Document, invocation, cancellationToken),
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
                "LQRF002"
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
        if (root is null)
        {
            return document;
        }

        var updatedInvocation = RenameInvocation(invocation, "SelectExpr");
        var originalLambda = AnalyzerHelpers.GetSelectorLambda(invocation);
        if (explicitDto)
        {
            if (originalLambda is null)
            {
                return document;
            }

            var semanticModel = await document
                .GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(false);
            if (semanticModel is null)
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
                    convertNamedObjectsToAnonymous: false
                )
            );
        }

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
            if (root is null)
            {
                return document;
            }

            return document.WithSyntaxRoot(
                root.ReplaceNode(invocation, RenameInvocation(invocation, "SelectExpr"))
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
            && AnalyzerHelpers.GetLambdaExpressionBody(updatedLambda)
                is ObjectCreationExpressionSyntax objectCreation
        )
        {
            var converted = RewriteProjectionExpression(
                objectCreation,
                simplifyTernary,
                convertNamedObjectsToAnonymous: true
            );
            updatedInvocation = updatedInvocation.ReplaceNode(objectCreation, converted);
        }

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

        var captureArgument = invocation.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameColon?.Name.Identifier.ValueText == "capture"
            || (
                !argument.Equals(invocation.ArgumentList.Arguments.First())
                && argument.Expression is AnonymousObjectCreationExpressionSyntax
            )
        );

        if (
            captureArgument?.Expression is not AnonymousObjectCreationExpressionSyntax captureObject
        )
        {
            return document;
        }

        var remaining = captureObject
            .Initializers.Where(initializer =>
                AnalyzerHelpers.GetAnonymousMemberName(initializer) != captureName
            )
            .ToList();

        if (remaining.Count == 0)
        {
            var newArguments = invocation.ArgumentList.Arguments.Remove(captureArgument);
            var updatedInvocation = invocation.WithArgumentList(
                invocation.ArgumentList.WithArguments(newArguments)
            );
            return document.WithSyntaxRoot(root.ReplaceNode(invocation, updatedInvocation));
        }

        var updatedCapture = captureObject.WithInitializers(SyntaxFactory.SeparatedList(remaining));
        var updatedInvocationWithCapture = invocation.ReplaceNode(captureObject, updatedCapture);
        return document.WithSyntaxRoot(root.ReplaceNode(invocation, updatedInvocationWithCapture));
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

        var captureArgument = invocation.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameColon?.Name.Identifier.ValueText == "capture"
        );

        if (captureArgument?.Expression is AnonymousObjectCreationExpressionSyntax captureObject)
        {
            if (
                captureObject.Initializers.Any(initializer =>
                    AnalyzerHelpers.GetAnonymousMemberName(initializer) == captureName
                )
            )
            {
                return document;
            }

            var updatedCapture = captureObject.WithInitializers(
                captureObject.Initializers.Add(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(
                        SyntaxFactory.IdentifierName(captureName)
                    )
                )
            );
            var updatedInvocation = invocation.ReplaceNode(captureObject, updatedCapture);
            return document.WithSyntaxRoot(root.ReplaceNode(invocation, updatedInvocation));
        }

        var newCaptureArgument = SyntaxFactory.Argument(
            SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("capture")),
            default,
            SyntaxFactory.AnonymousObjectCreationExpression(
                SyntaxFactory.SeparatedList(
                    new[]
                    {
                        SyntaxFactory.AnonymousObjectMemberDeclarator(
                            SyntaxFactory.IdentifierName(captureName)
                        ),
                    }
                )
            )
        );

        var updatedInvocationWithCapture = invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(
                invocation.ArgumentList.Arguments.Add(newCaptureArgument)
            )
        );

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, updatedInvocationWithCapture));
    }

    private static async Task<Document> ConvertGroupByKeyAsync(
        Document document,
        InvocationExpressionSyntax selectExprInvocation,
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

        var groupByInvocation = selectExprInvocation
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                AnalyzerHelpers.GetInvocationName(invocation.Expression) == "GroupBy"
            );
        if (groupByInvocation is null)
        {
            return document;
        }

        var lambda = AnalyzerHelpers.GetSelectorLambda(groupByInvocation);
        if (
            lambda is null
            || AnalyzerHelpers.GetLambdaExpressionBody(lambda)
                is not AnonymousObjectCreationExpressionSyntax anonymousKey
        )
        {
            return document;
        }

        var receiver = (groupByInvocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        if (receiver is null)
        {
            return document;
        }

        var sourceType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type;
        var typeName =
            $"{GetSequenceElementName(sourceType) ?? sourceType?.Name ?? "Group"}GroupKey";
        var replacement = CreateObjectCreationFromAnonymous(anonymousKey, typeName);
        var updatedRoot = root.ReplaceNode(anonymousKey, replacement);
        var classText = CreateDtoClassText(typeName, anonymousKey);
        updatedRoot = AppendTypeDeclaration(updatedRoot, classText);
        return document.WithSyntaxRoot(updatedRoot);
    }

    private static async Task<Document> AddProducesResponseTypeAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
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

        var dtoType = genericName.TypeArgumentList.Arguments[1].ToString();
        var attribute = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(
                    SyntaxFactory.ParseName(
                        "global::Microsoft.AspNetCore.Mvc.ProducesResponseType"
                    ),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.ParseExpression($"typeof({dtoType})")
                            )
                        )
                    )
                )
            )
        );

        var updatedMethod = method.AddAttributeLists(attribute);
        return document.WithSyntaxRoot(root.ReplaceNode(method, updatedMethod));
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

    private static string? GetSequenceElementName(ITypeSymbol? sequenceType)
    {
        if (sequenceType is not INamedTypeSymbol namedType)
        {
            return null;
        }

        if (namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0].Name;
        }

        return namedType
            .AllInterfaces.FirstOrDefault(interfaceType => interfaceType.TypeArguments.Length == 1)
            ?.TypeArguments[0]
            .Name;
    }

    private static ExpressionSyntax ConvertObjectCreationToAnonymous(
        ObjectCreationExpressionSyntax objectCreation,
        bool simplifyTernary
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
                AppendValueWithContinuation($"{assignment.Left} = ", assignment.Right.ToString())
            )
            .ToList();

        return SyntaxFactory.ParseExpression(BuildInitializerExpression("new", members));
    }

    private static ExpressionSyntax RewriteProjectionExpression(
        ExpressionSyntax expression,
        bool simplifyTernary,
        bool convertNamedObjectsToAnonymous
    )
    {
        var rewriter = new ProjectionExpressionRewriter(
            simplifyTernary,
            convertNamedObjectsToAnonymous
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

        if (
            IsNullLike(conditionalExpression.WhenTrue)
            && TryCollectNullGuardPaths(
                conditionalExpression.Condition,
                valueWhenConditionTrue: false,
                collectedPaths = new List<string>()
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
        bool convertNamedObjectsToAnonymous
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

            return ConvertObjectCreationToAnonymous(rewritten, simplifyTernary);
        }
    }

    private static ObjectCreationExpressionSyntax CreateObjectCreationFromAnonymous(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string dtoName
    )
    {
        var members = anonymousObject.Initializers.Select(initializer =>
            $"{AnalyzerHelpers.GetAnonymousMemberName(initializer)} = {initializer.Expression}"
        );
        return (ObjectCreationExpressionSyntax)
            SyntaxFactory.ParseExpression($"new {dtoName} {{ {string.Join(", ", members)} }}");
    }

    private static string CreateDtoClassText(
        string dtoName,
        AnonymousObjectCreationExpressionSyntax anonymousObject
    )
    {
        var members = string.Join(
            "\r\n",
            anonymousObject.Initializers.Select(initializer =>
            {
                var typeName =
                    initializer.Expression is LiteralExpressionSyntax literalExpression
                    && literalExpression.IsKind(SyntaxKind.StringLiteralExpression)
                        ? "string"
                        : "object";
                return $"    public {typeName} {AnalyzerHelpers.GetAnonymousMemberName(initializer)} {{ get; set; }}";
            })
        );

        return $$"""
            public partial class {{dtoName}}
            {
            {{members}}
            }
            """;
    }

    private static SyntaxNode AppendTypeDeclaration(SyntaxNode root, string declarationText)
    {
        if (root is CompilationUnitSyntax compilationUnit)
        {
            var declaration = SyntaxFactory.ParseMemberDeclaration(declarationText);
            if (declaration is not null)
            {
                return compilationUnit.AddMembers(declaration);
            }
        }

        return root;
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
