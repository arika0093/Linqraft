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

        // Find the lambda expression argument
        var lambda = FindLambdaExpression(invocation.ArgumentList);
        if (lambda == null)
        {
            return;
        }

        // Get the lambda parameter name(s)
        var lambdaParameters = GetLambdaParameterNames(lambda);

        // Find variables that need to be captured
        var variablesToCapture = FindVariablesToCapture(
            lambda,
            lambdaParameters,
            context.SemanticModel
        );

        if (variablesToCapture.Count == 0)
        {
            return;
        }

        // Get already captured variables
        var capturedVariables = GetCapturedVariables(invocation, context.SemanticModel);

        // Report diagnostic for variables that are not captured
        foreach (var (variableName, location) in variablesToCapture)
        {
            if (!capturedVariables.Contains(variableName))
            {
                var diagnostic = Diagnostic.Create(Rule, location, variableName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsSelectExprCall(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text
                == "SelectExpr",
            IdentifierNameSyntax identifier => identifier.Identifier.Text == "SelectExpr",
            _ => false,
        };
    }

    private static HashSet<string> GetCapturedVariables(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        var capturedVariables = new HashSet<string>();

        // Look for the capture argument
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            // Check if it's a named argument called "capture"
            if (argument.NameColon?.Name.Identifier.Text == "capture")
            {
                // Extract variable names from the capture anonymous object
                if (argument.Expression is AnonymousObjectCreationExpressionSyntax anonymousObject)
                {
                    foreach (var initializer in anonymousObject.Initializers)
                    {
                        // Get the property name from the initializer
                        string propertyName;
                        if (initializer.NameEquals != null)
                        {
                            propertyName = initializer.NameEquals.Name.Identifier.Text;
                        }
                        else if (initializer.Expression is IdentifierNameSyntax identifier)
                        {
                            propertyName = identifier.Identifier.Text;
                        }
                        else
                        {
                            continue;
                        }
                        capturedVariables.Add(propertyName);
                    }
                }
                break;
            }
        }

        // Also check positional arguments (second argument would be capture)
        if (capturedVariables.Count == 0 && invocation.ArgumentList.Arguments.Count > 1)
        {
            var secondArg = invocation.ArgumentList.Arguments[1];
            if (secondArg.Expression is AnonymousObjectCreationExpressionSyntax anonymousObject)
            {
                foreach (var initializer in anonymousObject.Initializers)
                {
                    string propertyName;
                    if (initializer.NameEquals != null)
                    {
                        propertyName = initializer.NameEquals.Name.Identifier.Text;
                    }
                    else if (initializer.Expression is IdentifierNameSyntax identifier)
                    {
                        propertyName = identifier.Identifier.Text;
                    }
                    else
                    {
                        continue;
                    }
                    capturedVariables.Add(propertyName);
                }
            }
        }

        return capturedVariables;
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
            SimpleLambdaExpressionSyntax simple => ImmutableHashSet.Create(
                simple.Parameter.Identifier.Text
            ),
            ParenthesizedLambdaExpressionSyntax paren => paren
                .ParameterList.Parameters.Select(p => p.Identifier.Text)
                .ToImmutableHashSet(),
            _ => ImmutableHashSet<string>.Empty,
        };
    }

    private static List<(string Name, Location Location)> FindVariablesToCapture(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var variablesToCapture = new List<(string, Location)>();
        var bodyExpression = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null,
        };

        if (bodyExpression == null)
        {
            return variablesToCapture;
        }

        // First, find all member access expressions that will be captured
        // We'll use this to skip identifiers that are part of these member accesses
        var memberAccessesToCapture = new HashSet<ExpressionSyntax>();
        var memberAccesses = bodyExpression
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>();

        foreach (var memberAccess in memberAccesses)
        {
            // Skip if this is a lambda parameter access (e.g., s.Property)
            if (
                memberAccess.Expression is IdentifierNameSyntax exprId
                && lambdaParameters.Contains(exprId.Identifier.Text)
            )
            {
                continue;
            }

            // Get symbol information for the member being accessed
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            var symbol = symbolInfo.Symbol;

            if (symbol == null)
            {
                continue;
            }

            // Check if this is a field or property that needs to be captured
            if ((symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property))
            {
                // Check if it's 'this.Member' or 'Type.StaticMember'
                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    memberAccessesToCapture.Add(memberAccess.Expression);
                }
                else if (
                    symbol.IsStatic && memberAccess.Expression is IdentifierNameSyntax
                )
                {
                    memberAccessesToCapture.Add(memberAccess.Expression);
                }
                else if (memberAccess.Expression is IdentifierNameSyntax exprIdentifier)
                {
                    // Check if the expression is a local variable or parameter from outer scope
                    var exprSymbolInfo = semanticModel.GetSymbolInfo(exprIdentifier);
                    var exprSymbol = exprSymbolInfo.Symbol;

                    if (exprSymbol != null)
                    {
                        // Check if it's a local variable or parameter from outer scope
                        if (
                            exprSymbol.Kind == SymbolKind.Local
                            || exprSymbol.Kind == SymbolKind.Parameter
                        )
                        {
                            // Ensure it's from outer scope, not declared inside the lambda
                            var exprSymbolLocation = exprSymbol.Locations.FirstOrDefault();
                            if (
                                exprSymbolLocation != null
                                && !lambda.Span.Contains(exprSymbolLocation.SourceSpan)
                            )
                            {
                                // Mark this expression as part of a member access we're capturing
                                memberAccessesToCapture.Add(memberAccess.Expression);
                            }
                        }
                    }
                }
            }
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

            // Skip if this identifier is the expression part of a member access that we're capturing
            if (memberAccessesToCapture.Contains(identifier))
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

            // Check if it needs to be captured
            if (NeedsCapture(symbol, lambda, lambdaParameters))
            {
                variablesToCapture.Add((identifierName, identifier.GetLocation()));
            }
        }

        // Now add member access expressions
        foreach (var memberAccess in memberAccesses)
        {
            // Skip if this is a lambda parameter access (e.g., s.Property)
            if (
                memberAccess.Expression is IdentifierNameSyntax exprId
                && lambdaParameters.Contains(exprId.Identifier.Text)
            )
            {
                continue;
            }

            // Get symbol information for the member being accessed
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            var symbol = symbolInfo.Symbol;

            if (symbol == null)
            {
                continue;
            }

            // Check if this is a field or property that needs to be captured
            if ((symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property))
            {
                // Check if it's 'this.Member' or 'Type.StaticMember'
                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    variablesToCapture.Add((memberName, memberAccess.GetLocation()));
                }
                else if (
                    symbol.IsStatic && memberAccess.Expression is IdentifierNameSyntax
                )
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    variablesToCapture.Add((memberName, memberAccess.GetLocation()));
                }
                else if (memberAccess.Expression is IdentifierNameSyntax exprIdentifier)
                {
                    // Check if the expression is a local variable or parameter from outer scope
                    var exprSymbolInfo = semanticModel.GetSymbolInfo(exprIdentifier);
                    var exprSymbol = exprSymbolInfo.Symbol;

                    if (exprSymbol != null)
                    {
                        // Check if it's a local variable or parameter from outer scope
                        if (
                            exprSymbol.Kind == SymbolKind.Local
                            || exprSymbol.Kind == SymbolKind.Parameter
                        )
                        {
                            // Ensure it's from outer scope, not declared inside the lambda
                            var exprSymbolLocation = exprSymbol.Locations.FirstOrDefault();
                            if (
                                exprSymbolLocation != null
                                && !lambda.Span.Contains(exprSymbolLocation.SourceSpan)
                            )
                            {
                                var memberName = memberAccess.Name.Identifier.Text;
                                variablesToCapture.Add((memberName, memberAccess.GetLocation()));
                            }
                        }
                    }
                }
            }
        }

        return variablesToCapture;
    }

    private static bool NeedsCapture(
        ISymbol symbol,
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters
    )
    {
        // Local variables (except const local variables)
        if (symbol.Kind == SymbolKind.Local)
        {
            // Skip const local variables - they are compile-time values
            if (symbol is ILocalSymbol localSymbol && localSymbol.IsConst)
            {
                return false;
            }

            // Ensure this is truly a local variable from outer scope
            // Check that the symbol is not declared inside the lambda
            var symbolLocation = symbol.Locations.FirstOrDefault();
            if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
            {
                return true;
            }
        }
        // Parameters from outer scope
        else if (symbol.Kind == SymbolKind.Parameter && !lambdaParameters.Contains(symbol.Name))
        {
            // This is a parameter from an outer scope
            var symbolLocation = symbol.Locations.FirstOrDefault();
            if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
            {
                return true;
            }
        }
        // Fields and properties (both instance and static, including const fields)
        else if (symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property)
        {
            // All fields and properties need to be captured for complete isolation
            return true;
        }

        return false;
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
            _ => null,
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
            else if (
                symbol.Kind == SymbolKind.Parameter
                && !lambdaParameters.Contains(identifierName)
            )
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

        // Check if this is inside a NameEquals (property name in anonymous object)
        // e.g., in "new { Value1 = ...", "Value1" is the property name
        if (parent is NameEqualsSyntax)
        {
            return true;
        }

        // Check if this is the left side of an assignment in an object initializer
        // e.g., in "new MyClass { Id = s.Id }", the left "Id" is the property being assigned
        if (parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier)
        {
            return true;
        }

        return false;
    }
}
