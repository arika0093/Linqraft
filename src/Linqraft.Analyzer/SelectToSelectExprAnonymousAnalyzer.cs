using System.Collections.Immutable;
using System.Linq;
using Linqraft.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects IQueryable.Select calls with anonymous types that can be converted to SelectExpr.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrs002-selecttoselectexpranonymousanalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SelectToSelectExprAnonymousAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRS002";

    private static readonly LocalizableString Title =
        "IQueryable.Select can be converted to SelectExpr";
    private static readonly LocalizableString MessageFormat =
        "IQueryable.Select with anonymous type can be converted to SelectExpr";
    private static readonly LocalizableString Description =
        "This Select call on IQueryable can be converted to use SelectExpr for better performance and type safety.";
    private const string Category = "Design";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrs002-selecttoselectexpranonymousanalyzer"
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
        if (methodSymbol.Name != "Select" || 
            !methodSymbol.ContainingNamespace.ToDisplayString().StartsWith("System.Linq"))
        {
            return;
        }

        // Check if it's on IQueryable (not IEnumerable)
        if (!IsIQueryable(invocation.Expression, semanticModel, context.CancellationToken))
        {
            return;
        }

        // Check if the lambda contains an anonymous type
        var anonymousType = FindAnonymousTypeInArguments(invocation.ArgumentList);
        if (anonymousType == null)
        {
            return;
        }

        // Get the location of the method name (Select)
        var location = GetMethodNameLocation(invocation.Expression);

        // Report diagnostic
        var diagnostic = Diagnostic.Create(Rule, location);
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
}
