using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Linqraft.Core.AnalyzerHelpers;
using Linqraft.Core.SyntaxHelpers;
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
        [LocalVariableCaptureAnalyzer.AnalyzerId];

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
        var lambdaParameters = LambdaHelper.GetLambdaParameterNames(lambda);

        // Find all variables that need to be captured (simple local variables)
        var simpleVariablesToCapture = CaptureHelper.FindSimpleVariablesToCapture(
            lambda,
            lambdaParameters,
            semanticModel
        );

        // Find member accesses that need to be captured with local variable declarations
        var memberAccessesToCapture = FindMemberAccessesToCapture(
            lambda,
            lambdaParameters,
            semanticModel
        );

        if (simpleVariablesToCapture.Count == 0 && memberAccessesToCapture.Count == 0)
            return document;

        // Get already captured variables
        var capturedVariables = CaptureHelper.GetCapturedVariables(invocation, semanticModel);

        // Generate unique captured variable names and create variable declarations for member accesses
        var captureDeclarations = new List<LocalDeclarationStatementSyntax>();
        var captureMapping = new Dictionary<ExpressionSyntax, string>();
        var allCaptureNames = new HashSet<string>(capturedVariables);

        // Add simple local variables to capture (without creating declarations)
        allCaptureNames.UnionWith(simpleVariablesToCapture);

        // Process member accesses - create local variable declarations
        foreach (var (expr, memberName) in memberAccessesToCapture)
        {
            var capturedVarName = $"captured_{memberName}";

            // Ensure the name is unique
            int suffix = 1;
            while (allCaptureNames.Contains(capturedVarName))
            {
                capturedVarName = $"captured_{memberName}{suffix}";
                suffix++;
            }

            allCaptureNames.Add(capturedVarName);
            captureMapping[expr] = capturedVarName;

            // Create local variable declaration: var captured_X = expr;
            var declaration = SyntaxFactory
                .LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("var"),
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(
                                SyntaxFactory.Identifier(capturedVarName),
                                null,
                                SyntaxFactory.EqualsValueClause(expr)
                            )
                        )
                    )
                )
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.LineFeed);

            captureDeclarations.Add(declaration);
        }

        // Replace member access expressions in the lambda with captured variable names
        var newLambda =
            captureMapping.Count > 0
                ? lambda.ReplaceNodes(
                    captureMapping.Keys,
                    (original, _) => SyntaxFactory.IdentifierName(captureMapping[original])
                )
                : lambda;

        // Create capture argument with all variables (existing + new)
        var captureArgument = CaptureHelper.CreateCaptureArgument(allCaptureNames);

        // Update or add capture argument
        InvocationExpressionSyntax newInvocation;
        var existingCaptureArgIndex = FindCaptureArgumentIndex(invocation);

        // First, update the lambda if it changed
        var tempInvocation = invocation;
        if (newLambda != lambda)
        {
            var lambdaArgIndex = 0;
            for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
            {
                if (invocation.ArgumentList.Arguments[i].Expression is LambdaExpressionSyntax)
                {
                    lambdaArgIndex = i;
                    break;
                }
            }

            var updatedLambdaArg = invocation
                .ArgumentList.Arguments[lambdaArgIndex]
                .WithExpression(newLambda);
            var newArgumentList = ArgumentListHelper.ReplaceArgument(
                invocation.ArgumentList,
                lambdaArgIndex,
                updatedLambdaArg
            );
            tempInvocation = invocation.WithArgumentList(newArgumentList);
        }

        // Then, add or update the capture argument
        if (existingCaptureArgIndex >= 0)
        {
            var newArgumentList = ArgumentListHelper.ReplaceArgument(
                tempInvocation.ArgumentList,
                existingCaptureArgIndex,
                captureArgument
            );
            newInvocation = tempInvocation.WithArgumentList(newArgumentList);
        }
        else
        {
            var newArgumentList = ArgumentListHelper.AddArgument(
                tempInvocation.ArgumentList,
                captureArgument
            );
            newInvocation = tempInvocation.WithArgumentList(newArgumentList);
        }

        // Insert capture declarations before the invocation statement
        var invocationStatement = invocation
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault();
        if (invocationStatement != null && captureDeclarations.Count > 0)
        {
            var newRoot = root.ReplaceNode(invocation, newInvocation);

            // Find the statement again in the new tree
            var newInvocationStatement = newRoot
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(inv => SelectExprHelper.IsSelectExprInvocationSyntax(inv.Expression))
                .Select(inv => inv.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault())
                .FirstOrDefault(stmt => stmt != null);

            if (newInvocationStatement != null)
            {
                // Insert declarations before the statement
                var parentBlock = newInvocationStatement.Parent as BlockSyntax;
                if (parentBlock != null)
                {
                    var statementIndex = parentBlock.Statements.IndexOf(newInvocationStatement);
                    if (statementIndex >= 0)
                    {
                        // Get the indentation from the target statement
                        var targetIndentation = newInvocationStatement
                            .GetLeadingTrivia()
                            .Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia))
                            .LastOrDefault();

                        // Apply indentation to all capture declarations
                        var indentedDeclarations = captureDeclarations
                            .Select(decl => decl.WithLeadingTrivia(targetIndentation))
                            .ToList();

                        var newStatements = parentBlock.Statements.InsertRange(
                            statementIndex,
                            indentedDeclarations
                        );
                        var newBlock = parentBlock.WithStatements(newStatements);
                        newRoot = newRoot.ReplaceNode(parentBlock, newBlock);
                    }
                }
            }

            var documentWithNewRoot = document.WithSyntaxRoot(newRoot);
            return await CodeFixFormattingHelper
                .FormatAndNormalizeLineEndingsAsync(documentWithNewRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            // No capture declarations, just replace the invocation
            var newRoot = root.ReplaceNode(invocation, newInvocation);
            var documentWithNewRoot = document.WithSyntaxRoot(newRoot);
            return await CodeFixFormattingHelper
                .NormalizeLineEndingsOnlyAsync(documentWithNewRoot, cancellationToken)
                .ConfigureAwait(false);
        }
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

    private static List<(
        ExpressionSyntax Expression,
        string MemberName
    )> FindMemberAccessesToCapture(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var memberAccessesToCapture = new List<(ExpressionSyntax, string)>();
        var bodyExpression = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null,
        };

        if (bodyExpression == null)
        {
            return memberAccessesToCapture;
        }

        // Find member access expressions that need to be captured with local variables
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

            // Check if this member actually needs to be captured
            if (!CaptureHelper.NeedsCapture(symbol, lambda, lambdaParameters, semanticModel))
            {
                continue;
            }

            // Check if this is a field or property that needs to be captured
            if ((symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property))
            {
                // Check if it's 'this.Member' or 'Type.StaticMember' or 'instance.Member'
                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    memberAccessesToCapture.Add((memberAccess, memberName));
                }
                else if (symbol.IsStatic && memberAccess.Expression is IdentifierNameSyntax)
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    memberAccessesToCapture.Add((memberAccess, memberName));
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
                                memberAccessesToCapture.Add((memberAccess, memberName));
                            }
                        }
                    }
                }
            }
        }

        return memberAccessesToCapture;
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
                symbol.Kind == SymbolKind.Parameter
                && !lambdaParameters.Contains(identifierName)
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

        // Check if this is the left side of an assignment in an object initializer
        // e.g., in "new MyClass { Id = s.Id }", the left "Id" is the property being assigned
        if (parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier)
        {
            return true;
        }

        return false;
    }
}
