using Linqraft.Core.AnalyzerHelpers;
using Linqraft.Core.RoslynHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects SelectExpr calls on IQueryable&lt;IGrouping&lt;anonymous type, TElement&gt;&gt;.
/// Anonymous types cannot be properly expanded in generated code, so this pattern should be avoided.
/// The code fix suggests converting the GroupBy anonymous type key to a tuple.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GroupByAnonymousKeyAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRE002";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "SelectExpr is not supported after GroupBy with anonymous type key",
        "SelectExpr cannot be used after GroupBy with anonymous type key. Convert the anonymous type to a tuple.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using SelectExpr after GroupBy with an anonymous type key, the generated code cannot properly expand the anonymous type. Use a tuple instead of an anonymous type in the GroupBy key selector.",
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{AnalyzerId}.md"
    );

    protected override string DiagnosticId => AnalyzerId;
    protected override LocalizableString Title => RuleInstance.Title;
    protected override LocalizableString MessageFormat => RuleInstance.MessageFormat;
    protected override LocalizableString Description => RuleInstance.Description;
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    protected override DiagnosticDescriptor Rule => RuleInstance;

    protected override void RegisterActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a SelectExpr call
        if (!IsSelectExprInvocation(invocation.Expression))
        {
            return;
        }

        // Get the expression before .SelectExpr()
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        // Get the type of the expression before .SelectExpr()
        var semanticModel = context.SemanticModel;
        var typeInfo = semanticModel.GetTypeInfo(
            memberAccess.Expression,
            context.CancellationToken
        );
        var type = typeInfo.Type;

        if (type == null)
        {
            return;
        }

        // Check if it's IQueryable<IGrouping<anonymous type, TElement>>
        if (
            !RoslynTypeHelper.IsQueryableWithAnonymousGroupingKey(
                type,
                semanticModel.Compilation
            )
        )
        {
            return;
        }

        // Find the GroupBy invocation in the chain
        var groupByInvocation = FindGroupByInvocation(memberAccess.Expression);
        if (groupByInvocation == null)
        {
            return;
        }

        // Report diagnostic at the GroupBy location
        var groupByLocation = GetMethodNameLocation(groupByInvocation);
        ReportDiagnostic(context, groupByLocation);
    }

    private static bool IsSelectExprInvocation(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                var name = memberAccess.Name;
                // Handle both simple name and generic name
                return name switch
                {
                    IdentifierNameSyntax identifier => identifier.Identifier.Text == "SelectExpr",
                    GenericNameSyntax genericName => genericName.Identifier.Text == "SelectExpr",
                    _ => false,
                };

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text == "SelectExpr";

            case GenericNameSyntax genericName:
                return genericName.Identifier.Text == "SelectExpr";

            default:
                return false;
        }
    }

    private static InvocationExpressionSyntax? FindGroupByInvocation(ExpressionSyntax expression)
    {
        // Walk up the expression tree to find GroupBy invocation
        var current = expression;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation)
            {
                if (IsGroupByInvocation(invocation.Expression))
                {
                    return invocation;
                }

                // Continue up the chain
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    current = memberAccess.Expression;
                    continue;
                }
            }
            else if (current is MemberAccessExpressionSyntax memberAccess)
            {
                current = memberAccess.Expression;
                continue;
            }

            break;
        }

        return null;
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

    private static Location GetMethodNameLocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.GetLocation();
        }

        return invocation.GetLocation();
    }
}
