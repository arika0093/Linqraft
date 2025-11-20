using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects SelectExpr usage in Razor files.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqre002-selectexprinrazoranalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SelectExprInRazorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRE002";

    private static readonly LocalizableString Title = "Cannot use SelectExpr in Razor files";
    private static readonly LocalizableString MessageFormat =
        "SelectExpr cannot be used inside Razor files";
    private static readonly LocalizableString Description =
        "Razor files are processed by Source Generators, so Linqraft's SelectExpr, which also uses Source Generators, cannot be used in Razor files.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqre002-selectexprinrazoranalyzer"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a SelectExpr call
        if (!IsSelectExprCall(invocation.Expression))
        {
            return;
        }

        // Check if we're in a Razor file
        if (!IsInRazorFile(context.SemanticModel.SyntaxTree))
        {
            return;
        }

        // Report diagnostic
        var location = GetMethodNameLocation(invocation.Expression);
        var diagnostic = Diagnostic.Create(Rule, location);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsSelectExprCall(ExpressionSyntax expression)
    {
        return expression switch
        {
            // obj.SelectExpr(...)
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text
                == "SelectExpr",
            // SelectExpr(...) - unlikely but handle it
            IdentifierNameSyntax identifier => identifier.Identifier.Text == "SelectExpr",
            _ => false,
        };
    }

    private static bool IsInRazorFile(SyntaxTree syntaxTree)
    {
        var filePath = syntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        // Check if the file has a .razor or .cshtml extension
        return filePath.EndsWith(".razor", System.StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".cshtml", System.StringComparison.OrdinalIgnoreCase);
    }

    private static Location GetMethodNameLocation(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.GetLocation();
        }
        return expression.GetLocation();
    }
}
