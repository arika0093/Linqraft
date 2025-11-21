using System.Collections.Immutable;
using System.Linq;
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
        helpLinkUri: "https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrs004-ternarynullchecktoconditionalanalyzer"
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
        if (!HasNullCheck(conditional.Condition))
        {
            return;
        }

        // Check if the else clause is null
        if (!IsNullOrNullCast(conditional.WhenFalse))
        {
            return;
        }

        // Check if the when true expression is an object or anonymous object creation
        var whenTrueExpr = RemoveNullableCast(conditional.WhenTrue);
        if (
            whenTrueExpr is not ObjectCreationExpressionSyntax
            && whenTrueExpr is not AnonymousObjectCreationExpressionSyntax
        )
        {
            return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(Rule, conditional.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasNullCheck(ExpressionSyntax condition)
    {
        // Check for simple null check: x != null
        if (
            condition is BinaryExpressionSyntax binary
            && binary.Kind() == SyntaxKind.NotEqualsExpression
        )
        {
            return IsNullLiteral(binary.Left) || IsNullLiteral(binary.Right);
        }

        // Check for chained null checks: x != null && y != null
        if (
            condition is BinaryExpressionSyntax logicalAnd
            && logicalAnd.Kind() == SyntaxKind.LogicalAndExpression
        )
        {
            return HasNullCheck(logicalAnd.Left) || HasNullCheck(logicalAnd.Right);
        }

        return false;
    }

    private static bool IsNullLiteral(ExpressionSyntax expr)
    {
        return expr is LiteralExpressionSyntax literal
            && literal.Kind() == SyntaxKind.NullLiteralExpression;
    }

    private static bool IsNullOrNullCast(ExpressionSyntax expr)
    {
        // Check for simple null
        if (IsNullLiteral(expr))
            return true;

        // Check for (Type?)null cast
        if (
            expr is CastExpressionSyntax cast
            && cast.Type is NullableTypeSyntax
            && IsNullLiteral(cast.Expression)
        )
            return true;

        return false;
    }

    private static ExpressionSyntax RemoveNullableCast(ExpressionSyntax expr)
    {
        // Remove (Type?) cast if present
        if (expr is CastExpressionSyntax cast && cast.Type is NullableTypeSyntax)
        {
            return cast.Expression;
        }

        return expr;
    }
}
