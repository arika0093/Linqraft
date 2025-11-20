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

        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        // Find the SelectExpr invocation to get the DTO type and check if it returns a collection
        var selectExprInfo = FindSelectExprInfo(methodDeclaration, semanticModel);
        if (selectExprInfo == null)
            return document;

        // Create the ProducesResponseType attribute
        var attribute = CreateProducesResponseTypeAttribute(selectExprInfo.Value.ResponseTypeName);

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

    private static SelectExprInfo? FindSelectExprInfo(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel
    )
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
                    var dtoTypeName = genericName.TypeArgumentList.Arguments[1].ToString();

                    // Find the outermost expression containing SelectExpr to determine the final return type
                    var expressionToAnalyze = FindOutermostExpression(invocation);

                    // Get the type of the expression using semantic model
                    var typeInfo = semanticModel.GetTypeInfo(expressionToAnalyze);
                    var resultType = typeInfo.Type;

                    // Determine the response type to use
                    var responseType = DetermineResponseType(resultType, dtoTypeName);

                    return new SelectExprInfo
                    {
                        DtoTypeName = dtoTypeName,
                        ResponseTypeName = responseType,
                    };
                }
            }
        }

        return null;
    }

    private static string DetermineResponseType(ITypeSymbol? type, string dtoTypeName)
    {
        if (type == null)
            return $"List<{dtoTypeName}>"; // Default to List if we can't determine the type

        // Check if it's an array
        if (type is IArrayTypeSymbol)
            return $"{dtoTypeName}[]";

        // Check if it's a collection type
        if (IsCollectionType(type))
        {
            // For collection types, return List<TDto>
            return $"List<{dtoTypeName}>";
        }

        // For single result types (like FirstOrDefault), return just the DTO type
        return dtoTypeName;
    }

    private static ExpressionSyntax FindOutermostExpression(InvocationExpressionSyntax selectExprInvocation)
    {
        // Traverse up the syntax tree to find the outermost expression
        // This handles cases like: query.SelectExpr(...).ToList(), query.SelectExpr(...).FirstOrDefault(), etc.
        SyntaxNode current = selectExprInvocation;
        SyntaxNode? lastExpression = selectExprInvocation;

        while (current.Parent != null)
        {
            var parent = current.Parent;

            // Keep going up if we're still in a method chain or invocation
            if (
                parent is MemberAccessExpressionSyntax
                || parent is InvocationExpressionSyntax
                || parent is ArgumentSyntax
            )
            {
                if (parent is ExpressionSyntax expr)
                {
                    lastExpression = expr;
                }
                current = parent;
            }
            else
            {
                break;
            }
        }

        return (ExpressionSyntax)lastExpression;
    }

    private static bool IsCollectionType(ITypeSymbol? type)
    {
        if (type == null)
            return true; // Default to collection if we can't determine the type

        // Check if it's an array
        if (type is IArrayTypeSymbol)
            return true;

        // Check if it's a named type (generic or not)
        if (type is INamedTypeSymbol namedType)
        {
            // Get the original definition for generic types
            var originalDefinition = namedType.OriginalDefinition;
            var typeName = originalDefinition.ToDisplayString();

            // Check for common collection types
            var collectionTypes = new[]
            {
                "System.Collections.Generic.IEnumerable<T>",
                "System.Collections.Generic.ICollection<T>",
                "System.Collections.Generic.IList<T>",
                "System.Collections.Generic.List<T>",
                "System.Collections.Generic.IReadOnlyCollection<T>",
                "System.Collections.Generic.IReadOnlyList<T>",
                "System.Linq.IQueryable<T>",
                "System.Linq.IOrderedQueryable<T>",
            };

            if (collectionTypes.Contains(typeName))
                return true;

            // Check if it implements IEnumerable<T>
            foreach (var iface in namedType.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private struct SelectExprInfo
    {
        public string DtoTypeName { get; set; }
        public string ResponseTypeName { get; set; }
    }

    private static AttributeSyntax CreateProducesResponseTypeAttribute(string responseTypeName)
    {
        // Parse the response type name to create the appropriate TypeSyntax
        TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(responseTypeName);

        var typeofExpression = SyntaxFactory.TypeOfExpression(typeSyntax);

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
