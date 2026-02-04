using System.Linq;
using Linqraft.Core.AnalyzerHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects void/Task methods with Select using anonymous types that can be converted to API response methods.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ApiResponseMethodGeneratorAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRF003";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "Method can be converted to API response method",
        "Method '{0}' can be converted to an async API response method",
        "Design",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "This void/Task method contains a Select with anonymous type that can be converted to an async API response method.",
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
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        // Check if the method has void or Task return type
        if (!IsVoidOrTaskReturnType(methodDeclaration, context.SemanticModel))
        {
            return;
        }

        // Find Select calls with anonymous types that are not assigned to variables
        var selectInvocation = FindUnassignedSelectWithAnonymousType(methodDeclaration);
        if (selectInvocation == null)
        {
            return;
        }

        // Verify it's on IQueryable
        if (!IsIQueryableSelect(selectInvocation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        // Report diagnostic at the method identifier location
        var diagnostic = Diagnostic.Create(
            RuleInstance,
            methodDeclaration.Identifier.GetLocation(),
            methodDeclaration.Identifier.Text
        );
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsVoidOrTaskReturnType(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel
    )
    {
        var returnTypeInfo = semanticModel.GetTypeInfo(methodDeclaration.ReturnType);
        var returnType = returnTypeInfo.Type;

        if (returnType == null)
        {
            return false;
        }

        // Check for void
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        // Check for Task (non-generic)
        if (
            returnType.Name == "Task"
            && returnType is INamedTypeSymbol namedType
            && !namedType.IsGenericType
        )
        {
            return true;
        }

        return false;
    }

    private static InvocationExpressionSyntax? FindUnassignedSelectWithAnonymousType(
        MethodDeclarationSyntax methodDeclaration
    )
    {
        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
        {
            return null;
        }

        // Find all invocation expressions in the method
        var invocations = methodDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            // Check if this is a Select call
            if (!IsSelectInvocation(invocation.Expression))
            {
                continue;
            }

            // Check if the lambda contains an anonymous type
            var anonymousType = FindAnonymousTypeInArguments(invocation.ArgumentList);
            if (anonymousType == null)
            {
                continue;
            }

            // Check if the invocation is not assigned to a variable
            // The invocation should be in an expression statement (standalone)
            // or in an await expression that is in an expression statement
            if (IsUnassignedInvocation(invocation))
            {
                return invocation;
            }
        }

        return null;
    }

    private static bool IsUnassignedInvocation(InvocationExpressionSyntax invocation)
    {
        var parent = invocation.Parent;

        // Handle await expression
        if (parent is AwaitExpressionSyntax awaitExpr)
        {
            parent = awaitExpr.Parent;
        }

        // Check if it's in an expression statement (standalone)
        return parent is ExpressionStatementSyntax;
    }

    private static bool IsSelectInvocation(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                => memberAccess.Name.Identifier.Text == "Select",
            IdentifierNameSyntax identifier => identifier.Identifier.Text == "Select",
            _ => false,
        };
    }

    private static bool IsIQueryableSelect(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
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

        // Check if it's IQueryable<T> or implements IQueryable<T>
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
