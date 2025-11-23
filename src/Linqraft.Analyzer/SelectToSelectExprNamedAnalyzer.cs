using Linqraft.Core.AnalyzerHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects IQueryable.Select calls with named types that can be converted to SelectExpr.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SelectToSelectExprNamedAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRS003";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "IQueryable.Select can be converted to SelectExpr",
        "IQueryable.Select with named type can be converted to SelectExpr",
        "Design",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "This Select call on IQueryable can be converted to use SelectExpr for better performance and type safety.",
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
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a Select call
        if (!IsSelectInvocation(invocation.Expression))
        {
            return;
        }

        // Get semantic model and check if it's on IQueryable
        var semanticModel = context.SemanticModel;
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Verify it's the Select method from System.Linq
        if (
            methodSymbol.Name != "Select"
            || !methodSymbol.ContainingNamespace.ToDisplayString().StartsWith("System.Linq")
        )
        {
            return;
        }

        // Check if it's on IQueryable (not IEnumerable)
        if (!IsIQueryable(invocation.Expression, semanticModel, context.CancellationToken))
        {
            return;
        }

        // Check if the lambda contains an object creation expression for a named type
        var objectCreation = FindNamedObjectCreationInArguments(invocation.ArgumentList);
        if (objectCreation == null)
        {
            return;
        }

        // Get the location of the method name (Select)
        var location = GetMethodNameLocation(invocation.Expression);

        // Report diagnostic
        var diagnostic = Diagnostic.Create(RuleInstance, location);
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

    private static bool IsSelectInvocation(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.Text == "Select";

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text == "Select";

            default:
                return false;
        }
    }

    private static bool IsIQueryable(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        // Get the type of the expression before .Select()
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
        var type = typeInfo.Type;

        if (type == null)
        {
            return false;
        }

        // Check if it's IQueryable<T> or implements IQueryable<T> (e.g., DbSet<T>)
        if (type is INamedTypeSymbol namedType)
        {
            // Check if it's IQueryable<T> itself
            var displayString = namedType.OriginalDefinition.ToDisplayString();
            if (displayString.StartsWith("System.Linq.IQueryable<"))
            {
                return true;
            }

            // Check if it implements IQueryable<T>
            foreach (var iface in namedType.AllInterfaces)
            {
                var ifaceDisplayString = iface.OriginalDefinition.ToDisplayString();
                if (ifaceDisplayString.StartsWith("System.Linq.IQueryable<"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ObjectCreationExpressionSyntax? FindNamedObjectCreationInArguments(
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

            // Check if it's an object creation expression (named type, not anonymous)
            if (lambda is ObjectCreationExpressionSyntax objectCreation)
            {
                return objectCreation;
            }
        }

        return null;
    }
}
