using Linqraft.Core;
using Linqraft.Core.AnalyzerHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects GroupBy calls with anonymous type keys followed by SelectExpr.
/// This pattern is problematic because the generated code cannot properly handle anonymous types
/// in the input type parameter.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GroupByAnonymousKeyAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRE002";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "GroupBy with anonymous type key cannot be used with SelectExpr",
        "GroupBy with anonymous type key cannot be used with SelectExpr. Convert the anonymous type to a named class.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Using an anonymous type as the key in GroupBy and then applying SelectExpr does not work correctly because the input type contains an anonymous type, which prevents the generated code from converting types properly. Convert the anonymous type key to a named class.",
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzers/{AnalyzerId}.md"
    );

    protected override string DiagnosticId => AnalyzerId;
    protected override LocalizableString Title => RuleInstance.Title;
    protected override LocalizableString MessageFormat => RuleInstance.MessageFormat;
    protected override LocalizableString Description => RuleInstance.Description;
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    protected override string Category => "Usage";
    protected override DiagnosticDescriptor Rule => RuleInstance;

    protected override void RegisterActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a SelectExpr call
        if (!IsSelectExprInvocation(invocation.Expression))
        {
            return;
        }

        // Check if the source is a GroupBy with anonymous type key
        var groupByInvocation = FindGroupByWithAnonymousKey(
            invocation.Expression,
            context.SemanticModel,
            context.CancellationToken
        );

        if (groupByInvocation == null)
        {
            return;
        }

        // Find the anonymous object creation in the GroupBy key selector
        var anonymousObject = FindAnonymousTypeInGroupByKeySelector(groupByInvocation);
        if (anonymousObject == null)
        {
            return;
        }

        // Report diagnostic on the anonymous type in GroupBy
        var diagnostic = Diagnostic.Create(RuleInstance, anonymousObject.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsSelectExprInvocation(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.Text == SelectExprHelper.MethodName;

            case GenericNameSyntax genericName:
                return genericName.Identifier.Text == SelectExprHelper.MethodName;

            default:
                return false;
        }
    }

    /// <summary>
    /// Finds a GroupBy invocation with an anonymous type key in the chain leading to this expression.
    /// This handles cases like:
    /// - source.GroupBy(x => new { ... }).SelectExpr(...)
    /// - source.GroupBy(x => new { ... }).Where(...).SelectExpr(...)
    /// </summary>
    private static InvocationExpressionSyntax? FindGroupByWithAnonymousKey(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        // Navigate up the method chain to find GroupBy
        var current = expression;

        while (current is MemberAccessExpressionSyntax memberAccess)
        {
            var sourceExpression = memberAccess.Expression;

            // Check if the source is an invocation
            if (sourceExpression is InvocationExpressionSyntax sourceInvocation)
            {
                // Check if it's a GroupBy call with anonymous type key
                if (IsGroupByWithAnonymousKey(sourceInvocation, semanticModel, cancellationToken))
                {
                    return sourceInvocation;
                }

                // Continue up the chain
                current = sourceInvocation.Expression;
            }
            else
            {
                break;
            }
        }

        return null;
    }

    private static bool IsGroupByWithAnonymousKey(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        // Check if this is a GroupBy call
        if (!IsGroupByInvocation(invocation.Expression))
        {
            return false;
        }

        // Get semantic info to verify it's a LINQ GroupBy
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        // Verify it's a GroupBy method from System.Linq
        if (
            methodSymbol.Name != "GroupBy"
            || !methodSymbol.ContainingNamespace.ToDisplayString().StartsWith("System.Linq")
        )
        {
            return false;
        }

        // Check if the key selector contains an anonymous type
        return HasAnonymousTypeInKeySelector(invocation);
    }

    private static bool IsGroupByInvocation(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.Text == "GroupBy";

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text == "GroupBy";

            default:
                return false;
        }
    }

    private static bool HasAnonymousTypeInKeySelector(InvocationExpressionSyntax invocation)
    {
        return FindAnonymousTypeInGroupByKeySelector(invocation) != null;
    }

    private static AnonymousObjectCreationExpressionSyntax? FindAnonymousTypeInGroupByKeySelector(
        InvocationExpressionSyntax invocation
    )
    {
        // The first argument is typically the key selector
        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        var firstArgument = invocation.ArgumentList.Arguments[0];

        // Look for lambda expressions
        var lambdaBody = firstArgument.Expression switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null,
        };

        if (lambdaBody is AnonymousObjectCreationExpressionSyntax anonymousObject)
        {
            return anonymousObject;
        }

        return null;
    }
}
