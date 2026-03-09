using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

internal static class AnalyzerHelpers
{
    private static readonly Regex HashNamespacePattern = new(@"(^|\.)(LinqraftGenerated_[A-Za-z0-9]{8,})($|\.)", RegexOptions.Compiled);

    public static bool IsSelectExprInvocation(InvocationExpressionSyntax invocation)
    {
        return string.Equals(GetInvocationName(invocation.Expression), "SelectExpr", StringComparison.Ordinal);
    }

    public static bool IsQueryableSelectInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (!string.Equals(GetInvocationName(invocation.Expression), "Select", StringComparison.Ordinal))
        {
            return false;
        }

        var symbol = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        if (symbol?.Name != "Select")
        {
            return false;
        }

        var reducedFrom = symbol.ReducedFrom ?? symbol;
        var receiverType = reducedFrom.Parameters.FirstOrDefault()?.Type;
        return ImplementsOpenGeneric(receiverType, "System.Linq.IQueryable<T>");
    }

    public static bool IsDbSet(
        ITypeSymbol? symbol
    )
    {
        var current = symbol as INamedTypeSymbol;
        while (current is not null)
        {
            if (current.Name == "DbSet" && current.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    public static bool IsHashNamespace(string namespaceName)
    {
        return HashNamespacePattern.IsMatch(namespaceName);
    }

    public static LambdaExpressionSyntax? GetSelectorLambda(InvocationExpressionSyntax invocation)
    {
        return invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
    }

    public static ExpressionSyntax? GetLambdaExpressionBody(LambdaExpressionSyntax lambda)
    {
        return lambda.Body as ExpressionSyntax;
    }

    public static bool IsAnonymousProjection(LambdaExpressionSyntax lambda)
    {
        return GetLambdaExpressionBody(lambda) is AnonymousObjectCreationExpressionSyntax;
    }

    public static bool IsNamedProjection(LambdaExpressionSyntax lambda)
    {
        return GetLambdaExpressionBody(lambda) is ObjectCreationExpressionSyntax;
    }

    public static string GetInvocationName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    public static SimpleNameSyntax? GetInvocationNameSyntax(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            IdentifierNameSyntax identifier => identifier,
            GenericNameSyntax genericName => genericName,
            _ => null,
        };
    }

    public static string GenerateDtoName(SyntaxNode contextNode)
    {
        var method = contextNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null)
        {
            var methodName = method.Identifier.ValueText;
            var trimmed = methodName.EndsWith("Async", StringComparison.Ordinal)
                ? methodName[..^5]
                : methodName;
            return $"{trimmed}Dto";
        }

        var variable = contextNode.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if (variable is not null)
        {
            return $"{FirstCharToUpper(variable.Identifier.ValueText)}Dto";
        }

        return $"ResultDto_{Math.Abs(contextNode.SpanStart):X8}";
    }

    public static IEnumerable<ISymbol> CollectOuterReferences(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var locallyDeclaredSymbols = new HashSet<ISymbol>(
            lambda.DescendantNodesAndSelf()
                .OfType<ParameterSyntax>()
                .Select(parameter => semanticModel.GetDeclaredSymbol(parameter, cancellationToken))
                .OfType<ISymbol>()
                .Concat(
                    lambda.DescendantNodes()
                        .OfType<VariableDeclaratorSyntax>()
                        .Select(variable => semanticModel.GetDeclaredSymbol(variable, cancellationToken))
                        .OfType<ISymbol>()
                ),
            SymbolEqualityComparer.Default
        );

        foreach (var identifier in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
            {
                continue;
            }

            if (identifier.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == identifier)
            {
                continue;
            }

            if (identifier.Parent is NameEqualsSyntax
                || identifier.Parent is NameColonSyntax
                || identifier.Parent is AnonymousObjectMemberDeclaratorSyntax { NameEquals: not null }
                || identifier.Parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier
                || identifier.Parent is VariableDeclaratorSyntax
                || identifier.Parent is ParameterSyntax)
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (symbol is null)
            {
                continue;
            }

            if (locallyDeclaredSymbols.Contains(symbol))
            {
                continue;
            }

            if (symbol is ILocalSymbol local && local.IsConst)
            {
                continue;
            }

            if (symbol is IFieldSymbol field && field.IsConst)
            {
                continue;
            }

            if (symbol.Kind is SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Field or SymbolKind.Property)
            {
                yield return symbol;
            }
        }

        foreach (var memberAccess in lambda.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
            if (symbol is not IFieldSymbol and not IPropertySymbol)
            {
                continue;
            }

            if (IsLocalAccess(memberAccess.Expression, semanticModel, cancellationToken, locallyDeclaredSymbols))
            {
                continue;
            }

            yield return symbol;
        }
    }

    public static IEnumerable<string> GetCaptureNames(InvocationExpressionSyntax invocation)
    {
        var capture = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText == "capture"
                || (!argument.Equals(invocation.ArgumentList.Arguments.First()) && argument.Expression is AnonymousObjectCreationExpressionSyntax)
            )?.Expression as AnonymousObjectCreationExpressionSyntax;

        if (capture is null)
        {
            return Enumerable.Empty<string>();
        }

        return capture.Initializers.Select(GetAnonymousMemberName);
    }

    public static string GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        if (initializer.NameEquals is not null)
        {
            return initializer.NameEquals.Name.Identifier.ValueText;
        }

        return initializer.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => initializer.Expression.ToString(),
        };
    }

    public static bool ContainsNullCheckedObjectTernary(ConditionalExpressionSyntax conditionalExpression)
    {
        var whenTrueIsNull = conditionalExpression.WhenTrue.IsKind(SyntaxKind.NullLiteralExpression);
        var whenFalseIsNull = conditionalExpression.WhenFalse.IsKind(SyntaxKind.NullLiteralExpression);
        var otherBranch = whenTrueIsNull ? conditionalExpression.WhenFalse : whenFalseIsNull ? conditionalExpression.WhenTrue : null;
        return otherBranch is ObjectCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax
            && conditionalExpression.Condition.ToString().Contains("!= null", StringComparison.Ordinal);
    }

    public static bool ImplementsOpenGeneric(ITypeSymbol? typeSymbol, string metadataName)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (typeSymbol is INamedTypeSymbol namedType
            && namedType.ConstructedFrom.ToDisplayString() == metadataName)
        {
            return true;
        }

        return typeSymbol.AllInterfaces.Any(interfaceType => interfaceType.ConstructedFrom.ToDisplayString() == metadataName);
    }

    private static string FirstCharToUpper(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Result";
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static bool IsLocalAccess(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ISet<ISymbol> locallyDeclaredSymbols
    )
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
            {
                var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                return symbol is not null && locallyDeclaredSymbols.Contains(symbol);
            }
            case MemberAccessExpressionSyntax memberAccess:
                return IsLocalAccess(memberAccess.Expression, semanticModel, cancellationToken, locallyDeclaredSymbols);
            case ConditionalAccessExpressionSyntax conditionalAccess:
                return IsLocalAccess(conditionalAccess.Expression, semanticModel, cancellationToken, locallyDeclaredSymbols);
            case ElementAccessExpressionSyntax elementAccess:
                return IsLocalAccess(elementAccess.Expression, semanticModel, cancellationToken, locallyDeclaredSymbols);
            case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                return IsLocalAccess(memberAccess.Expression, semanticModel, cancellationToken, locallyDeclaredSymbols);
            default:
                return false;
        }
    }
}
