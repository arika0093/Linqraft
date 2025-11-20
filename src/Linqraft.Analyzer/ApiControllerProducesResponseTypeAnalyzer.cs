using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects SelectExpr usage in ApiController methods without ProducesResponseType attribute.
/// </summary>
/// <remarks>
/// See documentation: https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrf002-apicontrollerproducesresponsetypeanalyzer
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ApiControllerProducesResponseTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LQRF002";

    private static readonly LocalizableString Title =
        "Add ProducesResponseType to clarify API return type";
    private static readonly LocalizableString MessageFormat =
        "Add [ProducesResponseType] to clarify the return value of the API and assist in generating OpenAPI documentation";
    private static readonly LocalizableString Description =
        "When SelectExpr<T, TDto> is used in ApiController methods that return IActionResult, adding [ProducesResponseType] helps with OpenAPI documentation generation.";
    private const string Category = "Design";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/arika0093/Linqraft/blob/main/docs/Analyzers.md#lqrf002-apicontrollerproducesresponsetypeanalyzer"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        // Check if the containing class has [ApiController] attribute
        if (!IsInApiController(methodDeclaration, context.SemanticModel))
        {
            return;
        }

        // Check if the return type is IActionResult or similar
        if (!IsActionResultReturnType(methodDeclaration, context.SemanticModel))
        {
            return;
        }

        // Check if method already has [ProducesResponseType] attribute
        if (HasProducesResponseTypeAttribute(methodDeclaration, context.SemanticModel))
        {
            return;
        }

        // Check if method body contains SelectExpr with type arguments
        var selectExprInfo = FindSelectExprWithTypeArguments(methodDeclaration);
        if (selectExprInfo == null)
        {
            return;
        }

        // Report diagnostic at the SelectExpr invocation location
        var diagnostic = Diagnostic.Create(
            Rule,
            selectExprInfo.Value.Invocation.GetLocation(),
            selectExprInfo.Value.DtoTypeName
        );
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsInApiController(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel
    )
    {
        var classDeclaration = methodDeclaration
            .Ancestors()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();
        if (classDeclaration == null)
        {
            return false;
        }

        // Check for [ApiController] attribute on the class
        foreach (var attributeList in classDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeSymbol = semanticModel.GetSymbolInfo(attribute).Symbol;
                if (attributeSymbol is IMethodSymbol methodSymbol)
                {
                    var attributeClass = methodSymbol.ContainingType;
                    if (attributeClass?.Name == "ApiControllerAttribute")
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsActionResultReturnType(
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

        // Check if return type is IActionResult
        // Do NOT report for ActionResult<T> because it's already typed
        var typeName = returnType.Name;
        if (typeName == "IActionResult")
        {
            return true;
        }

        // Check if it's untyped ActionResult (not ActionResult<T>)
        if (typeName == "ActionResult" && returnType is INamedTypeSymbol namedType)
        {
            // If it's generic ActionResult<T>, don't report (it's already typed)
            return !namedType.IsGenericType;
        }

        return false;
    }

    private static bool HasProducesResponseTypeAttribute(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel
    )
    {
        // First try semantic model approach (more reliable)
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol != null)
        {
            foreach (var attribute in methodSymbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass != null)
                {
                    var attributeName = attributeClass.Name;
                    if (
                        attributeName == "ProducesResponseTypeAttribute" // Semantic model includes the "Attribute" suffix
                        || attributeName == "ProducesResponseType"
                    )
                    {
                        return true;
                    }
                }
            }
        }

        // Fallback to syntax-based check
        foreach (var attributeList in methodDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name;

                // Extract the identifier name
                var identifierText = name switch
                {
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    QualifiedNameSyntax qualified => qualified.Right is IdentifierNameSyntax rightId
                        ? rightId.Identifier.ValueText
                        : null,
                    _ => null,
                };

                if (identifierText is "ProducesResponseType" or "ProducesResponseTypeAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SelectExprInfo? FindSelectExprWithTypeArguments(
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
            // Check if this is a SelectExpr call with type arguments
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (
                    memberAccess.Name is GenericNameSyntax genericName
                    && genericName.Identifier.Text == "SelectExpr"
                    && genericName.TypeArgumentList.Arguments.Count >= 2
                )
                {
                    // Get the DTO type name (second type argument)
                    var dtoType = genericName.TypeArgumentList.Arguments[1];
                    return new SelectExprInfo
                    {
                        DtoTypeName = dtoType.ToString(),
                        Invocation = invocation,
                    };
                }
            }
        }

        return null;
    }

    private struct SelectExprInfo
    {
        public string DtoTypeName { get; set; }
        public InvocationExpressionSyntax Invocation { get; set; }
    }
}
