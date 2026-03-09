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
            AnalyzeAnonymousObject,
            SyntaxKind.AnonymousObjectCreationExpression
        );
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

        foreach (var reference in outerReferences)
        {
            if (capturedNames.Contains(reference.Name))
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.LocalVariableCapture,
                    invocation.GetLocation(),
                    properties: ImmutableDictionary<string, string?>.Empty.Add(
                        "CaptureName",
                        reference.Name
                    ),
                    messageArgs: new object[] { reference.Name }
                )
            );
        }

        foreach (var captureName in capturedNames)
        {
            if (!outerReferences.Any(reference => reference.Name == captureName))
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

    private static void AnalyzeAnonymousObject(SyntaxNodeAnalysisContext context)
    {
        var anonymousObject = (AnonymousObjectCreationExpressionSyntax)context.Node;
        if (anonymousObject.Initializers.Count == 0)
        {
            return;
        }

        if (
            anonymousObject
                .Ancestors()
                .OfType<InvocationExpressionSyntax>()
                .Any(AnalyzerHelpers.IsSelectExprInvocation)
        )
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.AnonymousTypeToDto,
                anonymousObject.GetLocation()
            )
        );
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
        AnalyzeAsyncApiResponseMethod(context, method, methodSymbol);
        AnalyzeSyncApiResponseMethod(context, method, methodSymbol);
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
                && genericName.TypeArgumentList.Arguments.Count >= 2
            );

        if (selectExpr is null)
        {
            return;
        }

        var generic = (GenericNameSyntax)
            AnalyzerHelpers.GetInvocationNameSyntax(selectExpr.Expression)!;
        var dtoName = generic.TypeArgumentList.Arguments[1].ToString();
        context.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.ApiControllerProducesResponseType,
                method.Identifier.GetLocation(),
                method.Identifier.ValueText,
                dtoName
            )
        );
    }

    private static void AnalyzeAsyncApiResponseMethod(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax method,
        IMethodSymbol methodSymbol
    )
    {
        var returnType = methodSymbol.ReturnType;
        if (
            returnType.SpecialType != SpecialType.System_Void
            && !(
                returnType is INamedTypeSymbol namedType
                && namedType.Name == "Task"
                && namedType.TypeArguments.Length == 0
            )
        )
        {
            return;
        }

        var expressionStatement = method
            .DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Select(statement => statement.Expression)
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                AnalyzerHelpers.IsQueryableSelectInvocation(
                    invocation,
                    context.SemanticModel,
                    context.CancellationToken
                )
                && AnalyzerHelpers.GetSelectorLambda(invocation) is { } lambda
                && AnalyzerHelpers.IsAnonymousProjection(lambda)
                && AnalyzerHelpers.IsDbSet(
                    context
                        .SemanticModel.GetTypeInfo(
                            ((MemberAccessExpressionSyntax)invocation.Expression).Expression,
                            context.CancellationToken
                        )
                        .Type
                )
            );

        if (expressionStatement is not null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.AsyncApiResponseMethod,
                    method.Identifier.GetLocation(),
                    method.Identifier.ValueText
                )
            );
        }
    }

    private static void AnalyzeSyncApiResponseMethod(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax method,
        IMethodSymbol methodSymbol
    )
    {
        if (methodSymbol.ReturnsVoid is false)
        {
            return;
        }

        var expressionStatement = method
            .DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Select(statement => statement.Expression)
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                AnalyzerHelpers.IsQueryableSelectInvocation(
                    invocation,
                    context.SemanticModel,
                    context.CancellationToken
                )
                && AnalyzerHelpers.GetSelectorLambda(invocation) is { } lambda
                && AnalyzerHelpers.IsAnonymousProjection(lambda)
                && !AnalyzerHelpers.IsDbSet(
                    context
                        .SemanticModel.GetTypeInfo(
                            ((MemberAccessExpressionSyntax)invocation.Expression).Expression,
                            context.CancellationToken
                        )
                        .Type
                )
            );

        if (expressionStatement is not null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.SyncApiResponseMethod,
                    method.Identifier.GetLocation(),
                    method.Identifier.ValueText
                )
            );
        }
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
