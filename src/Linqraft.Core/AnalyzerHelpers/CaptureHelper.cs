using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Helper methods for capture parameter detection and variable capture analysis
/// </summary>
public static class CaptureHelper
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
}
