using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts SelectExpr to typed SelectExpr with type arguments
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SelectExprToTypedCodeFixProvider))]
[Shared]
public class SelectExprToTypedCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SelectExprToTypedAnalyzer.DiagnosticId);

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

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to typed SelectExpr",
                createChangedDocument: c =>
                    ConvertToTypedSelectExprAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToTypedSelectExpr"
            ),
            diagnostic
        );
    }

    private static async Task<Document> ConvertToTypedSelectExprAsync(
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

        // Find the anonymous type in arguments
        var anonymousType = FindAnonymousTypeInArguments(invocation.ArgumentList);
        if (anonymousType == null)
            return document;

        // Generate DTO name
        var dtoName = GenerateDtoName(invocation, anonymousType);

        // Create the new generic name with type arguments
        var newExpression = CreateTypedSelectExpr(
            invocation.Expression,
            sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            dtoName
        );
        if (newExpression == null)
            return document;

        // Replace the expression
        var newInvocation = invocation.WithExpression(newExpression);
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
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

        // Create the new member access expression
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
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

        // Extract the element type from IQueryable<T> or IEnumerable<T>
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
        string baseName;

        // Walk up the tree to find the relevant context
        var current = invocation.Parent;
        while (current != null)
        {
            switch (current)
            {
                // Check for variable declaration: var name = query.SelectExpr(...)
                case EqualsValueClauseSyntax equalsValue
                    when equalsValue.Parent is VariableDeclaratorSyntax declarator:
                    var varName = declarator.Identifier.Text;
                    baseName = ToPascalCase(varName) + "Dto";
                    return baseName + "_" + GenerateHash(anonymousType);

                // Check for assignment: name = query.SelectExpr(...)
                case AssignmentExpressionSyntax assignment
                    when assignment.Left is IdentifierNameSyntax identifier:
                    baseName = ToPascalCase(identifier.Identifier.Text) + "Dto";
                    return baseName + "_" + GenerateHash(anonymousType);

                // Stop at statement level
                case StatementSyntax:
                    break;

                default:
                    current = current.Parent;
                    continue;
            }
            break;
        }

        // Check for return statement with method name
        var methodDecl = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl != null)
        {
            var methodName = methodDecl.Identifier.Text;
            if (methodName.StartsWith("Get"))
            {
                methodName = methodName.Substring(3);
            }
            baseName = methodName + "Dto";
            return baseName + "_" + GenerateHash(anonymousType);
        }

        return "ResultDto_" + GenerateHash(anonymousType);
    }

    private static string GenerateHash(AnonymousObjectCreationExpressionSyntax anonymousType)
    {
        // Generate hash based on property names
        var sb = new StringBuilder();
        foreach (var initializer in anonymousType.Initializers)
        {
            string propertyName;
            if (initializer.NameEquals != null)
            {
                propertyName = initializer.NameEquals.Name.Identifier.Text;
            }
            else
            {
                propertyName = GetPropertyNameFromExpression(initializer.Expression);
            }
            sb.Append(propertyName);
            sb.Append(';');
        }

        // Create a deterministic hash using FNV-1a algorithm
        var str = sb.ToString();
        uint hash = 2166136261;
        foreach (char c in str)
        {
            hash ^= c;
            hash *= 16777619;
        }

        var hashString = new StringBuilder(8);

        // Convert to uppercase letters and digits (A-Z, 0-9)
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        for (int i = 0; i < 8; i++)
        {
            hashString.Append(chars[(int)(hash % chars.Length)]);
            hash /= (uint)chars.Length;
        }

        return hashString.ToString();
    }

    private static string GetPropertyNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding =>
                memberBinding.Name.Identifier.Text,
            _ => "Property",
        };
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
