using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Linqraft.Core.AnalyzerHelpers;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts void methods to synchronous API response methods
/// </summary>
[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(SyncApiResponseMethodGeneratorCodeFixProvider)
)]
[Shared]
public class SyncApiResponseMethodGeneratorCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        [SyncApiResponseMethodGeneratorAnalyzer.AnalyzerId];

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var methodDeclaration = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .First();

        if (methodDeclaration == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to synchronous API response method",
                createChangedDocument: c =>
                    ConvertToSyncApiResponseMethodAsync(context.Document, methodDeclaration, c),
                equivalenceKey: "ConvertToSyncApiResponseMethod"
            ),
            diagnostic
        );
    }

    private static async Task<Document> ConvertToSyncApiResponseMethodAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        // Find the Select invocation
        var selectInvocation = FindUnassignedSelectWithAnonymousType(methodDeclaration);
        if (selectInvocation == null)
            return document;

        // Get the source type from the Select
        var sourceType = GetSourceType(
            selectInvocation.Expression,
            semanticModel,
            cancellationToken
        );
        if (sourceType == null)
            return document;

        // Find the anonymous type
        var anonymousType = FindAnonymousTypeInArguments(selectInvocation.ArgumentList);
        if (anonymousType == null)
            return document;

        // Generate DTO name
        var dtoName = GenerateDtoName(selectInvocation, anonymousType);

        // Step 1: Replace Select with SelectExpr<TSource, TDto>
        var newSelectExpression = CreateTypedSelectExpr(
            selectInvocation.Expression,
            sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            dtoName
        );
        if (newSelectExpression == null)
            return document;

        // Simplify ternary null checks
        var newSelectInvocation = TernaryNullCheckSimplifier.SimplifyTernaryNullChecksInInvocation(
            selectInvocation.WithExpression(newSelectExpression)
        );

        // Find variables that need to be captured
        var variablesToCapture = FindVariablesToCapture(selectInvocation, semanticModel);
        if (variablesToCapture.Count > 0)
        {
            newSelectInvocation = AddCaptureArgument(newSelectInvocation, variablesToCapture);
        }

        // Step 2: Add ToList() call (not ToListAsync)
        var toListInvocation = CreateToListInvocation(newSelectInvocation);

        // Step 3: Wrap with return statement (with proper spacing)
        var returnStatement = SyntaxFactory.ReturnStatement(
            SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            toListInvocation,
            SyntaxFactory.Token(SyntaxKind.SemicolonToken)
        );

        // Step 4: Find and replace the expression statement
        var expressionStatement = FindExpressionStatement(selectInvocation);
        if (expressionStatement == null)
            return document;

        // Preserve trivia from the original statement
        returnStatement = returnStatement.WithTriviaFrom(expressionStatement);

        // Track the method declaration to update it after statement replacement
        var newRoot = root.TrackNodes(methodDeclaration, expressionStatement);

        var currentExpressionStatement = newRoot.GetCurrentNode(expressionStatement);
        if (currentExpressionStatement != null)
        {
            newRoot = newRoot.ReplaceNode(currentExpressionStatement, returnStatement);
        }

        // Get the tracked method declaration after the replacement
        var currentMethodDeclaration = newRoot.GetCurrentNode(methodDeclaration);
        if (currentMethodDeclaration == null)
            return document;

        // Step 5: Update method signature (no async)
        var updatedMethod = UpdateMethodSignature(currentMethodDeclaration, dtoName);

        newRoot = newRoot.ReplaceNode(currentMethodDeclaration, updatedMethod);

        // Add using directives if needed
        newRoot = UsingDirectiveHelper.AddUsingDirectiveForType(newRoot, sourceType);
        newRoot = AddUsingDirectiveIfNeeded(newRoot, "System.Linq");
        newRoot = AddUsingDirectiveIfNeeded(newRoot, "System.Collections.Generic");

        var documentWithNewRoot = document.WithSyntaxRoot(newRoot);

        // Format and normalize line endings
        return await CodeFixFormattingHelper
            .FormatAndNormalizeLineEndingsAsync(documentWithNewRoot, cancellationToken)
            .ConfigureAwait(false);
    }

    private static MethodDeclarationSyntax UpdateMethodSignature(
        MethodDeclarationSyntax methodDeclaration,
        string dtoName
    )
    {
        var newMethod = methodDeclaration;

        // Change return type to List<TDto> (not Task<List<TDto>>)
        var newReturnType = SyntaxFactory.ParseTypeName($"List<{dtoName}>");
        newMethod = newMethod.WithReturnType(newReturnType);

        return newMethod;
    }

    private static InvocationExpressionSyntax CreateToListInvocation(
        InvocationExpressionSyntax selectInvocation
    )
    {
        // Create .ToList() call (not ToListAsync)
        var memberAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            selectInvocation,
            SyntaxFactory.IdentifierName("ToList")
        );

        return SyntaxFactory.InvocationExpression(memberAccess, SyntaxFactory.ArgumentList());
    }

    private static ExpressionStatementSyntax? FindExpressionStatement(
        InvocationExpressionSyntax invocation
    )
    {
        return invocation.Parent as ExpressionStatementSyntax;
    }

    private static InvocationExpressionSyntax? FindUnassignedSelectWithAnonymousType(
        MethodDeclarationSyntax methodDeclaration
    )
    {
        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
        {
            return null;
        }

        var invocations = methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            if (!IsSelectInvocation(invocation.Expression))
            {
                continue;
            }

            var anonymousType = FindAnonymousTypeInArguments(invocation.ArgumentList);
            if (anonymousType == null)
            {
                continue;
            }

            if (IsUnassignedInvocation(invocation))
            {
                return invocation;
            }
        }

        return null;
    }

    private static bool IsUnassignedInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Parent is ExpressionStatementSyntax;
    }

    private static bool IsSelectInvocation(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text
                == "Select",
            IdentifierNameSyntax identifier => identifier.Identifier.Text == "Select",
            _ => false,
        };
    }

    private static ExpressionSyntax? CreateTypedSelectExpr(
        ExpressionSyntax expression,
        string sourceTypeName,
        string dtoName
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var typeArguments = SyntaxFactory.TypeArgumentList(
            SyntaxFactory.SeparatedList<TypeSyntax>(
                new[]
                {
                    SyntaxFactory.ParseTypeName(sourceTypeName),
                    SyntaxFactory.ParseTypeName(dtoName),
                }
            )
        );

        var genericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("SelectExpr"),
            typeArguments
        );

        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.OperatorToken,
            genericName
        );
    }

    private static ITypeSymbol? GetSourceType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
        var type = typeInfo.Type;

        if (type == null)
            return null;

        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var typeArguments = namedType.TypeArguments;
            if (typeArguments.Length > 0)
            {
                return typeArguments[0];
            }
        }

        return null;
    }

    private static AnonymousObjectCreationExpressionSyntax? FindAnonymousTypeInArguments(
        ArgumentListSyntax argumentList
    )
    {
        foreach (var argument in argumentList.Arguments)
        {
            var lambda = argument.Expression switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Body,
                ParenthesizedLambdaExpressionSyntax paren => paren.Body,
                _ => null,
            };

            if (lambda is AnonymousObjectCreationExpressionSyntax anonymousObject)
            {
                return anonymousObject;
            }
        }

        return null;
    }

    private static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousType
    )
    {
        return DtoNamingHelper.GenerateDtoName(invocation, anonymousType);
    }

    private static HashSet<string> FindVariablesToCapture(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        var lambda = FindLambdaExpression(invocation.ArgumentList);
        if (lambda == null)
            return [];

        var lambdaParameters = LambdaHelper.GetLambdaParameterNames(lambda);
        var variablesToCapture = CaptureHelper.FindSimpleVariablesToCapture(
            lambda,
            lambdaParameters,
            semanticModel
        );

        var capturedVariables = CaptureHelper.GetCapturedVariables(invocation, semanticModel);
        capturedVariables.UnionWith(variablesToCapture);

        return capturedVariables;
    }

    private static InvocationExpressionSyntax AddCaptureArgument(
        InvocationExpressionSyntax invocation,
        HashSet<string> variablesToCapture
    )
    {
        var captureArgument = CaptureHelper.CreateCaptureArgument(variablesToCapture);

        var existingCaptureArgIndex = FindCaptureArgumentIndex(invocation);
        if (existingCaptureArgIndex >= 0)
        {
            var newArgumentList = ArgumentListHelper.ReplaceArgument(
                invocation.ArgumentList,
                existingCaptureArgIndex,
                captureArgument
            );
            return invocation.WithArgumentList(newArgumentList);
        }
        else
        {
            var newArgumentList = ArgumentListHelper.AddArgument(
                invocation.ArgumentList,
                captureArgument
            );
            return invocation.WithArgumentList(newArgumentList);
        }
    }

    private static int FindCaptureArgumentIndex(InvocationExpressionSyntax invocation)
    {
        for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var argument = invocation.ArgumentList.Arguments[i];
            if (argument.NameColon?.Name.Identifier.Text == "capture")
            {
                return i;
            }
        }

        return -1;
    }

    private static LambdaExpressionSyntax? FindLambdaExpression(ArgumentListSyntax argumentList)
    {
        foreach (var argument in argumentList.Arguments)
        {
            if (argument.Expression is LambdaExpressionSyntax lambda)
            {
                return lambda;
            }
        }

        return null;
    }

    private static SyntaxNode AddUsingDirectiveIfNeeded(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        // Check if using directive already exists
        var hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
        if (hasUsing)
            return root;

        // Add using directive with proper trivia
        var usingDirective = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithUsingKeyword(
                SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space)
            )
            .WithTrailingTrivia(TriviaHelper.EndOfLine(root));

        return compilationUnit.AddUsings(usingDirective);
    }
}
