using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that adds capture parameter to SelectExpr for local variables
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalVariableCaptureCodeFixProvider))]
[Shared]
public class LocalVariableCaptureCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LocalVariableCaptureAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the invocation expression containing the SelectExpr call
        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv => IsSelectExprCall(inv.Expression));

        if (invocation == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add capture parameter",
                createChangedDocument: c =>
                    AddCaptureParameterAsync(context.Document, invocation, c),
                equivalenceKey: "AddCaptureParameter"
            ),
            diagnostic
        );
    }

    private async Task<Document> AddCaptureParameterAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Find the lambda expression
        var lambda = FindLambdaExpression(invocation.ArgumentList);
        if (lambda == null)
            return document;

        // Get lambda parameter names
        var lambdaParameters = GetLambdaParameterNames(lambda);

        // Find all variables that need to be captured
        var variablesToCapture = FindVariablesToCapture(lambda, lambdaParameters, semanticModel);

        if (variablesToCapture.Count == 0)
            return document;

        // Get already captured variables
        var capturedVariables = GetCapturedVariables(invocation);
        
        // Combine existing and new variables
        var allVariables = new HashSet<string>(capturedVariables);
        allVariables.UnionWith(variablesToCapture);

        // Create capture argument with anonymous object containing all variables
        var captureProperties = allVariables
            .OrderBy(name => name)
            .Select(name =>
                SyntaxFactory.AnonymousObjectMemberDeclarator(
                    SyntaxFactory.IdentifierName(name)
                )
            )
            .ToArray();

        var captureObject = SyntaxFactory.AnonymousObjectCreationExpression(
            SyntaxFactory.SeparatedList(captureProperties)
        );

        // Create named argument for capture
        var captureArgument = SyntaxFactory.Argument(
            SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("capture")),
            default,
            captureObject
        );

        InvocationExpressionSyntax newInvocation;
        
        // Check if there's already a capture argument to replace
        var existingCaptureArgIndex = FindCaptureArgumentIndex(invocation);
        if (existingCaptureArgIndex >= 0)
        {
            // Replace existing capture argument
            var arguments = invocation.ArgumentList.Arguments.ToList();
            arguments[existingCaptureArgIndex] = captureArgument;
            var newArgumentList = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(arguments)
            );
            newInvocation = invocation.WithArgumentList(newArgumentList);
        }
        else
        {
            // Add new capture argument
            var newArgumentList = invocation.ArgumentList.AddArguments(captureArgument);
            newInvocation = invocation.WithArgumentList(newArgumentList);
        }

        // Replace the old invocation with the new one
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }

    private static int FindCaptureArgumentIndex(InvocationExpressionSyntax invocation)
    {
        for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var argument = invocation.ArgumentList.Arguments[i];
            if (argument.NameColon?.Name.Identifier.Text == "capture")
            {
                return i;
            }
        }

        // Check if there are more than 1 arguments (second would be capture)
        if (invocation.ArgumentList.Arguments.Count > 1)
        {
            return 1;
        }

        return -1;
    }

    private static HashSet<string> GetCapturedVariables(InvocationExpressionSyntax invocation)
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

    private static bool IsSelectExprCall(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.Text == "SelectExpr",
            IdentifierNameSyntax identifier => identifier.Identifier.Text == "SelectExpr",
            _ => false
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

    private static HashSet<string> FindVariablesToCapture(
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
            _ => null
        };

        if (bodyExpression == null)
        {
            return variablesToCapture;
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

            // Check if it needs to be captured
            if (NeedsCapture(symbol, lambda, lambdaParameters))
            {
                variablesToCapture.Add(identifierName);
            }
        }

        // Also find member access expressions (this.Property, Class.StaticMember)
        var memberAccesses = bodyExpression.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

        foreach (var memberAccess in memberAccesses)
        {
            // Skip if this is a lambda parameter access (e.g., s.Property)
            if (memberAccess.Expression is IdentifierNameSyntax exprId && 
                lambdaParameters.Contains(exprId.Identifier.Text))
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
                if (memberAccess.Expression is ThisExpressionSyntax ||
                    (symbol.IsStatic && memberAccess.Expression is IdentifierNameSyntax))
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    variablesToCapture.Add(memberName);
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
            var symbolLocation = symbol.Locations.FirstOrDefault();
            if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
            {
                return true;
            }
        }
        // Parameters from outer scope
        else if (symbol.Kind == SymbolKind.Parameter && !lambdaParameters.Contains(symbol.Name))
        {
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

    private static HashSet<string> FindLocalVariables(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var localVariables = new HashSet<string>();
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
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    localVariables.Add(identifierName);
                }
            }
            else if (
                symbol.Kind == SymbolKind.Parameter && !lambdaParameters.Contains(identifierName)
            )
            {
                // This is a parameter from an outer scope
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    localVariables.Add(identifierName);
                }
            }
        }

        return localVariables;
    }

    private static bool IsPartOfMemberAccess(IdentifierNameSyntax identifier)
    {
        var parent = identifier.Parent;

        if (parent is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name == identifier;
        }

        if (parent is MemberBindingExpressionSyntax)
        {
            return true;
        }

        // Check if this is inside a NameEquals (property name in anonymous object)
        if (parent is NameEqualsSyntax)
        {
            return true;
        }

        return false;
    }
}
