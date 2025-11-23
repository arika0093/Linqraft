using Linqraft.Core;
using Linqraft.Core.AnalyzerHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects SelectExpr calls without type arguments that can be converted to typed versions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SelectExprToTypedAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRS001";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "SelectExpr can be converted to typed version",
        "SelectExpr can be converted to SelectExpr<{0}, {1}>",
        "Design",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "This SelectExpr call can be converted to use explicit type arguments for better type safety.",
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{AnalyzerId}.md"
    );

    protected override string DiagnosticId => AnalyzerId;
    protected override LocalizableString Title => RuleInstance.Title;
    protected override LocalizableString MessageFormat => RuleInstance.MessageFormat;
    protected override LocalizableString Description => RuleInstance.Description;
    protected override DiagnosticDescriptor Rule => RuleInstance;

    protected override void RegisterActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a SelectExpr call without type arguments
        if (!IsSelectExprWithoutTypeArguments(invocation.Expression))
        {
            return;
        }

        // Check if the lambda contains an anonymous type
        var anonymousType = FindAnonymousTypeInArguments(invocation.ArgumentList);
        if (anonymousType == null)
        {
            return;
        }

        // Get the source type from the expression
        var semanticModel = context.SemanticModel;
        var sourceType = GetSourceType(
            invocation.Expression,
            semanticModel,
            context.CancellationToken
        );
        if (sourceType == null)
        {
            return;
        }

        // Generate DTO name based on context
        var dtoName = GenerateDtoName(invocation, anonymousType);

        // Get the location of the method name (SelectExpr)
        var location = GetMethodNameLocation(invocation.Expression);

        // Report diagnostic
        var diagnostic = Diagnostic.Create(RuleInstance, location, sourceType.Name, dtoName);
        context.ReportDiagnostic(diagnostic);
    }

    private static Location GetMethodNameLocation(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.GetLocation();
        }
        return expression.GetLocation();
    }

    private static bool IsSelectExprWithoutTypeArguments(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                // Check if it's SelectExpr and NOT a generic name (no type arguments)
                return memberAccess.Name.Identifier.Text == SelectExprHelper.MethodName
                    && memberAccess.Name is not GenericNameSyntax;

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text == SelectExprHelper.MethodName;

            default:
                return false;
        }
    }

    private static AnonymousObjectCreationExpressionSyntax? FindAnonymousTypeInArguments(
        ArgumentListSyntax argumentList
    )
    {
        foreach (var argument in argumentList.Arguments)
        {
            // Look for lambda expressions
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

    private static ITypeSymbol? GetSourceType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        // Get the type of the expression before .SelectExpr()
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
        var type = typeInfo.Type;

        if (type == null)
        {
            return null;
        }

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

    private static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousType
    )
    {
        return DtoNamingHelper.GenerateDtoName(invocation, anonymousType);
    }
}
