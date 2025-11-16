using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects Select queries that can be converted to SelectExpr
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SelectToSelectExprAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.SelectToSelectExpr);

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

        // Check if the method name is "Select"
        if (memberAccess.Name.Identifier.Text != "Select")
            return;

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
}
