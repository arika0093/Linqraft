using System.Collections.Immutable;
using System.Linq;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects ternary operators with null checks that return object creations
/// and can be converted to use null-conditional operators.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrs004-ternarynullchecktoconditionalanalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TernaryNullCheckToConditionalAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRS004";

    private static readonly LocalizableString Title =
        "Ternary null check can use null-conditional operators";
    private static readonly LocalizableString MessageFormat =
        "Ternary null check returning object can be simplified to use null-conditional operators";
    private static readonly LocalizableString Description =
        "This ternary operator with null check can be simplified to use null-conditional operators (?.) for better readability and to avoid CS8602 warnings.";
    private const string Category = "Design";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{DiagnosticId}.md"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeConditional, SyntaxKind.ConditionalExpression);
    }

    private static void AnalyzeConditional(SyntaxNodeAnalysisContext context)
    {
        var conditional = (ConditionalExpressionSyntax)context.Node;

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
            var diagnostic = Diagnostic.Create(Rule, conditional.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
