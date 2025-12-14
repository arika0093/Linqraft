using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Analysis;

/// <summary>
/// Analyzer for extracting type information from parsed syntax.
/// Resolves source and target types from SelectExpr expressions.
/// </summary>
internal class TypeAnalyzer : ISemanticAnalyzer
{
    private readonly SemanticModel _semanticModel;

    public TypeAnalyzer(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
    }

    /// <inheritdoc/>
    public AnalyzedSyntax Analyze(Parsing.ParsedSyntax parsed)
    {
        ITypeSymbol? sourceType = null;
        ITypeSymbol? targetType = null;

        if (parsed.OriginalNode is InvocationExpressionSyntax invocation)
        {
            sourceType = GetSourceType(invocation);
            targetType = GetTargetType(invocation);
        }

        return new AnalyzedSyntax
        {
            ParsedSyntax = parsed,
            SourceType = sourceType,
            TargetType = targetType
        };
    }

    private ITypeSymbol? GetSourceType(InvocationExpressionSyntax invocation)
    {
        // Get the type of the source collection (e.g., IQueryable<User>)
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var typeInfo = _semanticModel.GetTypeInfo(memberAccess.Expression);
            if (typeInfo.Type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                // Return the element type (e.g., User from IQueryable<User>)
                return namedType.TypeArguments.FirstOrDefault();
            }
        }
        return null;
    }

    private ITypeSymbol? GetTargetType(InvocationExpressionSyntax invocation)
    {
        // Get the return type of the SelectExpr expression
        var methodSymbol = _semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol?.ReturnType is INamedTypeSymbol returnType && returnType.IsGenericType)
        {
            // Return the element type of the result collection
            return returnType.TypeArguments.FirstOrDefault();
        }
        return null;
    }
}
