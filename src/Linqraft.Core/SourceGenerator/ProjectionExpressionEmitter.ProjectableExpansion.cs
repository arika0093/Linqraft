using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Emits C# expressions for the finalized projection model.
/// </summary>
internal sealed partial class ProjectionExpressionEmitter
{
    // Projectable helpers inline member bodies into the current selector before normal emission resumes.
    /// <summary>
    /// Builds lambda parameter bindings.
    /// </summary>
    private static Dictionary<ISymbol, ExpressionSyntax> BuildLambdaParameterBindings(
        LambdaExpressionSyntax lambda,
        SemanticModel declarationModel,
        ExpressionSyntax replacementExpression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var bindings = new Dictionary<ISymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);
        foreach (var parameter in GetLambdaParameters(lambda))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declarationModel.GetDeclaredSymbol(parameter, cancellationToken) is { } symbol)
            {
                bindings[symbol] = replacementExpression;
            }
        }

        return bindings;
    }

    /// <summary>
    /// Gets lambda parameters.
    /// </summary>
    private static IEnumerable<ParameterSyntax> GetLambdaParameters(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized
                .ParameterList
                .Parameters,
            _ => [],
        };
    }

    /// <summary>
    /// Gets readable projection properties.
    /// </summary>
    private static IReadOnlyList<IPropertySymbol> GetReadableProjectionProperties(
        INamedTypeSymbol sourceType
    )
    {
        return sourceType
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                !property.IsStatic
                && property.GetMethod is not null
                && property.Parameters.Length == 0
                && property.DeclaredAccessibility == Accessibility.Public
                && IsProjectionLeafType(property.Type)
            )
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Determines whether the type is a projection leaf type.
    /// </summary>
    private static bool IsProjectionLeafType(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_String
            || type.TypeKind == TypeKind.Enum
            || type.IsValueType;
    }

    /// <summary>
    /// Gets projection source type.
    /// </summary>
    private INamedTypeSymbol? GetProjectionSourceType(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var type = GetExpressionType(expression, cancellationToken);
        return type switch
        {
            INamedTypeSymbol namedType
                when namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T =>
                namedType.TypeArguments[0] as INamedTypeSymbol,
            INamedTypeSymbol namedType => namedType,
            _ => null,
        };
    }

    /// <summary>
    /// Handles wrap member access receiver.
    /// </summary>
    private static string WrapMemberAccessReceiver(string receiver)
    {
        if (
            !ContainsLineBreak(receiver)
            && (
                receiver.Length == 0
                || receiver.All(character =>
                    char.IsLetterOrDigit(character)
                    || character is '_' or '.' or '[' or ']' or '!' or '?'
                )
            )
        )
        {
            return receiver;
        }

        if (!ContainsLineBreak(receiver))
        {
            return $"({receiver})";
        }

        return string.Join("\n", "(", IndentAllLines(receiver), ")");
    }

    /// <summary>
    /// Removes nullable annotation.
    /// </summary>
    private static string RemoveNullableAnnotation(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName[..^1] : typeName;
    }

    /// <summary>
    /// Attempts to emit projectable hook.
    /// </summary>
    private bool TryEmitProjectableHook(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        return expression is InvocationExpressionSyntax invocation
            && TryEmitProjectableHook(
                invocation,
                overrideReceiver: null,
                out rewritten,
                cancellationToken
            );
    }

    /// <summary>
    /// Attempts to emit projectable hook.
    /// </summary>
    private bool TryEmitProjectableHook(
        InvocationExpressionSyntax invocation,
        string? overrideReceiver,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;

        // Projectable hooks replace a computed member call with its source expression so query
        // providers see the fully expanded tree instead of an opaque runtime invocation.
        if (
            !IsProjectionHookInvocation(
                invocation,
                LinqraftProjectionHookKind.Projectable,
                cancellationToken
            )
        )
        {
            return false;
        }

        if (
            !TryExpandProjectableInvocation(
                invocation,
                overrideReceiver,
                out var expanded,
                out var expandedSymbol,
                cancellationToken
            )
        )
        {
            return false;
        }

        if (expandedSymbol is not null && !_activeProjectableSymbols.Add(expandedSymbol))
        {
            throw new global::System.InvalidOperationException(
                $"Detected recursive projectable helper expansion for '{expandedSymbol.ToDisplayString()}'."
            );
        }

        try
        {
            rewritten = Emit(expanded, cancellationToken);
            return true;
        }
        finally
        {
            if (expandedSymbol is not null)
            {
                _activeProjectableSymbols.Remove(expandedSymbol);
            }
        }
    }

    /// <summary>
    /// Attempts to emit extension invocation.
    /// </summary>
    private bool TryEmitExtensionInvocation(
        InvocationExpressionSyntax invocation,
        SimpleNameSyntax methodName,
        string receiver,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (!BelongsToSemanticModel(invocation))
        {
            return false;
        }

        var symbolInfo = _semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var methodSymbol =
            symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        var reducedFrom = methodSymbol?.ReducedFrom;
        if (reducedFrom is null)
        {
            return false;
        }

        var arguments = invocation
            .ArgumentList.Arguments.Select(argument => EmitArgument(argument, cancellationToken))
            .ToList();
        arguments.Insert(0, receiver);
        rewritten = BuildInvocationExpression(
            $"{reducedFrom.ContainingType.ToFullyQualifiedTypeName()}.{EmitSimpleName(methodName)}",
            arguments,
            cancellationToken
        );
        return true;
    }

    /// <summary>
    /// Attempts to handle expand projectable invocation.
    /// </summary>
    private bool TryExpandProjectableInvocation(
        InvocationExpressionSyntax invocation,
        string? overrideReceiver,
        out ExpressionSyntax expanded,
        out ISymbol? expandedSymbol,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var targetExpression = GetHookTargetExpression(invocation, cancellationToken);
        if (targetExpression is null)
        {
            expanded = invocation;
            expandedSymbol = null;
            return false;
        }

        if (
            TryExpandProjectableProperty(
                targetExpression,
                overrideReceiver,
                out expanded,
                out expandedSymbol,
                cancellationToken
            )
        )
        {
            return true;
        }

        if (targetExpression is InvocationExpressionSyntax targetInvocation)
        {
            return TryExpandProjectableMethod(
                targetInvocation,
                overrideReceiver,
                out expanded,
                out expandedSymbol,
                cancellationToken
            );
        }

        expanded = invocation;
        expandedSymbol = null;
        return false;
    }

    /// <summary>
    /// Attempts to handle expand projectable property.
    /// </summary>
    private bool TryExpandProjectableProperty(
        ExpressionSyntax targetExpression,
        string? overrideReceiver,
        out ExpressionSyntax expanded,
        out ISymbol? expandedSymbol,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (!BelongsToSemanticModel(targetExpression))
        {
            expanded = targetExpression;
            expandedSymbol = null;
            return false;
        }

        var propertySymbol =
            _semanticModel.GetSymbolInfo(targetExpression, cancellationToken).Symbol
                as IPropertySymbol
            ?? _semanticModel
                .GetSymbolInfo(targetExpression, cancellationToken)
                .CandidateSymbols.OfType<IPropertySymbol>()
                .FirstOrDefault();
        if (propertySymbol?.IsStatic == true)
        {
            expanded = targetExpression;
            expandedSymbol = null;
            return false;
        }

        var propertySyntax =
            propertySymbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
            as PropertyDeclarationSyntax;
        var bodyExpression = GetProjectableBodyExpression(propertySyntax);
        if (bodyExpression is null)
        {
            expanded = targetExpression;
            expandedSymbol = null;
            return false;
        }

        var declarationModel = _semanticModel.Compilation.GetSemanticModel(
            bodyExpression.SyntaxTree
        );
        EnsureProjectableExpansionIsAcyclic(
            propertySymbol!,
            bodyExpression,
            declarationModel,
            new HashSet<ISymbol>(_activeProjectableSymbols, SymbolEqualityComparer.Default),
            cancellationToken
        );
        var receiverExpression = overrideReceiver is null
            ? GetProjectableReceiverExpression(targetExpression)
            : SyntaxFactory.ParseExpression(overrideReceiver);
        var rewriter = new ProjectableInliningRewriter(
            declarationModel,
            propertySymbol!.ContainingType,
            receiverExpression,
            parameterBindings: null
        );
        expanded =
            (ExpressionSyntax?)rewriter.Visit(bodyExpression.WithoutTrivia()) ?? bodyExpression;
        expandedSymbol = propertySymbol;
        return true;
    }

    /// <summary>
    /// Attempts to handle expand projectable method.
    /// </summary>
    private bool TryExpandProjectableMethod(
        InvocationExpressionSyntax targetInvocation,
        string? overrideReceiver,
        out ExpressionSyntax expanded,
        out ISymbol? expandedSymbol,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (!BelongsToSemanticModel(targetInvocation))
        {
            expanded = targetInvocation;
            expandedSymbol = null;
            return false;
        }

        var methodSymbol =
            _semanticModel.GetSymbolInfo(targetInvocation, cancellationToken).Symbol
                as IMethodSymbol
            ?? _semanticModel
                .GetSymbolInfo(targetInvocation, cancellationToken)
                .CandidateSymbols.OfType<IMethodSymbol>()
                .FirstOrDefault();
        if (methodSymbol is null || methodSymbol.IsStatic)
        {
            expanded = targetInvocation;
            expandedSymbol = null;
            return false;
        }

        var methodSyntax =
            methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
            as MethodDeclarationSyntax;
        var bodyExpression = GetProjectableBodyExpression(methodSyntax);
        if (bodyExpression is null)
        {
            expanded = targetInvocation;
            expandedSymbol = null;
            return false;
        }

        var declarationModel = _semanticModel.Compilation.GetSemanticModel(
            bodyExpression.SyntaxTree
        );
        EnsureProjectableExpansionIsAcyclic(
            methodSymbol,
            bodyExpression,
            declarationModel,
            new HashSet<ISymbol>(_activeProjectableSymbols, SymbolEqualityComparer.Default),
            cancellationToken
        );
        var receiverExpression = overrideReceiver is null
            ? GetProjectableReceiverExpression(targetInvocation)
            : SyntaxFactory.ParseExpression(overrideReceiver);
        var parameterBindings = BuildProjectableParameterBindings(
            targetInvocation,
            methodSymbol,
            declarationModel,
            cancellationToken
        );
        var rewriter = new ProjectableInliningRewriter(
            declarationModel,
            methodSymbol.ContainingType,
            receiverExpression,
            parameterBindings
        );
        expanded =
            (ExpressionSyntax?)rewriter.Visit(bodyExpression.WithoutTrivia()) ?? bodyExpression;
        expandedSymbol = methodSymbol;
        return true;
    }

    /// <summary>
    /// Gets projectable body expression.
    /// </summary>
    private static ExpressionSyntax? GetProjectableBodyExpression(
        PropertyDeclarationSyntax? property
    )
    {
        if (property is null)
        {
            return null;
        }

        if (property.ExpressionBody is not null)
        {
            return property.ExpressionBody.Expression;
        }

        var getter = property.AccessorList?.Accessors.FirstOrDefault(accessor =>
            accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
        );
        return GetProjectableBodyExpression(getter);
    }

    /// <summary>
    /// Gets projectable body expression.
    /// </summary>
    private static ExpressionSyntax? GetProjectableBodyExpression(MethodDeclarationSyntax? method)
    {
        if (method is null)
        {
            return null;
        }

        if (method.ExpressionBody is not null)
        {
            return method.ExpressionBody.Expression;
        }

        return method
            .Body?.Statements.OfType<ReturnStatementSyntax>()
            .Select(statement => statement.Expression)
            .FirstOrDefault(expression => expression is not null);
    }

    /// <summary>
    /// Gets projectable body expression.
    /// </summary>
    private static ExpressionSyntax? GetProjectableBodyExpression(
        AccessorDeclarationSyntax? accessor
    )
    {
        if (accessor is null)
        {
            return null;
        }

        if (accessor.ExpressionBody is not null)
        {
            return accessor.ExpressionBody.Expression;
        }

        return accessor
            .Body?.Statements.OfType<ReturnStatementSyntax>()
            .Select(statement => statement.Expression)
            .FirstOrDefault(expression => expression is not null);
    }

    /// <summary>
    /// Builds projectable parameter bindings.
    /// </summary>
    private static Dictionary<ISymbol, ExpressionSyntax> BuildProjectableParameterBindings(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel declarationModel,
        CancellationToken cancellationToken = default
    )
    {
        var bindings = new Dictionary<ISymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);
        Dictionary<string, ISymbol> parameterSyntaxByName;
        if (
            methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
            is MethodDeclarationSyntax methodSyntax
        )
        {
            parameterSyntaxByName = methodSyntax.ParameterList.Parameters.ToDictionary(
                parameter => parameter.Identifier.ValueText,
                parameter => (ISymbol)declarationModel.GetDeclaredSymbol(parameter)!,
                global::System.StringComparer.Ordinal
            );
        }
        else
        {
            parameterSyntaxByName = new Dictionary<string, ISymbol>(
                global::System.StringComparer.Ordinal
            );
        }

        for (var index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var argument = invocation.ArgumentList.Arguments[index];
            var parameterName =
                argument.NameColon?.Name.Identifier.ValueText
                ?? (
                    index < methodSymbol.Parameters.Length
                        ? methodSymbol.Parameters[index].Name
                        : null
                );
            if (
                parameterName is null
                || !parameterSyntaxByName.TryGetValue(parameterName, out var parameterSymbol)
            )
            {
                continue;
            }

            bindings[parameterSymbol] = argument.Expression.WithoutTrivia();
        }

        return bindings;
    }

    /// <summary>
    /// Gets projectable receiver expression.
    /// </summary>
    private static ExpressionSyntax? GetProjectableReceiverExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression.WithoutTrivia(),
            InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Expression.WithoutTrivia(),
            _ => null,
        };
    }

    /// <summary>
    /// Ensures projectable expansion is acyclic.
    /// </summary>
    private void EnsureProjectableExpansionIsAcyclic(
        ISymbol symbol,
        ExpressionSyntax bodyExpression,
        SemanticModel semanticModel,
        ISet<ISymbol> activeSymbols,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (!activeSymbols.Add(symbol))
        {
            throw new global::System.InvalidOperationException(
                $"Detected recursive projectable helper expansion for '{symbol.ToDisplayString()}'."
            );
        }

        try
        {
            foreach (
                var invocation in bodyExpression
                    .DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>()
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    !IsProjectionHookInvocation(
                        invocation,
                        LinqraftProjectionHookKind.Projectable,
                        cancellationToken
                    )
                )
                {
                    continue;
                }

                var targetExpression = GetHookTargetExpression(invocation, cancellationToken);
                if (
                    targetExpression is null
                    || !TryGetProjectableTargetSymbol(
                        targetExpression,
                        semanticModel,
                        out var nestedSymbol,
                        out var nestedBodyExpression,
                        out var nestedSemanticModel,
                        cancellationToken
                    )
                )
                {
                    continue;
                }

                EnsureProjectableExpansionIsAcyclic(
                    nestedSymbol,
                    nestedBodyExpression,
                    nestedSemanticModel,
                    activeSymbols,
                    cancellationToken
                );
            }
        }
        finally
        {
            activeSymbols.Remove(symbol);
        }
    }

    /// <summary>
    /// Attempts to get projectable target symbol.
    /// </summary>
    private static bool TryGetProjectableTargetSymbol(
        ExpressionSyntax targetExpression,
        SemanticModel semanticModel,
        out ISymbol symbol,
        out ExpressionSyntax bodyExpression,
        out SemanticModel declarationModel,
        CancellationToken cancellationToken = default
    )
    {
        symbol = null!;
        bodyExpression = null!;
        declarationModel = null!;

        switch (targetExpression)
        {
            case InvocationExpressionSyntax targetInvocation:
            {
                var methodSymbol =
                    semanticModel.GetSymbolInfo(targetInvocation, cancellationToken).Symbol
                        as IMethodSymbol
                    ?? semanticModel
                        .GetSymbolInfo(targetInvocation, cancellationToken)
                        .CandidateSymbols.OfType<IMethodSymbol>()
                        .FirstOrDefault();
                if (methodSymbol is null || methodSymbol.IsStatic)
                {
                    return false;
                }

                var methodSyntax =
                    methodSymbol
                        .DeclaringSyntaxReferences.FirstOrDefault()
                        ?.GetSyntax(cancellationToken) as MethodDeclarationSyntax;
                var nestedBodyExpression = GetProjectableBodyExpression(methodSyntax);
                if (nestedBodyExpression is null)
                {
                    return false;
                }

                symbol = methodSymbol;
                bodyExpression = nestedBodyExpression;
                declarationModel = semanticModel.Compilation.GetSemanticModel(
                    nestedBodyExpression.SyntaxTree
                );
                return true;
            }
            default:
            {
                var propertySymbol =
                    semanticModel.GetSymbolInfo(targetExpression, cancellationToken).Symbol
                        as IPropertySymbol
                    ?? semanticModel
                        .GetSymbolInfo(targetExpression, cancellationToken)
                        .CandidateSymbols.OfType<IPropertySymbol>()
                        .FirstOrDefault();
                if (propertySymbol is null || propertySymbol.IsStatic)
                {
                    return false;
                }

                var propertySyntax =
                    propertySymbol
                        .DeclaringSyntaxReferences.FirstOrDefault()
                        ?.GetSyntax(cancellationToken) as PropertyDeclarationSyntax;
                var nestedBodyExpression = GetProjectableBodyExpression(propertySyntax);
                if (nestedBodyExpression is null)
                {
                    return false;
                }

                symbol = propertySymbol;
                bodyExpression = nestedBodyExpression;
                declarationModel = semanticModel.Compilation.GetSemanticModel(
                    nestedBodyExpression.SyntaxTree
                );
                return true;
            }
        }
    }

    /// <summary>
    /// Determines whether the invocation is a projection hook invocation.
    /// </summary>
    private bool IsProjectionHookInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftProjectionHookKind kind,
        CancellationToken cancellationToken = default
    )
    {
        return TryGetProjectionHookInvocation(invocation, kind, out _, cancellationToken);
    }

    /// <summary>
    /// Determines whether the expression contains a projection hook.
    /// </summary>
    private bool ContainsProjectionHook(
        ExpressionSyntax expression,
        LinqraftProjectionHookKind kind,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsProjectionHookInvocation(invocation, kind, cancellationToken));
    }

    /// <summary>
    /// Gets hook target expression.
    /// </summary>
    private ExpressionSyntax? GetHookTargetExpression(
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        foreach (
            LinqraftProjectionHookKind kind in Enum.GetValues(typeof(LinqraftProjectionHookKind))
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                TryGetProjectionHookInvocation(
                    invocation,
                    kind,
                    out var hookInvocation,
                    cancellationToken
                )
            )
            {
                return hookInvocation.TargetExpression;
            }
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    }

    /// <summary>
    /// Attempts to get projection hook invocation.
    /// </summary>
    private bool TryGetProjectionHookInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftProjectionHookKind kind,
        out ProjectionHookSyntaxHelper.HookInvocationInfo hookInvocation,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return ProjectionHookSyntaxHelper.TryGetHookInvocation(
            invocation,
            _semanticModel,
            _generatorOptions,
            _projectionHelperParameterName,
            _projectionHelperParameterTypeName,
            kind,
            out hookInvocation,
            cancellationToken
        );
    }

    /// <summary>
    /// Attempts to get projected value selection.
    /// </summary>
    private bool TryGetProjectedValueSelection(
        InvocationExpressionSyntax invocation,
        out ProjectionHookSyntaxHelper.ProjectedValueSelectionInfo selectionInfo,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return ProjectionHookSyntaxHelper.TryGetProjectedValueSelection(
            invocation,
            _semanticModel,
            _generatorOptions,
            _projectionHelperParameterName,
            _projectionHelperParameterTypeName,
            out selectionInfo,
            cancellationToken
        );
    }
}
