using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.RoslynHelpers;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Analysis;

/// <summary>
/// Analyzes expressions to create DtoProperty and DtoStructure instances.
/// This is part of the Analysis phase of the pipeline.
/// </summary>
internal class DtoAnalyzer
{
    private readonly SemanticModel _semanticModel;
    private readonly LinqraftConfiguration? _configuration;

    /// <summary>
    /// Creates a new DTO analyzer.
    /// </summary>
    public DtoAnalyzer(SemanticModel semanticModel, LinqraftConfiguration? configuration = null)
    {
        _semanticModel = semanticModel;
        _configuration = configuration;
    }

    /// <summary>
    /// Analyzes an anonymous object creation expression and creates a DtoStructure.
    /// </summary>
    public DtoStructure? AnalyzeAnonymousType(
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        ITypeSymbol sourceType,
        string? hintName = null)
    {
        return DtoStructure.AnalyzeAnonymousType(
            anonymousObj,
            _semanticModel,
            sourceType,
            hintName,
            _configuration);
    }

    /// <summary>
    /// Analyzes a named type object creation expression and creates a DtoStructure.
    /// </summary>
    public DtoStructure? AnalyzeNamedType(
        ObjectCreationExpressionSyntax namedObj,
        ITypeSymbol sourceType,
        string? hintName = null)
    {
        return DtoStructure.AnalyzeNamedType(
            namedObj,
            _semanticModel,
            sourceType,
            hintName,
            _configuration);
    }

    /// <summary>
    /// Analyzes an expression and creates a DtoProperty.
    /// </summary>
    public DtoProperty? AnalyzeProperty(
        string propertyName,
        ExpressionSyntax expression,
        IPropertySymbol? targetProperty = null,
        string? accessibility = null)
    {
        return DtoProperty.AnalyzeExpression(
            propertyName,
            expression,
            _semanticModel,
            targetProperty,
            accessibility,
            _configuration);
    }

    /// <summary>
    /// Extracts the source type from a lambda expression's parameter.
    /// </summary>
    public ITypeSymbol? ExtractSourceType(LambdaExpressionSyntax lambda)
    {
        var lambdaSymbol = _semanticModel.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
        if (lambdaSymbol is not null && lambdaSymbol.Parameters.Length > 0)
        {
            return lambdaSymbol.Parameters[0].Type;
        }
        return null;
    }

    /// <summary>
    /// Extracts the source type from an invocation expression.
    /// </summary>
    public ITypeSymbol? ExtractSourceTypeFromInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var typeInfo = _semanticModel.GetTypeInfo(memberAccess.Expression);
            if (typeInfo.Type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                return namedType.TypeArguments.Length > 0 ? namedType.TypeArguments[0] : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Determines the target type for code generation from a lambda expression.
    /// </summary>
    public ITypeSymbol? ExtractTargetType(LambdaExpressionSyntax lambda)
    {
        var body = lambda.Body;
        if (body is ExpressionSyntax bodyExpr)
        {
            var typeInfo = _semanticModel.GetTypeInfo(bodyExpr);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }
        return null;
    }

    /// <summary>
    /// Checks if an expression is an anonymous type creation.
    /// </summary>
    public bool IsAnonymousTypeCreation(ExpressionSyntax expression)
    {
        return expression is AnonymousObjectCreationExpressionSyntax;
    }

    /// <summary>
    /// Checks if an expression is a named type object creation.
    /// </summary>
    public bool IsNamedTypeCreation(ExpressionSyntax expression)
    {
        return expression is ObjectCreationExpressionSyntax;
    }

    /// <summary>
    /// Gets the properties of a type symbol.
    /// </summary>
    public IEnumerable<IPropertySymbol> GetTypeProperties(ITypeSymbol typeSymbol)
    {
        return typeSymbol.GetMembers().OfType<IPropertySymbol>();
    }
}
