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
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.Analyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LinqraftCompositeCodeFixProvider)), Shared]
public sealed class LinqraftCompositeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
        "LQRF001",
        "LQRF002",
        "LQRF003",
        "LQRF004",
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
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
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
                case "LQRF001":
                    RegisterAnonymousToDtoFixes(context, node);
                    break;
                case "LQRF002":
                    RegisterProducesResponseTypeFix(context, node);
                    break;
                case "LQRF003":
                    RegisterApiResponseMethodFix(context, node, asyncVersion: true);
                    break;
                case "LQRF004":
                    RegisterApiResponseMethodFix(context, node, asyncVersion: false);
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
                cancellationToken => ConvertSelectExprToTypedAsync(context.Document, invocation, cancellationToken),
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
                cancellationToken => ConvertSelectAnonymousAsync(context.Document, invocation, false, cancellationToken),
                "LQRS002.Anonymous"
            ),
            context.Diagnostics
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr<T, TDto>",
                cancellationToken => ConvertSelectAnonymousAsync(context.Document, invocation, true, cancellationToken),
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
                cancellationToken => ConvertSelectNamedAsync(context.Document, invocation, simplifyTernary: true, keepNamedDto: false, cancellationToken),
                "LQRS003.Explicit"
            ),
            context.Diagnostics
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr<T, TDto> (strict)",
                cancellationToken => ConvertSelectNamedAsync(context.Document, invocation, simplifyTernary: false, keepNamedDto: false, cancellationToken),
                "LQRS003.Strict"
            ),
            context.Diagnostics
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to SelectExpr (use predefined classes)",
                cancellationToken => ConvertSelectNamedAsync(context.Document, invocation, simplifyTernary: false, keepNamedDto: true, cancellationToken),
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
                cancellationToken => SimplifyTernaryAsync(context.Document, conditionalExpression, cancellationToken),
                "LQRS004"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterUnnecessaryCaptureFix(CodeFixContext context, SyntaxNode node, Diagnostic diagnostic)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        var captureName = diagnostic.Properties.TryGetValue("CaptureName", out var value) ? value : null;
        if (string.IsNullOrWhiteSpace(captureName))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Remove capture '{captureName}'",
                cancellationToken => RemoveCaptureAsync(context.Document, invocation, captureName!, cancellationToken),
                $"LQRS005.{captureName}"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterMissingCaptureFix(CodeFixContext context, SyntaxNode node, Diagnostic diagnostic)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        var captureName = diagnostic.Properties.TryGetValue("CaptureName", out var value) ? value : null;
        if (string.IsNullOrWhiteSpace(captureName))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Add capture '{captureName}'",
                cancellationToken => AddCaptureAsync(context.Document, invocation, captureName!, cancellationToken),
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
                cancellationToken => ConvertGroupByKeyAsync(context.Document, invocation, cancellationToken),
                "LQRE002"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterAnonymousToDtoFixes(CodeFixContext context, SyntaxNode node)
    {
        var anonymousObject = node.FirstAncestorOrSelf<AnonymousObjectCreationExpressionSyntax>();
        if (anonymousObject is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert anonymous type to DTO (current file)",
                cancellationToken => ConvertAnonymousToDtoInCurrentDocumentAsync(context.Document, anonymousObject, cancellationToken),
                "LQRF001.Current"
            ),
            context.Diagnostics
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert anonymous type to DTO (new file)",
                cancellationToken => ConvertAnonymousToDtoInNewDocumentAsync(context.Document, anonymousObject, cancellationToken),
                "LQRF001.New"
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
                cancellationToken => AddProducesResponseTypeAsync(context.Document, method, cancellationToken),
                "LQRF002"
            ),
            context.Diagnostics
        );
    }

    private static void RegisterApiResponseMethodFix(CodeFixContext context, SyntaxNode node, bool asyncVersion)
    {
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null)
        {
            return;
        }

        var title = asyncVersion
            ? "Convert to async API response method"
            : "Convert to synchronous API response method";

        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => ConvertApiResponseMethodAsync(context.Document, method, asyncVersion, cancellationToken),
                asyncVersion ? "LQRF003" : "LQRF004"
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
        ).ConfigureAwait(false);
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
        if (explicitDto)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                return document;
            }

            updatedInvocation = AddTypeArguments(
                updatedInvocation,
                GetSelectSourceTypeName(invocation, semanticModel, cancellationToken),
                AnalyzerHelpers.GenerateDtoName(invocation)
            );
        }

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, updatedInvocation));
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

            return document.WithSyntaxRoot(root.ReplaceNode(invocation, RenameInvocation(invocation, "SelectExpr")));
        }

        return await ReplaceInvocationWithTypedSelectExprAsync(
            document,
            invocation,
            keepNamedProjection: false,
            simplifyTernary,
            cancellationToken
        ).ConfigureAwait(false);
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
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
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
            AnalyzerHelpers.GenerateDtoName(invocation)
        );

        if (!keepNamedProjection && AnalyzerHelpers.GetLambdaExpressionBody(lambda) is ObjectCreationExpressionSyntax objectCreation)
        {
            var converted = ConvertObjectCreationToAnonymous(objectCreation, simplifyTernary);
            updatedInvocation = updatedInvocation.ReplaceNode(objectCreation, converted);
        }

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, updatedInvocation));
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

        return document.WithSyntaxRoot(root.ReplaceNode(conditionalExpression, simplified));
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

        var captureArgument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText == "capture"
                || (!argument.Equals(invocation.ArgumentList.Arguments.First()) && argument.Expression is AnonymousObjectCreationExpressionSyntax)
            );

        if (captureArgument?.Expression is not AnonymousObjectCreationExpressionSyntax captureObject)
        {
            return document;
        }

        var remaining = captureObject.Initializers
            .Where(initializer => AnalyzerHelpers.GetAnonymousMemberName(initializer) != captureName)
            .ToList();

        if (remaining.Count == 0)
        {
            var newArguments = invocation.ArgumentList.Arguments.Remove(captureArgument);
            var updatedInvocation = invocation.WithArgumentList(invocation.ArgumentList.WithArguments(newArguments));
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

        var captureArgument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == "capture");

        if (captureArgument?.Expression is AnonymousObjectCreationExpressionSyntax captureObject)
        {
            if (captureObject.Initializers.Any(initializer => AnalyzerHelpers.GetAnonymousMemberName(initializer) == captureName))
            {
                return document;
            }

            var updatedCapture = captureObject.WithInitializers(
                captureObject.Initializers.Add(
                    SyntaxFactory.AnonymousObjectMemberDeclarator(SyntaxFactory.IdentifierName(captureName))
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
                        SyntaxFactory.AnonymousObjectMemberDeclarator(SyntaxFactory.IdentifierName(captureName)),
                    }
                )
            )
        );

        var updatedInvocationWithCapture = invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(invocation.ArgumentList.Arguments.Add(newCaptureArgument))
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
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var groupByInvocation = selectExprInvocation.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation => AnalyzerHelpers.GetInvocationName(invocation.Expression) == "GroupBy");
        if (groupByInvocation is null)
        {
            return document;
        }

        var lambda = AnalyzerHelpers.GetSelectorLambda(groupByInvocation);
        if (lambda is null || AnalyzerHelpers.GetLambdaExpressionBody(lambda) is not AnonymousObjectCreationExpressionSyntax anonymousKey)
        {
            return document;
        }

        var receiver = (groupByInvocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        if (receiver is null)
        {
            return document;
        }

        var sourceType = semanticModel.GetTypeInfo(receiver, cancellationToken).Type;
        var typeName = $"{GetSequenceElementName(sourceType) ?? sourceType?.Name ?? "Group"}GroupKey";
        var replacement = CreateObjectCreationFromAnonymous(anonymousKey, typeName);
        var updatedRoot = root.ReplaceNode(anonymousKey, replacement);
        var classText = CreateDtoClassText(typeName, anonymousKey);
        updatedRoot = AppendTypeDeclaration(updatedRoot, classText);
        return document.WithSyntaxRoot(updatedRoot);
    }

    private static async Task<Document> ConvertAnonymousToDtoInCurrentDocumentAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var dtoName = AnalyzerHelpers.GenerateDtoName(anonymousObject);
        var replacement = CreateObjectCreationFromAnonymous(anonymousObject, dtoName);
        var updatedRoot = root.ReplaceNode(anonymousObject, replacement);
        updatedRoot = AppendTypeDeclaration(updatedRoot, CreateDtoClassText(dtoName, anonymousObject));
        return document.WithSyntaxRoot(updatedRoot);
    }

    private static async Task<Solution> ConvertAnonymousToDtoInNewDocumentAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var dtoName = AnalyzerHelpers.GenerateDtoName(anonymousObject);
        var replacement = CreateObjectCreationFromAnonymous(anonymousObject, dtoName);
        var updatedRoot = root.ReplaceNode(anonymousObject, replacement);
        var classText = CreateDtoClassText(dtoName, anonymousObject);
        var updatedSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
        var newDocumentName = $"{dtoName}.cs";
        updatedSolution = updatedSolution.AddDocument(
            DocumentId.CreateNewId(document.Project.Id),
            newDocumentName,
            SourceText.From(classText)
        );
        return updatedSolution;
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

        var selectExpr = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(AnalyzerHelpers.IsSelectExprInvocation);
        if (selectExpr is null
            || AnalyzerHelpers.GetInvocationNameSyntax(selectExpr.Expression) is not GenericNameSyntax genericName
            || genericName.TypeArgumentList.Arguments.Count < 2)
        {
            return document;
        }

        var dtoType = genericName.TypeArgumentList.Arguments[1].ToString();
        var attribute = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(
                    SyntaxFactory.ParseName("global::Microsoft.AspNetCore.Mvc.ProducesResponseType"),
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

    private static async Task<Document> ConvertApiResponseMethodAsync(
        Document document,
        MethodDeclarationSyntax method,
        bool asyncVersion,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null || method.Body is null)
        {
            return document;
        }

        var expressionStatement = method.Body.Statements.OfType<ExpressionStatementSyntax>()
            .FirstOrDefault(statement =>
                statement.Expression is InvocationExpressionSyntax invocation
                && AnalyzerHelpers.IsQueryableSelectInvocation(invocation, semanticModel, cancellationToken)
            );
        if (expressionStatement?.Expression is not InvocationExpressionSyntax statement)
        {
            return document;
        }

        var dtoName = AnalyzerHelpers.GenerateDtoName(statement);
        var selectExpr = AddTypeArguments(
            RenameInvocation(statement, "SelectExpr"),
            GetSelectSourceTypeName(statement, semanticModel, cancellationToken),
            dtoName
        );
        var chainText = selectExpr.ToString() + (asyncVersion ? ".ToListAsync()" : ".ToList()");
        var newStatement = asyncVersion
            ? (StatementSyntax)SyntaxFactory.ParseStatement($"return await {chainText};")
            : SyntaxFactory.ParseStatement($"return {chainText};");

        var newBody = method.Body.WithStatements(
            SyntaxFactory.List(
                method.Body.Statements.Select(statementSyntax => statementSyntax == expressionStatement ? newStatement : statementSyntax)
            )
        );
        var returnTypeText = asyncVersion
            ? $"Task<List<{dtoName}>>"
            : $"List<{dtoName}>";

        var updatedMethod = method
            .WithIdentifier(
                SyntaxFactory.Identifier(
                    asyncVersion && !method.Identifier.ValueText.EndsWith("Async", StringComparison.Ordinal)
                        ? method.Identifier.ValueText + "Async"
                        : method.Identifier.ValueText
                )
            )
            .WithReturnType(SyntaxFactory.ParseTypeName(returnTypeText))
            .WithBody(newBody)
            .WithExpressionBody(null)
            .WithSemicolonToken(default);

        if (asyncVersion && !updatedMethod.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            updatedMethod = updatedMethod.WithModifiers(updatedMethod.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)));
        }

        var updatedRoot = root.ReplaceNode(method, updatedMethod);
        updatedRoot = EnsureUsings(updatedRoot, "System.Collections.Generic", "System.Linq");
        if (asyncVersion)
        {
            updatedRoot = EnsureUsings(updatedRoot, "System.Threading.Tasks", "Microsoft.EntityFrameworkCore");
        }

        return document.WithSyntaxRoot(updatedRoot);
    }

    private static InvocationExpressionSyntax RenameInvocation(InvocationExpressionSyntax invocation, string newName)
    {
        return invocation.WithExpression(
            invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(SyntaxFactory.IdentifierName(newName)),
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
        var genericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("SelectExpr"),
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SeparatedList<TypeSyntax>(
                    new[]
                    {
                        SyntaxFactory.ParseTypeName(sourceType),
                        SyntaxFactory.ParseTypeName(dtoName),
                    }
                )
            )
        );

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
        var receiver = invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;
        var receiverType = receiver is null ? null : semanticModel.GetTypeInfo(receiver, cancellationToken).Type;
        if (receiverType is INamedTypeSymbol namedType)
        {
            var element = namedType.TypeArguments.FirstOrDefault()
                ?? namedType.AllInterfaces.FirstOrDefault(interfaceType => interfaceType.TypeArguments.Length == 1)?.TypeArguments[0];
            if (element is not null)
            {
                return element.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }
        }

        return "TSource";
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

        return namedType.AllInterfaces
            .FirstOrDefault(interfaceType => interfaceType.TypeArguments.Length == 1)
            ?.TypeArguments[0]
            .Name;
    }

    private static ExpressionSyntax ConvertObjectCreationToAnonymous(ObjectCreationExpressionSyntax objectCreation, bool simplifyTernary)
    {
        var members = objectCreation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Select(
                assignment =>
                {
                    var memberName = assignment.Left.ToString();
                    var value = RewriteForAnonymous(assignment.Right, simplifyTernary);
                    return $"{memberName} = {value}";
                }
            )
            ?? Enumerable.Empty<string>();

        return SyntaxFactory.ParseExpression($"new {{ {string.Join(", ", members)} }}");
    }

    private static string RewriteForAnonymous(ExpressionSyntax expression, bool simplifyTernary)
    {
        if (expression is ObjectCreationExpressionSyntax nestedObject)
        {
            return ConvertObjectCreationToAnonymous(nestedObject, simplifyTernary).ToString();
        }

        if (simplifyTernary && expression is ConditionalExpressionSyntax conditionalExpression)
        {
            var simplified = TrySimplifyConditionalExpression(conditionalExpression);
            if (simplified is not null)
            {
                return simplified.ToString();
            }
        }

        return expression.ToString();
    }

    private static ExpressionSyntax? TrySimplifyConditionalExpression(ConditionalExpressionSyntax conditionalExpression)
    {
        if (!AnalyzerHelpers.ContainsNullCheckedObjectTernary(conditionalExpression))
        {
            return null;
        }

        var checkedPath = ExtractCheckedPath(conditionalExpression.Condition);
        if (string.IsNullOrWhiteSpace(checkedPath))
        {
            return null;
        }

        var objectBranch = conditionalExpression.WhenTrue.IsKind(SyntaxKind.NullLiteralExpression)
            ? conditionalExpression.WhenFalse
            : conditionalExpression.WhenTrue;

        return objectBranch switch
        {
            AnonymousObjectCreationExpressionSyntax anonymousObject => SimplifyAnonymousObject(anonymousObject, checkedPath!),
            ObjectCreationExpressionSyntax objectCreation => SimplifyNamedObject(objectCreation, checkedPath!),
            _ => null,
        };
    }

    private static ExpressionSyntax SimplifyAnonymousObject(AnonymousObjectCreationExpressionSyntax anonymousObject, string checkedPath)
    {
        var members = anonymousObject.Initializers.Select(
            initializer =>
            {
                var memberName = AnalyzerHelpers.GetAnonymousMemberName(initializer);
                var value = ApplyNullConditional(initializer.Expression.ToString(), checkedPath);
                return $"{memberName} = {value}";
            }
        );
        return SyntaxFactory.ParseExpression($"new {{ {string.Join(", ", members)} }}");
    }

    private static ExpressionSyntax SimplifyNamedObject(ObjectCreationExpressionSyntax objectCreation, string checkedPath)
    {
        var members = objectCreation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Select(assignment => $"{assignment.Left} = {ApplyNullConditional(assignment.Right.ToString(), checkedPath)}")
            ?? Enumerable.Empty<string>();
        return SyntaxFactory.ParseExpression($"new {objectCreation.Type} {{ {string.Join(", ", members)} }}");
    }

    private static string? ExtractCheckedPath(ExpressionSyntax condition)
    {
        if (condition is BinaryExpressionSyntax binaryExpression
            && binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)
            && binaryExpression.Right.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return binaryExpression.Left.ToString();
        }

        if (condition is BinaryExpressionSyntax logicalAnd
            && logicalAnd.IsKind(SyntaxKind.LogicalAndExpression))
        {
            return ExtractCheckedPath(logicalAnd.Left);
        }

        return null;
    }

    private static string ApplyNullConditional(string expressionText, string checkedPath)
    {
        var needle = checkedPath + ".";
        var replacement = checkedPath + "?.";
        var index = expressionText.IndexOf(needle, StringComparison.Ordinal);
        if (index < 0)
        {
            return expressionText;
        }

        return expressionText.Remove(index, needle.Length).Insert(index, replacement);
    }

    private static ObjectCreationExpressionSyntax CreateObjectCreationFromAnonymous(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string dtoName
    )
    {
        var members = anonymousObject.Initializers.Select(
            initializer => $"{AnalyzerHelpers.GetAnonymousMemberName(initializer)} = {initializer.Expression}"
        );
        return (ObjectCreationExpressionSyntax)SyntaxFactory.ParseExpression(
            $"new {dtoName} {{ {string.Join(", ", members)} }}"
        );
    }

    private static string CreateDtoClassText(string dtoName, AnonymousObjectCreationExpressionSyntax anonymousObject)
    {
        var members = string.Join(
            "\r\n",
            anonymousObject.Initializers.Select(
            initializer =>
            {
                var typeName = initializer.Expression is LiteralExpressionSyntax literalExpression && literalExpression.IsKind(SyntaxKind.StringLiteralExpression)
                    ? "string"
                    : "object";
                return $"    public {typeName} {AnalyzerHelpers.GetAnonymousMemberName(initializer)} {{ get; set; }}";
            }
        )
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
            compilationUnit.Usings
                .Select(usingDirective => usingDirective.Name?.ToString())
                .OfType<string>(),
            StringComparer.Ordinal
        );
        var missingUsings = namespaces
            .Where(namespaceName => !string.IsNullOrWhiteSpace(namespaceName) && !existing.Contains(namespaceName))
            .Select(namespaceName => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName)))
            .ToArray();

        return missingUsings.Length == 0
            ? compilationUnit
            : compilationUnit.WithUsings(compilationUnit.Usings.AddRange(missingUsings));
    }
}
