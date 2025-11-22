using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Linqraft.Core.Formatting;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts IQueryable.Select with named types to SelectExpr
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SelectToSelectExprNamedCodeFixProvider))]
[Shared]
public class SelectToSelectExprNamedCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SelectToSelectExprNamedAnalyzer.AnalyzerId);

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

        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .First();

        if (invocation == null)
            return;

        // Register three code fixes
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr<T, TDto> (convert all to anonymous)",
                createChangedDocument: c =>
                    ConvertToSelectExprExplicitDtoAllAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprExplicitDtoAll"
            ),
            diagnostic
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr<T, TDto> (convert root only to anonymous)",
                createChangedDocument: c =>
                    ConvertToSelectExprExplicitDtoRootOnlyAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprExplicitDtoRootOnly"
            ),
            diagnostic
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr (use predefined classes)",
                createChangedDocument: c =>
                    ConvertToSelectExprPredefinedDtoAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprPredefinedDto"
            ),
            diagnostic
        );
    }

    private static async Task<Document> ConvertToSelectExprExplicitDtoAllAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Get the source type
        var sourceType = GetSourceType(invocation.Expression, semanticModel, cancellationToken);
        if (sourceType == null)
            return document;

        // Find the object creation in arguments
        var objectCreation = FindNamedObjectCreationInArguments(invocation.ArgumentList);
        if (objectCreation == null)
            return document;

        // Generate DTO name from the object creation
        var dtoName = GenerateDtoName(invocation, objectCreation);

        // Create the new generic SelectExpr call with type arguments
        var newExpression = CreateTypedSelectExpr(
            invocation.Expression,
            sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            dtoName
        );
        if (newExpression == null)
            return document;

        // Convert the named object creation to anonymous type (including nested)
        var anonymousCreation = ConvertToAnonymousTypeRecursive(objectCreation);

        // Simplify ternary null checks in the anonymous creation
        anonymousCreation = (AnonymousObjectCreationExpressionSyntax)
            TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(anonymousCreation);

        // Replace both nodes in one operation using a dictionary
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>
        {
            { invocation.Expression, newExpression },
            { objectCreation, anonymousCreation },
        };

        var newRoot = root.ReplaceNodes(replacements.Keys, (oldNode, _) => replacements[oldNode]);

        // Add using directive for source type if needed
        newRoot = AddUsingDirectiveForType(newRoot, sourceType);

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> ConvertToSelectExprExplicitDtoRootOnlyAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Get the source type
        var sourceType = GetSourceType(invocation.Expression, semanticModel, cancellationToken);
        if (sourceType == null)
            return document;

        // Find the object creation in arguments
        var objectCreation = FindNamedObjectCreationInArguments(invocation.ArgumentList);
        if (objectCreation == null)
            return document;

        // Generate DTO name from the object creation
        var dtoName = GenerateDtoName(invocation, objectCreation);

        // Create the new generic SelectExpr call with type arguments
        var newExpression = CreateTypedSelectExpr(
            invocation.Expression,
            sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            dtoName
        );
        if (newExpression == null)
            return document;

        // Convert the named object creation to anonymous type (root only)
        var anonymousCreation = ConvertToAnonymousType(objectCreation, semanticModel);

        // Simplify ternary null checks in the anonymous creation
        anonymousCreation = (AnonymousObjectCreationExpressionSyntax)
            TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(anonymousCreation);

        // Replace both nodes in one operation using a dictionary
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>
        {
            { invocation.Expression, newExpression },
            { objectCreation, anonymousCreation },
        };

        var newRoot = root.ReplaceNodes(replacements.Keys, (oldNode, _) => replacements[oldNode]);

        // Add using directive for source type if needed
        newRoot = AddUsingDirectiveForType(newRoot, sourceType);

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> ConvertToSelectExprPredefinedDtoAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Replace "Select" with "SelectExpr"
        var newExpression = ReplaceMethodName(invocation.Expression, "SelectExpr");
        if (newExpression == null)
            return document;

        // Simplify ternary null checks in the lambda body
        var newInvocation = SimplifyTernaryNullChecksInInvocation(
            invocation.WithExpression(newExpression)
        );

        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }

    private static ExpressionSyntax? ReplaceMethodName(
        ExpressionSyntax expression,
        string newMethodName
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Create the new identifier
        var newName = SyntaxFactory.IdentifierName(newMethodName);

        // Create the new member access expression
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.OperatorToken,
            newName
        );
    }

    private static ExpressionSyntax? CreateTypedSelectExpr(
        ExpressionSyntax expression,
        string sourceTypeName,
        string dtoName
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Create type arguments
        var typeArguments = SyntaxFactory.TypeArgumentList(
            SyntaxFactory.SeparatedList<TypeSyntax>(
                new[]
                {
                    SyntaxFactory.ParseTypeName(sourceTypeName),
                    SyntaxFactory.ParseTypeName(dtoName),
                }
            )
        );

        // Create the generic name
        var genericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("SelectExpr"),
            typeArguments
        );

        // Create the new member access expression, preserving the original dot token's trivia
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

        // Extract the element type from IQueryable<T>
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

    private static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousType(
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        return ConvertToAnonymousType(objectCreation, null);
    }

    private static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousType(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel? semanticModel
    )
    {
        // Convert object initializer to anonymous object creation
        if (objectCreation.Initializer == null)
        {
            return SyntaxFactory.AnonymousObjectCreationExpression();
        }

        var members = new List<AnonymousObjectMemberDeclaratorSyntax>();

        foreach (var expression in objectCreation.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment)
            {
                // Convert assignment like "Id = x.Id" to anonymous member
                if (assignment.Left is IdentifierNameSyntax identifier)
                {
                    // Use the right side expression as-is (no FullName conversion needed)
                    var processedRight = assignment.Right;

                    // Create the name equals with preserved identifier trivia
                    var nameEquals = SyntaxFactory
                        .NameEquals(SyntaxFactory.IdentifierName(identifier.Identifier))
                        .WithLeadingTrivia(assignment.GetLeadingTrivia());

                    // Create the member with preserved trivia from the right side
                    var member = SyntaxFactory
                        .AnonymousObjectMemberDeclarator(nameEquals, processedRight)
                        .WithTrailingTrivia(assignment.GetTrailingTrivia());

                    members.Add(member);
                }
            }
        }

        // Create the separated list preserving separators from the original initializer
        var separatedMembers = SyntaxFactory.SeparatedList(
            members,
            objectCreation.Initializer.Expressions.GetSeparators()
        );

        // Collect all trivia between "new" and the opening brace
        // This includes trivia from the type node (both leading and trailing)
        var triviaBeforeOpenBrace = SyntaxTriviaList.Empty;
        if (objectCreation.Type != null)
        {
            triviaBeforeOpenBrace = triviaBeforeOpenBrace
                .AddRange(objectCreation.Type.GetLeadingTrivia())
                .AddRange(objectCreation.Type.GetTrailingTrivia());
        }

        // Create anonymous object creation preserving trivia
        var result = SyntaxFactory
            .AnonymousObjectCreationExpression(
                SyntaxFactory
                    .Token(SyntaxKind.NewKeyword)
                    .WithLeadingTrivia(objectCreation.GetLeadingTrivia())
                    .WithTrailingTrivia(triviaBeforeOpenBrace),
                SyntaxFactory
                    .Token(SyntaxKind.OpenBraceToken)
                    .WithTrailingTrivia(objectCreation.Initializer.OpenBraceToken.TrailingTrivia),
                separatedMembers,
                SyntaxFactory
                    .Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(objectCreation.Initializer.CloseBraceToken.LeadingTrivia)
                    .WithTrailingTrivia(objectCreation.Initializer.CloseBraceToken.TrailingTrivia)
            )
            .WithTrailingTrivia(objectCreation.GetTrailingTrivia());

        return result;
    }

    private static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousTypeRecursive(
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        // Convert object initializer to anonymous object creation, recursively converting nested object creations
        if (objectCreation.Initializer == null)
        {
            return SyntaxFactory.AnonymousObjectCreationExpression();
        }

        var members = new List<AnonymousObjectMemberDeclaratorSyntax>();

        foreach (var expression in objectCreation.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment)
            {
                // Convert assignment like "Id = x.Id" to anonymous member
                if (assignment.Left is IdentifierNameSyntax identifier)
                {
                    // Recursively process the right side to convert nested object creations
                    var processedRight = ProcessExpressionForNestedConversions(assignment.Right);

                    members.Add(
                        SyntaxFactory.AnonymousObjectMemberDeclarator(
                            SyntaxFactory.NameEquals(identifier.Identifier.Text),
                            processedRight
                        )
                    );
                }
            }
        }

        return SyntaxFactory.AnonymousObjectCreationExpression(
            SyntaxFactory.SeparatedList(members)
        );
    }

    private static ExpressionSyntax ProcessExpressionForNestedConversions(
        ExpressionSyntax expression
    )
    {
        // Use a recursive rewriter to find and convert all nested object creations
        var rewriter = new NestedObjectCreationRewriter();
        return (ExpressionSyntax)rewriter.Visit(expression);
    }

    /// <summary>
    /// Syntax rewriter that converts ObjectCreationExpressionSyntax to AnonymousObjectCreationExpressionSyntax
    /// </summary>
    private class NestedObjectCreationRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitObjectCreationExpression(
            ObjectCreationExpressionSyntax node
        )
        {
            // Only convert if it has an initializer with assignment expressions
            // Don't convert things like "new List()" or "new List<T>()"
            if (node.Initializer != null && HasAssignmentExpressions(node.Initializer))
            {
                // Convert this object creation to anonymous type (non-recursively to avoid infinite loop)
                var anonymousType = ConvertToAnonymousType(node);

                // Continue visiting children to convert nested object creations
                return base.Visit(anonymousType) ?? anonymousType;
            }

            // For object creations without proper initializers, don't convert but still visit children
            return base.VisitObjectCreationExpression(node);
        }

        private static bool HasAssignmentExpressions(InitializerExpressionSyntax initializer)
        {
            return initializer.Expressions.Any(e => e is AssignmentExpressionSyntax);
        }
    }

    private static ObjectCreationExpressionSyntax? FindNamedObjectCreationInArguments(
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

            if (lambda is ObjectCreationExpressionSyntax objectCreation)
            {
                return objectCreation;
            }
        }

        return null;
    }

    private static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        // For explicit DTO pattern, generate a DTO name similar to anonymous types
        // We'll use the existing naming logic but pass the initializer as if it were anonymous

        // If there's an initializer, convert it to represent the properties
        if (objectCreation.Initializer != null)
        {
            // Create a synthetic anonymous object for naming purposes
            var anonymousSyntax = SyntaxFactory.AnonymousObjectCreationExpression(
                SyntaxFactory.SeparatedList(
                    objectCreation
                        .Initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                        .Where(assignment => assignment.Left is IdentifierNameSyntax)
                        .Select(assignment =>
                            SyntaxFactory.AnonymousObjectMemberDeclarator(
                                SyntaxFactory.NameEquals(
                                    ((IdentifierNameSyntax)assignment.Left).Identifier.Text
                                ),
                                assignment.Right
                            )
                        )
                )
            );

            return DtoNamingHelper.GenerateDtoName(invocation, anonymousSyntax);
        }

        // Fallback to a default name
        return "ResultDto";
    }

    private static InvocationExpressionSyntax SimplifyTernaryNullChecksInInvocation(
        InvocationExpressionSyntax invocation
    )
    {
        // Find and simplify ternary null checks in lambda body
        var newArguments = new List<ArgumentSyntax>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (
                argument.Expression is SimpleLambdaExpressionSyntax simpleLambda
                && simpleLambda.Body is ExpressionSyntax bodyExpr
            )
            {
                var simplifiedBody = TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(bodyExpr);
                var newLambda = simpleLambda.WithBody(simplifiedBody);
                newArguments.Add(argument.WithExpression(newLambda));
            }
            else if (
                argument.Expression is ParenthesizedLambdaExpressionSyntax parenLambda
                && parenLambda.Body is ExpressionSyntax parenBodyExpr
            )
            {
                var simplifiedBody = TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(
                    parenBodyExpr
                );
                var newLambda = parenLambda.WithBody(simplifiedBody);
                newArguments.Add(argument.WithExpression(newLambda));
            }
            else
            {
                newArguments.Add(argument);
            }
        }

        return invocation.WithArgumentList(
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(newArguments))
        );
    }

    private static SyntaxNode AddUsingDirectiveForType(SyntaxNode root, ITypeSymbol typeSymbol)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        // Get the namespace of the type
        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrEmpty(namespaceName) || namespaceName == "<global namespace>")
            return root;

        // Check if using directive already exists
        var hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
        if (hasUsing)
            return root;

        // Detect the line ending used in the file by looking at existing using directives
        var endOfLineTrivia = compilationUnit.Usings.Any()
            ? compilationUnit.Usings.Last().GetTrailingTrivia().LastOrDefault()
            : TriviaHelper.EndOfLine();

        // If the detected trivia is not an end of line, use a default
        if (!endOfLineTrivia.IsKind(SyntaxKind.EndOfLineTrivia))
        {
            endOfLineTrivia = TriviaHelper.EndOfLine();
        }

        // Add using directive (namespaceName is guaranteed non-null here due to the check above)
        var usingDirective = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(namespaceName!))
            .WithTrailingTrivia(endOfLineTrivia);

        return compilationUnit.AddUsings(usingDirective);
    }
}
