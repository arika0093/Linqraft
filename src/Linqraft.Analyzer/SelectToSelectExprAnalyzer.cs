using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects Select queries that can be converted to SelectExpr
/// and SelectExpr queries that can be enhanced
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SelectToSelectExprAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.SelectToSelectExpr,
            DiagnosticDescriptors.EnhanceSelectExpr);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a method call
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;

        // Check if the method name is "Select" or "SelectExpr"
        if (methodName == "Select")
        {
            AnalyzeSelect(context, invocation, memberAccess);
        }
        else if (methodName == "SelectExpr")
        {
            AnalyzeSelectExpr(context, invocation, memberAccess);
        }
    }

    private static void AnalyzeSelect(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess)
    {
        // Get the symbol information
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Check if this is System.Linq.Queryable.Select or System.Linq.Enumerable.Select
        if (methodSymbol.ContainingType?.ToString() != "System.Linq.Queryable" &&
            methodSymbol.ContainingType?.ToString() != "System.Linq.Enumerable")
            return;

        // Check if the argument is a lambda expression
        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        var argument = invocation.ArgumentList.Arguments[0];
        if (argument.Expression is not (SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax))
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.SelectToSelectExpr,
            memberAccess.Name.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeSelectExpr(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess)
    {
        // Check if the argument is a lambda expression
        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        var argument = invocation.ArgumentList.Arguments[0];
        if (argument.Expression is not (SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax))
            return;

        // Check if it's using anonymous type or can be enhanced
        var canBeEnhanced = CanSelectExprBeEnhanced(invocation, context.SemanticModel, memberAccess);
        if (canBeEnhanced)
        {
            // Report diagnostic
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.EnhanceSelectExpr,
                memberAccess.Name.GetLocation());

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool CanSelectExprBeEnhanced(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        MemberAccessExpressionSyntax memberAccess)
    {
        // Check if it's already using auto-generated DTO (has generic type arguments)
        if (memberAccess.Name is GenericNameSyntax)
        {
            // Already using auto-generated DTO, could only be enhanced with separate file
            return true;
        }

        // Check if it's using anonymous type
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var argument = invocation.ArgumentList.Arguments[0];
            if (argument.Expression is SimpleLambdaExpressionSyntax simpleLambda)
            {
                if (simpleLambda.Body is AnonymousObjectCreationExpressionSyntax)
                {
                    // Using anonymous type, can be enhanced to auto-generated DTO
                    return true;
                }
            }
            else if (argument.Expression is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
            {
                if (parenthesizedLambda.Body is AnonymousObjectCreationExpressionSyntax)
                {
                    // Using anonymous type, can be enhanced to auto-generated DTO
                    return true;
                }
            }
        }

        return false;
    }
}
