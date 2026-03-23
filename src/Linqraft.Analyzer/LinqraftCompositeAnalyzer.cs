using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LinqraftCompositeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        DiagnosticDescriptors.All;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeConditionalExpression,
            SyntaxKind.ConditionalExpression
        );
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(
            AnalyzeExplicitGeneratedDtoUsage,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.VariableDeclaration,
            SyntaxKind.Parameter,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.FieldDeclaration
        );
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        AnalyzeAnonymousCapturePattern(context, invocation);

        if (AnalyzerHelpers.IsSelectExprInvocation(invocation))
        {
            AnalyzeSelectExprInvocation(context, invocation);
            return;
        }

        if (
            !AnalyzerHelpers.IsQueryableSelectInvocation(
                invocation,
                context.SemanticModel,
                context.CancellationToken
            )
        )
        {
            return;
        }

        var lambda = AnalyzerHelpers.GetSelectorLambda(invocation);
        if (lambda is null)
        {
            return;
        }

        var containsNullTernary =
            AnalyzerHelpers.GetLambdaExpressionBody(lambda) is { } selectorBody
            && AnalyzerHelpers.ContainsSimplifiableNullCheckTernary(selectorBody);

        if (AnalyzerHelpers.IsAnonymousProjection(lambda) && containsNullTernary)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.SelectToSelectExprAnonymousWithNullTernary,
                    invocation.GetLocation()
                )
            );
            return;
        }

        if (AnalyzerHelpers.IsAnonymousProjection(lambda))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.SelectToSelectExprAnonymous,
                    invocation.GetLocation()
                )
            );
            return;
        }

        if (AnalyzerHelpers.IsNamedProjection(lambda) && containsNullTernary)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.SelectToSelectExprNamedWithNullTernary,
                    invocation.GetLocation()
                )
            );
            return;
        }

        if (AnalyzerHelpers.IsNamedProjection(lambda))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.SelectToSelectExprNamed,
                    invocation.GetLocation()
                )
            );
        }
    }

    private static void AnalyzeAnonymousCapturePattern(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var captureArgument = AnalyzerHelpers.GetCaptureArgument(invocation);
        if (captureArgument?.Expression is not AnonymousObjectCreationExpressionSyntax)
        {
            return;
        }

        var methodSymbol =
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            as IMethodSymbol;
        var reducedMethod = methodSymbol?.ReducedFrom ?? methodSymbol;
        if (
            reducedMethod?.Name
                is not "SelectExpr"
                    and not "Select"
                    and not "SelectManyExpr"
                    and not "GroupByExpr"
                    and not "Generate"
            || reducedMethod.ContainingNamespace.ToDisplayString() != "Linqraft"
        )
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.AnonymousCaptureDelegatePattern,
                captureArgument.Expression.GetLocation()
            )
        );
    }

    private static void AnalyzeSelectExprInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        var lambda = AnalyzerHelpers.GetSelectorLambda(invocation);
        if (lambda is null)
        {
            return;
        }

        var selectorBody = AnalyzerHelpers.GetLambdaExpressionBody(lambda);
        if (
            selectorBody is AnonymousObjectCreationExpressionSyntax
            && AnalyzerHelpers.GetInvocationNameSyntax(invocation.Expression)
                is not GenericNameSyntax
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(DiagnosticDescriptors.SelectExprToTyped, invocation.GetLocation())
            );
        }

        var outerReferences = AnalyzerHelpers
            .CollectOuterReferences(lambda, context.SemanticModel, context.CancellationToken)
            .Distinct(SymbolEqualityComparer.Default)
            .ToList();
        var capturedNames = new HashSet<string>(
            AnalyzerHelpers.GetCaptureNames(invocation),
            System.StringComparer.Ordinal
        );
        var outerReferenceNames = new HashSet<string>(
            outerReferences.Select(reference => reference.Name),
            System.StringComparer.Ordinal
        );

        foreach (
            var referenceName in outerReferences
                .Where(reference => !capturedNames.Contains(reference.Name))
                .Select(reference => reference.Name)
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.LocalVariableCapture,
                    invocation.GetLocation(),
                    properties: ImmutableDictionary<string, string?>.Empty.Add(
                        "CaptureName",
                        referenceName
                    ),
                    messageArgs: new object[] { referenceName }
                )
            );
        }

        foreach (
            var captureName in capturedNames.Where(captureName =>
                !outerReferenceNames.Contains(captureName)
            )
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnnecessaryCapture,
                    invocation.GetLocation(),
                    properties: ImmutableDictionary<string, string?>.Empty.Add(
                        "CaptureName",
                        captureName
                    ),
                    messageArgs: new object[] { captureName }
                )
            );
        }

        if (FlowsFromAnonymousGroupBy(invocation))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.GroupByAnonymousKey,
                    invocation.GetLocation()
                )
            );
        }
    }

    private static void AnalyzeConditionalExpression(SyntaxNodeAnalysisContext context)
    {
        var conditionalExpression = (ConditionalExpressionSyntax)context.Node;
        if (
            !conditionalExpression
                .Ancestors()
                .OfType<InvocationExpressionSyntax>()
                .Any(AnalyzerHelpers.IsSelectExprInvocation)
        )
        {
            return;
        }

        if (AnalyzerHelpers.ContainsNullCheckedObjectTernary(conditionalExpression))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.TernaryNullCheckToConditional,
                    conditionalExpression.GetLocation()
                )
            );
        }
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;
        if (usingDirective.Name is null)
        {
            return;
        }

        var namespaceName = usingDirective.Name.ToString();
        if (AnalyzerHelpers.IsHashNamespace(namespaceName))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.GeneratedHashedNamespaceUsage,
                    usingDirective.GetLocation(),
                    namespaceName
                )
            );
        }
    }

    private static void AnalyzeExplicitGeneratedDtoUsage(SyntaxNodeAnalysisContext context)
    {
        ITypeSymbol? typeSymbol = context.Node switch
        {
            ObjectCreationExpressionSyntax objectCreation => context
                .SemanticModel.GetTypeInfo(objectCreation, context.CancellationToken)
                .Type,
            VariableDeclarationSyntax declaration when declaration.Type.IsVar => null,
            VariableDeclarationSyntax declaration => context
                .SemanticModel.GetTypeInfo(declaration.Type, context.CancellationToken)
                .Type,
            ParameterSyntax parameter when parameter.Type is not null => context
                .SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken)
                .Type,
            ParameterSyntax => null,
            PropertyDeclarationSyntax property => context
                .SemanticModel.GetTypeInfo(property.Type, context.CancellationToken)
                .Type,
            FieldDeclarationSyntax field => context
                .SemanticModel.GetTypeInfo(field.Declaration.Type, context.CancellationToken)
                .Type,
            _ => null,
        };

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        if (
            !namedType
                .GetAttributes()
                .Any(attribute =>
                    attribute.AttributeClass?.Name == "LinqraftAutoGeneratedDtoAttribute"
                )
        )
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.AutoGeneratedDtoUsage,
                context.Node.GetLocation(),
                namedType.Name
            )
        );
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body is null && method.ExpressionBody is null)
        {
            return;
        }

        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(
            method,
            context.CancellationToken
        );
        if (methodSymbol is null)
        {
            return;
        }

        AnalyzeApiControllerMethod(context, method, methodSymbol);
    }

    private static void AnalyzeApiControllerMethod(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax method,
        IMethodSymbol methodSymbol
    )
    {
        var containingType = methodSymbol.ContainingType;
        if (
            containingType is null
            || !containingType
                .GetAttributes()
                .Any(attribute => attribute.AttributeClass?.Name == "ApiControllerAttribute")
        )
        {
            return;
        }

        var returnTypeName = methodSymbol.ReturnType.ToDisplayString();
        if (
            returnTypeName
            is not "Microsoft.AspNetCore.Mvc.IActionResult"
                and not "Microsoft.AspNetCore.Mvc.ActionResult"
        )
        {
            return;
        }

        if (
            method
                .AttributeLists.SelectMany(list => list.Attributes)
                .Any(attribute => attribute.Name.ToString().Contains("ProducesResponseType"))
        )
        {
            return;
        }

        var selectExpr = method
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                AnalyzerHelpers.IsSelectExprInvocation(invocation)
                && AnalyzerHelpers.GetInvocationNameSyntax(invocation.Expression)
                    is GenericNameSyntax genericName
                && genericName.TypeArgumentList.Arguments.Count >= 1
            );

        if (selectExpr is null)
        {
            return;
        }

        var generic = (GenericNameSyntax)
            AnalyzerHelpers.GetInvocationNameSyntax(selectExpr.Expression)!;
        var dtoName = generic.TypeArgumentList.Arguments.Last().ToString();
        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.ApiControllerProducesResponseType,
                method.Identifier.GetLocation(),
                method.Identifier.ValueText,
                dtoName
            )
        );
    }

    private static bool FlowsFromAnonymousGroupBy(InvocationExpressionSyntax selectExpr)
    {
        SyntaxNode? current = selectExpr.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;

        while (current is InvocationExpressionSyntax invocation)
        {
            if (AnalyzerHelpers.GetInvocationName(invocation.Expression) == "GroupBy")
            {
                var lambda = AnalyzerHelpers.GetSelectorLambda(invocation);
                return lambda is not null
                    && AnalyzerHelpers.GetLambdaExpressionBody(lambda)
                        is AnonymousObjectCreationExpressionSyntax;
            }

            current = invocation.Expression is MemberAccessExpressionSyntax chainedAccess
                ? chainedAccess.Expression
                : null;
        }

        return false;
    }
}
