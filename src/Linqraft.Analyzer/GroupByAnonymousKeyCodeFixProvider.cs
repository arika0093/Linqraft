using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts anonymous type keys in GroupBy to named DTO classes.
/// This is required when using SelectExpr after GroupBy, as the generated code cannot
/// handle anonymous types in the input type parameter.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GroupByAnonymousKeyCodeFixProvider))]
[Shared]
public class GroupByAnonymousKeyCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        [GroupByAnonymousKeyAnalyzer.AnalyzerId];

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

        var anonymousObject = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .First();

        if (anonymousObject == null)
            return;

        // Register code fix to convert anonymous type to DTO
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert GroupBy key to named class",
                createChangedDocument: c =>
                    ConvertGroupByKeyToDtoAsync(context.Document, anonymousObject, c),
                equivalenceKey: "ConvertGroupByKeyToDto"
            ),
            diagnostic
        );
    }

    private static Task<Document> ConvertGroupByKeyToDtoAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        CancellationToken cancellationToken
    )
    {
        // Generate DTO class name based on context (GroupBy key)
        var dtoClassName = GenerateGroupByKeyClassName(anonymousObject);

        // Use the shared helper to convert the anonymous type to a DTO
        return AnonymousToDtoCodeFixHelper.ConvertToDtoInSameFileAsync(
            document,
            anonymousObject,
            dtoClassName,
            cancellationToken
        );
    }

    private static string GenerateGroupByKeyClassName(
        AnonymousObjectCreationExpressionSyntax anonymousObject
    )
    {
        // Try to find context from the GroupBy method chain
        var current = anonymousObject.Parent;
        while (current != null)
        {
            // Look for method invocation to get context
            if (current is InvocationExpressionSyntax invocation)
            {
                // Check if this is a GroupBy call
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    if (memberAccess.Name.Identifier.Text == "GroupBy")
                    {
                        // Try to get the source entity type name
                        var sourceExpression = memberAccess.Expression;
                        var sourceName = GetSourceTypeName(sourceExpression);
                        if (!string.IsNullOrEmpty(sourceName))
                        {
                            return $"{sourceName}GroupKey";
                        }
                    }
                }
            }

            current = current.Parent;
        }

        // Fallback to generic name based on property names
        var propertyNames = anonymousObject.Initializers
            .Select(i => GetPropertyName(i))
            .Where(n => !string.IsNullOrEmpty(n))
            .Take(2)
            .ToList();

        if (propertyNames.Count > 0)
        {
            return $"{string.Join("", propertyNames)}GroupKey";
        }

        return "GroupKey";
    }

    private static string? GetSourceTypeName(ExpressionSyntax expression)
    {
        // Try to extract type name from the expression
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                // e.g., entities.GroupBy(...)
                var name = identifier.Identifier.Text;
                // Capitalize the first letter
                return CapitalizeFirstLetter(name);

            case MemberAccessExpressionSyntax memberAccess:
                // e.g., dbContext.Entities.GroupBy(...)
                return GetSourceTypeName(memberAccess.Name);

            case InvocationExpressionSyntax invocation:
                // e.g., entities.Where(...).GroupBy(...)
                if (invocation.Expression is MemberAccessExpressionSyntax invocationMemberAccess)
                {
                    return GetSourceTypeName(invocationMemberAccess.Expression);
                }
                return null;

            default:
                return null;
        }
    }

    private static string CapitalizeFirstLetter(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        if (char.IsUpper(text[0]))
        {
            return text;
        }
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static string GetPropertyName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        if (initializer.NameEquals != null)
        {
            return initializer.NameEquals.Name.Identifier.Text;
        }

        return ExpressionHelper.GetPropertyNameOrDefault(initializer.Expression, "");
    }
}
