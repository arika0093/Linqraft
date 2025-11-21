using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
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
        ImmutableArray.Create(SelectToSelectExprNamedAnalyzer.DiagnosticId);

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

        // Replace both nodes in one operation using a dictionary
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>
        {
            { invocation.Expression, newExpression },
            { objectCreation, anonymousCreation },
        };

        var newRoot = root.ReplaceNodes(replacements.Keys, (oldNode, _) => replacements[oldNode]);

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
        var anonymousCreation = ConvertToAnonymousType(objectCreation);

        // Replace both nodes in one operation using a dictionary
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>
        {
            { invocation.Expression, newExpression },
            { objectCreation, anonymousCreation },
        };

        var newRoot = root.ReplaceNodes(replacements.Keys, (oldNode, _) => replacements[oldNode]);

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

        // Replace the invocation
        var newInvocation = invocation.WithExpression(newExpression);
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
                    members.Add(
                        SyntaxFactory.AnonymousObjectMemberDeclarator(
                            SyntaxFactory.NameEquals(identifier.Identifier.Text),
                            assignment.Right
                        )
                    );
                }
            }
        }

        return SyntaxFactory.AnonymousObjectCreationExpression(
            SyntaxFactory.SeparatedList(members)
        );
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
            // Convert this object creation to anonymous type (non-recursively to avoid infinite loop)
            var anonymousType = ConvertToAnonymousType(node);

            // Continue visiting children to convert nested object creations
            return base.Visit(anonymousType) ?? anonymousType;
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
}
