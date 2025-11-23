using System.Collections.Generic;
using System.Collections.Immutable;
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
/// Analyzer that detects unnecessary capture variables in SelectExpr that are not referenced in the lambda.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrs005-unnecessarycaptureanalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnnecessaryCaptureAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRS005";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "Unnecessary capture variables detected",
        "Unnecessary capture variable '{0}' detected. It can be safely removed.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Capture variables that are not referenced in SelectExpr should be removed.",
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{AnalyzerId}.md"
    );

    protected override string DiagnosticId => AnalyzerId;
    protected override LocalizableString Title => RuleInstance.Title;
    protected override LocalizableString MessageFormat => RuleInstance.MessageFormat;
    protected override LocalizableString Description => RuleInstance.Description;
    protected override string Category => "Usage";
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    protected override DiagnosticDescriptor Rule => RuleInstance;

    protected override void RegisterActions(AnalysisContext context)
    {
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

        // Get already captured variables
        var capturedVariables = CaptureHelper.GetCapturedVariables(
            invocation,
            context.SemanticModel
        );

        if (capturedVariables.Count == 0)
        {
            return;
        }

        // Get the lambda parameter name(s)
        var lambdaParameters = LambdaHelper.GetLambdaParameterNames(lambda);

        // Find variables that are actually used in the lambda
        var usedVariables = FindUsedVariables(lambda, lambdaParameters, context.SemanticModel);

        // Report diagnostic for captured variables that are not used
        foreach (var capturedVariable in capturedVariables)
        {
            if (!usedVariables.Contains(capturedVariable))
            {
                // Find the location of this captured variable in the capture argument
                var location = FindCapturedVariableLocation(invocation, capturedVariable);
                if (location != null)
                {
                    var diagnostic = Diagnostic.Create(RuleInstance, location, capturedVariable);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool IsSelectExprCall(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text
                == SelectExprHelper.MethodName,
            IdentifierNameSyntax identifier => identifier.Identifier.Text
                == SelectExprHelper.MethodName,
            _ => false,
        };
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

    private static HashSet<string> FindUsedVariables(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var usedVariables = new HashSet<string>();
        var bodyExpression = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null,
        };

        if (bodyExpression == null)
        {
            return usedVariables;
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

            // Add to used variables
            usedVariables.Add(identifierName);
        }

        // Also check for member accesses (e.g., this.Property, obj.Property)
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

            // Check if this is a field or property that might be captured
            if (symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property)
            {
                // Check if it's 'this.Member'
                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    usedVariables.Add(memberName);
                }
                // Static members accessed via type name don't need capturing
                else if (symbol.IsStatic && memberAccess.Expression is IdentifierNameSyntax)
                {
                    // Skip - static members don't need to be captured
                }
                // Check if it's accessing a member via a local variable/parameter
                else if (memberAccess.Expression is IdentifierNameSyntax exprIdentifier)
                {
                    // The expression part is the variable being used
                    usedVariables.Add(exprIdentifier.Identifier.Text);
                }
            }
        }

        return usedVariables;
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

    private static Location? FindCapturedVariableLocation(
        InvocationExpressionSyntax invocation,
        string variableName
    )
    {
        // Look for the capture argument
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            // Check if it's a named argument called "capture"
            if (argument.NameColon?.Name.Identifier.Text == "capture")
            {
                // Extract variable names from the capture anonymous object
                if (argument.Expression is AnonymousObjectCreationExpressionSyntax anonymousObject)
                {
                    return FindVariableInAnonymousObject(anonymousObject, variableName);
                }
                break;
            }
        }

        // Also check positional arguments (second argument would be capture)
        if (invocation.ArgumentList.Arguments.Count > 1)
        {
            var secondArg = invocation.ArgumentList.Arguments[1];
            if (
                secondArg.NameColon == null
                && secondArg.Expression is AnonymousObjectCreationExpressionSyntax anonymousObject
            )
            {
                return FindVariableInAnonymousObject(anonymousObject, variableName);
            }
        }

        return null;
    }

    private static Location? FindVariableInAnonymousObject(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string variableName
    )
    {
        foreach (var initializer in anonymousObject.Initializers)
        {
            // Get the property name from the initializer
            string propertyName;
            SyntaxNode locationNode;

            if (initializer.NameEquals != null)
            {
                propertyName = initializer.NameEquals.Name.Identifier.Text;
                locationNode = initializer.NameEquals.Name;
            }
            else if (initializer.Expression is IdentifierNameSyntax identifier)
            {
                propertyName = identifier.Identifier.Text;
                locationNode = identifier;
            }
            else
            {
                continue;
            }

            if (propertyName == variableName)
            {
                return locationNode.GetLocation();
            }
        }

        return null;
    }
}
