using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects SelectExpr calls in API controllers that should have ProducesResponseType attribute.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrs002-producesresponsetypeanalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ProducesResponseTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRS002";

    private static readonly LocalizableString Title =
        "API method should have ProducesResponseType attribute";
    private static readonly LocalizableString MessageFormat =
        "Add [ProducesResponseType(typeof({0}), StatusCodes.Status200OK)] to the method";
    private static readonly LocalizableString Description =
        "API controller methods using SelectExpr should have ProducesResponseType attribute for better API documentation.";
    private const string Category = "Design";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrs002-producesresponsetypeanalyzer"
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

        // Check if this is a SelectExpr call with type arguments
        var dtoTypeName = GetDtoTypeNameFromSelectExpr(invocation.Expression);
        if (dtoTypeName == null)
        {
            return;
        }

        // Find the containing method
        var methodDecl = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl == null)
        {
            return;
        }

        // Find the containing class
        var classDecl = methodDecl.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl == null)
        {
            return;
        }

        // Check if the class has [ApiController] attribute
        if (!HasApiControllerAttribute(classDecl))
        {
            return;
        }

        // Check if the method already has ProducesResponseType with the correct type
        if (HasProducesResponseTypeAttribute(methodDecl, dtoTypeName))
        {
            return;
        }

        // Report diagnostic on the method name
        var diagnostic = Diagnostic.Create(Rule, methodDecl.Identifier.GetLocation(), dtoTypeName);
        context.ReportDiagnostic(diagnostic);
    }

    private static string? GetDtoTypeNameFromSelectExpr(ExpressionSyntax expression)
    {
        // Check for SelectExpr<TSource, TDto>
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (
                memberAccess.Name is GenericNameSyntax genericName
                && genericName.Identifier.Text == "SelectExpr"
                && genericName.TypeArgumentList.Arguments.Count >= 2
            )
            {
                // Return the second type argument (TDto)
                return genericName.TypeArgumentList.Arguments[1].ToString();
            }
        }

        return null;
    }

    private static bool HasApiControllerAttribute(ClassDeclarationSyntax classDecl)
    {
        foreach (var attributeList in classDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = GetAttributeName(attribute);
                if (name == "ApiController" || name == "ApiControllerAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasProducesResponseTypeAttribute(
        MethodDeclarationSyntax methodDecl,
        string dtoTypeName
    )
    {
        foreach (var attributeList in methodDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = GetAttributeName(attribute);
                if (name == "ProducesResponseType" || name == "ProducesResponseTypeAttribute")
                {
                    // Check if it has the correct type argument
                    if (attribute.ArgumentList != null)
                    {
                        foreach (var arg in attribute.ArgumentList.Arguments)
                        {
                            // Check for typeof(DtoType)
                            if (arg.Expression is TypeOfExpressionSyntax typeOf)
                            {
                                var typeName = typeOf.Type.ToString();
                                if (typeName == dtoTypeName)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private static string GetAttributeName(AttributeSyntax attribute)
    {
        return attribute.Name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => string.Empty,
        };
    }
}
