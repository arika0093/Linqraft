using System;
using System.Linq;
using System.Threading;
using Linqraft.Core.Configuration;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

internal static class ProjectionHookSyntaxHelper
{
    public static bool TryGetHookInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        LinqraftGeneratorOptionsCore generatorOptions,
        string? projectionHelperParameterName,
        string? projectionHelperParameterTypeName,
        LinqraftProjectionHookKind kind,
        out HookInvocationInfo hookInvocation,
        CancellationToken cancellationToken = default
    )
    {
        hookInvocation = default;

        var hook = generatorOptions.FindProjectionHook(GetInvocationName(invocation.Expression));
        if (hook?.Kind != kind)
        {
            return false;
        }

        if (
            TryGetHelperTarget(
                invocation,
                semanticModel,
                projectionHelperParameterName,
                projectionHelperParameterTypeName,
                out var helperTarget,
                cancellationToken
            )
        )
        {
            hookInvocation = new HookInvocationInfo
            {
                Hook = hook,
                Invocation = invocation,
                TargetExpression = helperTarget,
                GenericTypeArgument = GetSingleGenericTypeArgument(invocation.Expression),
            };
            return true;
        }

        return false;
    }

    public static bool TryGetProjectedValueSelection(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        LinqraftGeneratorOptionsCore generatorOptions,
        string? projectionHelperParameterName,
        string? projectionHelperParameterTypeName,
        out ProjectedValueSelectionInfo selectionInfo,
        CancellationToken cancellationToken = default
    )
    {
        selectionInfo = default;
        if (
            !string.Equals(
                GetInvocationName(invocation.Expression),
                "Select",
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        if (
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Expression is not InvocationExpressionSyntax projectInvocation
            || !TryGetHookInvocation(
                projectInvocation,
                semanticModel,
                generatorOptions,
                projectionHelperParameterName,
                projectionHelperParameterTypeName,
                LinqraftProjectionHookKind.Project,
                out var hookInvocation,
                cancellationToken
            )
        )
        {
            return false;
        }

        var selector = invocation
            .ArgumentList.Arguments.Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
        var selectorBody = GetLambdaBody(selector);
        if (selector is null || selectorBody is null)
        {
            return false;
        }

        selectionInfo = new ProjectedValueSelectionInfo
        {
            SelectionInvocation = invocation,
            ProjectInvocation = hookInvocation,
            Selector = selector,
            SelectorBody = selectorBody,
        };
        return true;
    }

    private static bool TryGetHelperTarget(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string? projectionHelperParameterName,
        string? projectionHelperParameterTypeName,
        out ExpressionSyntax targetExpression,
        CancellationToken cancellationToken = default
    )
    {
        targetExpression = null!;
        if (projectionHelperParameterName is null)
        {
            return false;
        }

        if (
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Expression is not IdentifierNameSyntax identifier
            || invocation.ArgumentList.Arguments.Count != 1
        )
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(identifier, cancellationToken);
        var parameterSymbol =
            symbolInfo.Symbol as IParameterSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IParameterSymbol>().FirstOrDefault();
        if (parameterSymbol is null)
        {
            return false;
        }

        if (
            !string.Equals(
                parameterSymbol.Name,
                projectionHelperParameterName,
                StringComparison.Ordinal
            )
            || (
                projectionHelperParameterTypeName is not null
                && !string.Equals(
                    parameterSymbol.Type.ToFullyQualifiedTypeName(),
                    projectionHelperParameterTypeName,
                    StringComparison.Ordinal
                )
            )
        )
        {
            return false;
        }

        targetExpression = invocation.ArgumentList.Arguments[0].Expression;
        return true;
    }

    private static string GetInvocationName(ExpressionSyntax expression)
    {
        return GetInvocationNameSyntax(expression)?.Identifier.ValueText ?? string.Empty;
    }

    private static SimpleNameSyntax? GetInvocationNameSyntax(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            GenericNameSyntax genericName => genericName,
            _ => null,
        };
    }

    private static TypeSyntax? GetSingleGenericTypeArgument(ExpressionSyntax expression)
    {
        return GetInvocationNameSyntax(expression) is GenericNameSyntax genericName
            && genericName.TypeArgumentList.Arguments.Count == 1
            ? genericName.TypeArgumentList.Arguments[0]
            : null;
    }

    private static ExpressionSyntax? GetLambdaBody(LambdaExpressionSyntax? lambda)
    {
        return lambda?.Body as ExpressionSyntax;
    }

    internal readonly record struct HookInvocationInfo
    {
        public required LinqraftProjectionHookDefinition Hook { get; init; }

        public required InvocationExpressionSyntax Invocation { get; init; }

        public required ExpressionSyntax TargetExpression { get; init; }

        public required TypeSyntax? GenericTypeArgument { get; init; }
    }

    internal readonly record struct ProjectedValueSelectionInfo
    {
        public required InvocationExpressionSyntax SelectionInvocation { get; init; }

        public required HookInvocationInfo ProjectInvocation { get; init; }

        public required LambdaExpressionSyntax Selector { get; init; }

        public required ExpressionSyntax SelectorBody { get; init; }
    }
}
