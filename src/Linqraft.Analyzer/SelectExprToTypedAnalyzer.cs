using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects SelectExpr calls without type arguments that can be converted to typed versions
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SelectExprToTypedAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRS001";

    private static readonly LocalizableString Title =
        "SelectExpr can be converted to typed version";
    private static readonly LocalizableString MessageFormat =
        "SelectExpr can be converted to SelectExpr<{0}, {1}>";
    private static readonly LocalizableString Description =
        "This SelectExpr call can be converted to use explicit type arguments for better type safety.";
    private const string Category = "Design";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression
        );
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a SelectExpr call without type arguments
        if (!IsSelectExprWithoutTypeArguments(invocation.Expression))
        {
            return;
        }

        // Check if the lambda contains an anonymous type
        var anonymousType = FindAnonymousTypeInArguments(invocation.ArgumentList);
        if (anonymousType == null)
        {
            return;
        }

        // Get the source type from the expression
        var semanticModel = context.SemanticModel;
        var sourceType = GetSourceType(invocation.Expression, semanticModel, context.CancellationToken);
        if (sourceType == null)
        {
            return;
        }

        // Generate DTO name based on context
        var dtoName = GenerateDtoName(invocation, anonymousType);

        // Get the location of the method name (SelectExpr)
        var location = GetMethodNameLocation(invocation.Expression);

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            location,
            sourceType.Name,
            dtoName
        );
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

    private static bool IsSelectExprWithoutTypeArguments(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                // Check if it's SelectExpr and NOT a generic name (no type arguments)
                return memberAccess.Name.Identifier.Text == "SelectExpr"
                    && memberAccess.Name is not GenericNameSyntax;

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text == "SelectExpr";

            default:
                return false;
        }
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

    private static ITypeSymbol? GetSourceType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        // Get the type of the expression before .SelectExpr()
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
        var type = typeInfo.Type;

        if (type == null)
        {
            return null;
        }

        // Extract the element type from IQueryable<T> or IEnumerable<T>
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var typeArguments = namedType.TypeArguments;
            if (typeArguments.Length > 0)
            {
                return typeArguments[0];
            }
        }

        return null;
    }

    private static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousType
    )
    {
        string baseName;

        // Try to infer a name from the context
        // Walk up the tree to find the relevant context
        var current = invocation.Parent;
        while (current != null)
        {
            switch (current)
            {
                // Check for variable declaration: var name = query.SelectExpr(...)
                case EqualsValueClauseSyntax equalsValue
                    when equalsValue.Parent is VariableDeclaratorSyntax declarator:
                    var varName = declarator.Identifier.Text;
                    baseName = ToPascalCase(varName) + "Dto";
                    return baseName + "_" + GenerateHash(anonymousType);

                // Check for assignment: name = query.SelectExpr(...)
                case AssignmentExpressionSyntax assignment
                    when assignment.Left is IdentifierNameSyntax identifier:
                    baseName = ToPascalCase(identifier.Identifier.Text) + "Dto";
                    return baseName + "_" + GenerateHash(anonymousType);

                // Stop at statement level
                case StatementSyntax:
                    break;

                default:
                    current = current.Parent;
                    continue;
            }
            break;
        }

        // Check for return statement with method name
        var methodDecl = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl != null)
        {
            var methodName = methodDecl.Identifier.Text;
            // Remove Get prefix if present
            if (methodName.StartsWith("Get"))
            {
                methodName = methodName.Substring(3);
            }
            baseName = methodName + "Dto";
            return baseName + "_" + GenerateHash(anonymousType);
        }

        // Default fallback
        return "ResultDto_" + GenerateHash(anonymousType);
    }

    private static string GenerateHash(AnonymousObjectCreationExpressionSyntax anonymousType)
    {
        // Generate hash based on property names
        var sb = new StringBuilder();
        foreach (var initializer in anonymousType.Initializers)
        {
            string propertyName;
            if (initializer.NameEquals != null)
            {
                propertyName = initializer.NameEquals.Name.Identifier.Text;
            }
            else
            {
                propertyName = GetPropertyNameFromExpression(initializer.Expression);
            }
            sb.Append(propertyName);
            sb.Append(';');
        }

        // Create a deterministic hash using FNV-1a algorithm
        var str = sb.ToString();
        uint hash = 2166136261;
        foreach (char c in str)
        {
            hash ^= c;
            hash *= 16777619;
        }

        var hashString = new StringBuilder(8);

        // Convert to uppercase letters and digits (A-Z, 0-9)
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        for (int i = 0; i < 8; i++)
        {
            hashString.Append(chars[(int)(hash % chars.Length)]);
            hash /= (uint)chars.Length;
        }

        return hashString.ToString();
    }

    private static string GetPropertyNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding =>
                memberBinding.Name.Identifier.Text,
            _ => "Property",
        };
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
