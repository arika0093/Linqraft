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

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that adds ProducesResponseType attribute to ApiController methods
/// </summary>
[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(ApiControllerProducesResponseTypeCodeFixProvider)
)]
[Shared]
public class ApiControllerProducesResponseTypeCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ApiControllerProducesResponseTypeAnalyzer.DiagnosticId);

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
                title: "Add [ProducesResponseType]",
                createChangedDocument: c =>
                    AddProducesResponseTypeAsync(context.Document, methodDeclaration, c),
                equivalenceKey: "AddProducesResponseType"
            ),
            diagnostic
        );
    }

    private static async Task<Document> AddProducesResponseTypeAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Find the SelectExpr invocation to get the DTO type
        var dtoTypeName = FindDtoTypeName(methodDeclaration);
        if (dtoTypeName == null)
            return document;

        // Create the ProducesResponseType attribute
        var attribute = CreateProducesResponseTypeAttribute(dtoTypeName);

        // Create the attribute list with proper formatting
        var attributeList = SyntaxFactory
            .AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        // Get existing attributes or create new array
        var attributeLists = methodDeclaration.AttributeLists.Add(attributeList);

        // Create new method with the updated attributes
        var newMethod = methodDeclaration.WithAttributeLists(attributeLists);

        // Replace the old method with the new one
        var newRoot = root.ReplaceNode(methodDeclaration, newMethod);

        // Add using directive if necessary
        newRoot = AddUsingDirectiveIfNeeded(newRoot, "Microsoft.AspNetCore.Mvc");

        return document.WithSyntaxRoot(newRoot);
    }

    private static string? FindDtoTypeName(MethodDeclarationSyntax methodDeclaration)
    {
        // Find SelectExpr with type arguments
        var invocations = methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (
                    memberAccess.Name is GenericNameSyntax genericName
                    && genericName.Identifier.Text == "SelectExpr"
                    && genericName.TypeArgumentList.Arguments.Count >= 2
                )
                {
                    // Get the DTO type name (second type argument)
                    return genericName.TypeArgumentList.Arguments[1].ToString();
                }
            }
        }

        return null;
    }

    private static AttributeSyntax CreateProducesResponseTypeAttribute(string dtoTypeName)
    {
        // Create: [ProducesResponseType(typeof(List<TDto>), 200)]
        var typeofExpression = SyntaxFactory.TypeOfExpression(
            SyntaxFactory.GenericName(
                SyntaxFactory.Identifier("List"),
                SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                        SyntaxFactory.IdentifierName(dtoTypeName)
                    )
                )
            )
        );

        var arguments = SyntaxFactory.AttributeArgumentList(
            SyntaxFactory.SeparatedList(
                new[]
                {
                    SyntaxFactory.AttributeArgument(typeofExpression),
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            SyntaxFactory.Literal(200)
                        )
                    ),
                }
            )
        );

        return SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("ProducesResponseType"),
            arguments
        );
    }

    private static SyntaxNode AddUsingDirectiveIfNeeded(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        // Check if using directive already exists
        var hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
        if (hasUsing)
            return root;

        // Add using directive
        var usingDirective = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        return compilationUnit.AddUsings(usingDirective);
    }
}
