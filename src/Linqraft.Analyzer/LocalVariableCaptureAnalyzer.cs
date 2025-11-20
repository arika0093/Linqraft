using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects local variable usage in SelectExpr without capture parameter.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqre001-localvariablecaptureanalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LocalVariableCaptureAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRE001";

    private static readonly LocalizableString Title =
        "Local variable used in SelectExpr without capture parameter";
    private static readonly LocalizableString MessageFormat =
        "Local variable '{0}' is used in SelectExpr. Use the capture parameter to pass local variables.";
    private static readonly LocalizableString Description =
        "Local variables referenced in SelectExpr should be passed via the capture parameter.";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqre001-localvariablecaptureanalyzer"
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

        // Check if it already has a capture parameter
        if (HasCaptureParameter(invocation))
        {
            return;
        }

        // Find the lambda expression argument
        var lambda = FindLambdaExpression(invocation.ArgumentList);
        if (lambda == null)
        {
            return;
        }

        // Get the lambda parameter name(s)
        var lambdaParameters = GetLambdaParameterNames(lambda);

        // Find local variables referenced in the lambda body
        var localVariables = FindLocalVariables(lambda, lambdaParameters, context.SemanticModel);

        // Report diagnostic for each local variable found
        foreach (var (variableName, location) in localVariables)
        {
            var diagnostic = Diagnostic.Create(Rule, location, variableName);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsSelectExprCall(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.Text == "SelectExpr",
            IdentifierNameSyntax identifier =>
                identifier.Identifier.Text == "SelectExpr",
            _ => false
        };
    }

    private static bool HasCaptureParameter(InvocationExpressionSyntax invocation)
    {
        // Check if there's a named argument called "capture"
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text == "capture")
            {
                return true;
            }
        }

        // Check if there are more than 1 arguments (first is lambda, second would be capture)
        return invocation.ArgumentList.Arguments.Count > 1;
    }

    private static LambdaExpressionSyntax? FindLambdaExpression(ArgumentListSyntax argumentList)
    {
        foreach (var argument in argumentList.Arguments)
        {
            if (argument.Expression is LambdaExpressionSyntax lambda)
            {
                return lambda;
            }
        }

        return null;
    }

    private static ImmutableHashSet<string> GetLambdaParameterNames(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple =>
                ImmutableHashSet.Create(simple.Parameter.Identifier.Text),
            ParenthesizedLambdaExpressionSyntax paren =>
                paren.ParameterList.Parameters.Select(p => p.Identifier.Text).ToImmutableHashSet(),
            _ => ImmutableHashSet<string>.Empty
        };
    }

    private static List<(string Name, Location Location)> FindLocalVariables(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var localVariables = new List<(string, Location)>();
        var bodyExpression = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null
        };

        if (bodyExpression == null)
        {
            return localVariables;
        }

        // Find all identifier references in the lambda body
        var identifiers = bodyExpression.DescendantNodes().OfType<IdentifierNameSyntax>();

        foreach (var identifier in identifiers)
        {
            var identifierName = identifier.Identifier.Text;

            // Skip if it's a lambda parameter
            if (lambdaParameters.Contains(identifierName))
            {
                continue;
            }

            // Skip if it's part of a member access expression on the right side
            // (e.g., s.Property where Property is not a local variable)
            if (IsPartOfMemberAccess(identifier))
            {
                continue;
            }

            // Get symbol information
            var symbolInfo = semanticModel.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;

            if (symbol == null)
            {
                continue;
            }

            // Check if it's a local variable or parameter (not a lambda parameter)
            if (symbol.Kind == SymbolKind.Local)
            {
                // Skip constants - they are compile-time values and don't need capture
                if (symbol is ILocalSymbol localSymbol && localSymbol.IsConst)
                {
                    continue;
                }

                // Ensure this is truly a local variable from outer scope
                // Check that the symbol is not declared inside the lambda
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    localVariables.Add((identifierName, identifier.GetLocation()));
                }
            }
            else if (symbol.Kind == SymbolKind.Parameter && !lambdaParameters.Contains(identifierName))
            {
                // This is a parameter from an outer scope
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    localVariables.Add((identifierName, identifier.GetLocation()));
                }
            }
        }

        return localVariables;
    }

    private static bool IsPartOfMemberAccess(IdentifierNameSyntax identifier)
    {
        // Check if this identifier is the name part of a member access expression
        // e.g., in "s.Property", "Property" is part of member access
        var parent = identifier.Parent;
        
        if (parent is MemberAccessExpressionSyntax memberAccess)
        {
            // If the identifier is on the right side of the dot, it's a member name
            return memberAccess.Name == identifier;
        }

        // Check for conditional member access (e.g., s?.Property)
        if (parent is MemberBindingExpressionSyntax)
        {
            return true;
        }

        return false;
    }
}
