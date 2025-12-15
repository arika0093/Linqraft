using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Helper methods for capture parameter detection and variable capture analysis
/// </summary>
internal static class CaptureHelper
{
    /// <summary>
    /// Extracts captured variable names from a SelectExpr invocation's capture argument.
    /// Looks for both named arguments (capture: new { ... }) and positional arguments (second argument).
    /// </summary>
    /// <param name="invocation">The SelectExpr invocation expression</param>
    /// <param name="semanticModel">The semantic model (currently unused but kept for consistency)</param>
    /// <returns>A set of captured variable names</returns>
    public static HashSet<string> GetCapturedVariables(
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

    /// <summary>
    /// Determines if a symbol needs to be captured when used in a SelectExpr lambda.
    /// Local variables, parameters from outer scope, and certain fields/properties need capture.
    /// Const local variables, enum values, and public/internal static members don't need capture.
    /// </summary>
    /// <param name="symbol">The symbol to check</param>
    /// <param name="lambda">The lambda expression containing the reference</param>
    /// <param name="lambdaParameters">The lambda's parameter names</param>
    /// <param name="semanticModel">The semantic model (currently unused but kept for consistency)</param>
    /// <returns>True if the symbol needs to be captured</returns>
    public static bool NeedsCapture(
        ISymbol symbol,
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
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
        // Fields
        else if (symbol.Kind == SymbolKind.Field)
        {
            var fieldSymbol = symbol as IFieldSymbol;
            if (fieldSymbol == null)
            {
                return true;
            }

            // Enum values can be accessed directly
            if (fieldSymbol.ContainingType?.TypeKind == TypeKind.Enum)
            {
                return false;
            }

            // Const fields with public or internal accessibility can be accessed directly
            if (fieldSymbol.IsConst)
            {
                if (
                    fieldSymbol.DeclaredAccessibility == Accessibility.Public
                    || fieldSymbol.DeclaredAccessibility == Accessibility.Internal
                )
                {
                    return false;
                }
            }

            // Static const fields with public or internal accessibility can be accessed directly
            if (fieldSymbol.IsStatic && fieldSymbol.IsConst)
            {
                if (
                    fieldSymbol.DeclaredAccessibility == Accessibility.Public
                    || fieldSymbol.DeclaredAccessibility == Accessibility.Internal
                )
                {
                    return false;
                }
            }

            // All other fields need to be captured
            return true;
        }
        // Properties
        else if (symbol.Kind == SymbolKind.Property)
        {
            var propertySymbol = symbol as IPropertySymbol;
            if (propertySymbol == null)
            {
                return true;
            }

            // Static properties with public or internal accessibility can be accessed directly
            if (propertySymbol.IsStatic)
            {
                if (
                    propertySymbol.DeclaredAccessibility == Accessibility.Public
                    || propertySymbol.DeclaredAccessibility == Accessibility.Internal
                )
                {
                    return false;
                }
            }

            // All other properties need to be captured
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds simple local variables and parameters that need to be captured in a lambda expression.
    /// This method only finds simple identifier references (not member accesses like this.Property or obj.Property).
    /// </summary>
    /// <param name="lambda">The lambda expression to analyze</param>
    /// <param name="lambdaParameters">The lambda's parameter names</param>
    /// <param name="semanticModel">The semantic model for symbol resolution</param>
    /// <returns>A set of variable names that need to be captured</returns>
    public static HashSet<string> FindSimpleVariablesToCapture(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var variablesToCapture = new HashSet<string>();
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
        var memberAccessExpressionsToCapture = new HashSet<ExpressionSyntax>();
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
            if (symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property)
            {
                // Check if it's 'this.Member' or 'instance.Member'
                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    memberAccessExpressionsToCapture.Add(memberAccess.Expression);
                }
                // For static members accessed via type name (e.g., Console.WriteLine),
                // we don't need to capture the type name identifier
                else if (symbol.IsStatic)
                {
                    // Skip - static members don't need their type identifier captured
                    continue;
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
                                memberAccessExpressionsToCapture.Add(memberAccess.Expression);
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
            if (SyntaxHelper.IsPartOfMemberAccess(identifier))
            {
                continue;
            }

            // Skip if this identifier is the expression part of a member access that we're capturing
            if (memberAccessExpressionsToCapture.Contains(identifier))
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
            if (!NeedsCapture(symbol, lambda, lambdaParameters, semanticModel))
            {
                continue;
            }

            if (symbol.Kind == SymbolKind.Local)
            {
                // Skip const local variables - they are compile-time values
                if (symbol is ILocalSymbol localSymbol && localSymbol.IsConst)
                {
                    continue;
                }

                // Ensure this is truly a local variable from outer scope
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    variablesToCapture.Add(identifierName);
                }
            }
            else if (symbol.Kind == SymbolKind.Parameter && !lambdaParameters.Contains(symbol.Name))
            {
                // This is a parameter from an outer scope
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    variablesToCapture.Add(identifierName);
                }
            }
            else if (symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property)
            {
                // This is a field or property accessed without an explicit receiver
                // (implicit 'this' access)
                variablesToCapture.Add(identifierName);
            }
        }

        return variablesToCapture;
    }

    /// <summary>
    /// Creates a capture argument syntax node for a set of variable names.
    /// The argument is created as: capture: new { var1, var2, var3 }
    /// </summary>
    /// <param name="variableNames">Names of variables to capture</param>
    /// <returns>An argument syntax node with the capture parameter</returns>
    public static ArgumentSyntax CreateCaptureArgument(IEnumerable<string> variableNames)
    {
        var orderedNames = variableNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        // Create anonymous object members
        var captureProperties = orderedNames
            .Select(name =>
                SyntaxFactory.AnonymousObjectMemberDeclarator(SyntaxFactory.IdentifierName(name))
            )
            .ToArray();

        // Create anonymous object with proper spacing
        // Use SeparatedList with comma+space separators
        var separators = Enumerable.Repeat(
            SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
            captureProperties.Length - 1
        );
        var captureObject = SyntaxFactory
            .AnonymousObjectCreationExpression(
                SyntaxFactory.SeparatedList(captureProperties, separators)
            )
            .WithNewKeyword(
                SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space)
            )
            .WithOpenBraceToken(
                SyntaxFactory
                    .Token(SyntaxKind.OpenBraceToken)
                    .WithTrailingTrivia(SyntaxFactory.Space)
            )
            .WithCloseBraceToken(
                SyntaxFactory
                    .Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(SyntaxFactory.Space)
            );

        // Create named argument for capture with proper spacing
        var captureArgument = SyntaxFactory
            .Argument(
                SyntaxFactory
                    .NameColon(SyntaxFactory.IdentifierName("capture"))
                    .WithTrailingTrivia(SyntaxFactory.Space),
                default,
                captureObject
            )
            .WithLeadingTrivia(SyntaxFactory.Space);

        return captureArgument;
    }
}
