using System.Linq;
using Linqraft.Core;
using Linqraft.Core.AnalyzerHelpers;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects ternary operators with null checks that return object creations
/// and can be converted to use null-conditional operators.
/// This analyzer only triggers inside SelectExpr calls.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TernaryNullCheckToConditionalAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRS004";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "Ternary null check can use null-conditional operators",
        "Ternary null check returning object can be simplified to use null-conditional operators",
        "Design",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "This ternary operator with null check can be simplified to use null-conditional operators (?.) for better readability and to avoid CS8602 warnings.",
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{AnalyzerId}.md"
    );

    protected override string DiagnosticId => AnalyzerId;
    protected override LocalizableString Title => RuleInstance.Title;
    protected override LocalizableString MessageFormat => RuleInstance.MessageFormat;
    protected override LocalizableString Description => RuleInstance.Description;
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Info;
    protected override DiagnosticDescriptor Rule => RuleInstance;

    protected override void RegisterActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeConditional, SyntaxKind.ConditionalExpression);
    }

    private static void AnalyzeConditional(SyntaxNodeAnalysisContext context)
    {
        var conditional = (ConditionalExpressionSyntax)context.Node;

        // Check if this conditional is inside a SelectExpr call
        // If not, don't report any diagnostic (issue #156)
        if (!IsInsideSelectExpr(conditional, context.SemanticModel))
        {
            return;
        }

        // Check if the condition is a null check (simple or complex with &&)
        if (!NullConditionalHelper.HasNullCheck(conditional.Condition))
        {
            return;
        }

        // Extract both expressions, removing any type casts
        var whenTrueExpr = NullConditionalHelper.RemoveNullableCast(conditional.WhenTrue);
        var whenFalseExpr = NullConditionalHelper.RemoveNullableCast(conditional.WhenFalse);

        // Check if one branch is null and the other contains an object creation
        var whenTrueIsNull = NullConditionalHelper.IsNullOrNullCast(conditional.WhenTrue);
        var whenFalseIsNull = NullConditionalHelper.IsNullOrNullCast(conditional.WhenFalse);
        var whenTrueHasObject =
            whenTrueExpr is ObjectCreationExpressionSyntax
            || whenTrueExpr is AnonymousObjectCreationExpressionSyntax;
        var whenFalseHasObject =
            whenFalseExpr is ObjectCreationExpressionSyntax
            || whenFalseExpr is AnonymousObjectCreationExpressionSyntax;

        // Report diagnostic if one branch is null and the other has an object creation
        // This handles both: condition ? new{} : null  AND  condition ? null : new{}
        if ((whenTrueIsNull && whenFalseHasObject) || (whenFalseIsNull && whenTrueHasObject))
        {
            var diagnostic = Diagnostic.Create(RuleInstance, conditional.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Checks if the given syntax node is inside a SelectExpr method call
    /// </summary>
    private static bool IsInsideSelectExpr(SyntaxNode node, SemanticModel semanticModel)
    {
        // Walk up the syntax tree to find an InvocationExpressionSyntax
        var ancestor = node.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();

        while (ancestor != null)
        {
            // Check if this invocation is a SelectExpr call
            if (SelectExprHelper.IsSelectExprInvocation(ancestor, semanticModel))
            {
                return true;
            }

            // Continue walking up to check parent invocations
            ancestor = ancestor.Parent?.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        }

        return false;
    }
}
