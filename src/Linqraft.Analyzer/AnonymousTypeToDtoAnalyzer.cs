using System.Collections.Immutable;
using Linqraft.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects anonymous type usages that can be converted to DTOs.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrf001-anonymoustypetodtoanalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AnonymousTypeToDtoAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRF001";

    private static readonly LocalizableString Title = "Anonymous type can be converted to DTO";
    private static readonly LocalizableString MessageFormat =
        "Anonymous type can be converted to a DTO class";
    private static readonly LocalizableString Description =
        "This anonymous type can be converted to a strongly-typed DTO class for better type safety and reusability.";
    private const string Category = "Design";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{DiagnosticId}.md"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeAnonymousObjectCreation,
            SyntaxKind.AnonymousObjectCreationExpression
        );
    }

    private static void AnalyzeAnonymousObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var anonymousObject = (AnonymousObjectCreationExpressionSyntax)context.Node;

        // Skip if it has no initializers (empty anonymous type)
        if (anonymousObject.Initializers.Count == 0)
        {
            return;
        }

        // Check if this is in a context where conversion makes sense
        // (variable declaration, return statement, assignment, etc.)
        if (!IsInConvertibleContext(anonymousObject))
        {
            return;
        }

        // Skip if this is inside a SelectExpr call (Linqraft handles these)
        if (IsInsideSelectExprCall(anonymousObject))
        {
            return;
        }

        // Get the semantic model and check if we can analyze this
        var semanticModel = context.SemanticModel;
        var typeInfo = semanticModel.GetTypeInfo(anonymousObject, context.CancellationToken);

        if (typeInfo.Type == null || !typeInfo.Type.IsAnonymousType)
        {
            return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(Rule, anonymousObject.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsInConvertibleContext(
        AnonymousObjectCreationExpressionSyntax anonymousObject
    )
    {
        var parent = anonymousObject.Parent;

        // Direct contexts where conversion makes sense
        while (parent != null)
        {
            switch (parent)
            {
                // var result = new { ... }
                case EqualsValueClauseSyntax:
                    return true;

                // return new { ... }
                case ReturnStatementSyntax:
                    return true;

                // yield return new { ... }
                case YieldStatementSyntax:
                    return true;

                // x = new { ... }
                case AssignmentExpressionSyntax:
                    return true;

                // Method argument: Method(new { ... })
                case ArgumentSyntax:
                    return true;

                // Array initializer: new[] { new { ... } }
                case InitializerExpressionSyntax:
                    return true;

                // Conditional: condition ? new { ... } : other
                case ConditionalExpressionSyntax:
                    return true;

                // Lambda body: x => new { ... }
                case SimpleLambdaExpressionSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                    return true;

                // Skip through parentheses and continue checking
                case ParenthesizedExpressionSyntax:
                    parent = parent.Parent;
                    continue;

                default:
                    // If we hit a statement or declaration that's not in our list, stop
                    if (parent is StatementSyntax or MemberDeclarationSyntax)
                    {
                        return false;
                    }
                    parent = parent.Parent;
                    continue;
            }
        }

        return false;
    }

    private static bool IsInsideSelectExprCall(
        AnonymousObjectCreationExpressionSyntax anonymousObject
    )
    {
        var current = anonymousObject.Parent;

        while (current != null)
        {
            // Check if we're inside an invocation expression
            if (current is InvocationExpressionSyntax invocation)
            {
                // Check if it's a SelectExpr call with type arguments
                if (IsSelectExprWithTypeArguments(invocation.Expression))
                {
                    return true;
                }
            }

            // Stop at method/property declarations
            if (current is MemberDeclarationSyntax)
            {
                break;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsSelectExprWithTypeArguments(ExpressionSyntax expression)
    {
        // Get the method name and check for type arguments
        switch (expression)
        {
            // obj.SelectExpr<T, TDto>(...)
            case MemberAccessExpressionSyntax memberAccess:
                if (
                    memberAccess.Name.Identifier.Text == SelectExprHelper.MethodName
                    && memberAccess.Name is GenericNameSyntax genericName
                    && genericName.TypeArgumentList.Arguments.Count >= 2
                )
                {
                    return true;
                }
                break;

            // SelectExpr<T, TDto>(...) - unlikely but handle it
            case GenericNameSyntax genericIdentifier:
                if (
                    genericIdentifier.Identifier.Text == SelectExprHelper.MethodName
                    && genericIdentifier.TypeArgumentList.Arguments.Count >= 2
                )
                {
                    return true;
                }
                break;
        }

        return false;
    }

    private static string? GetMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            // obj.Method()
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            // Method()
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            // obj.Method<T>()
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            _ => null,
        };
    }
}
