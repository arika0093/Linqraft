using System;
using System.Linq;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Represents a property in a DTO structure with metadata for code generation
/// </summary>
public record DtoProperty(
    string Name,
    bool IsNullable,
    string OriginalExpression,
    ExpressionSyntax OriginalSyntax,
    ITypeSymbol TypeSymbol,
    DtoStructure? NestedStructure,
    string? Accessibility = null
)
{
    /// <summary>
    /// Gets the fully qualified type name of the property
    /// </summary>
    public string TypeName
    {
        get
        {
            // Check if the type symbol is an error type or invalid
            if (
                TypeSymbol is IErrorTypeSymbol
                || TypeSymbol.SpecialType == SpecialType.System_Object
            )
            {
                return "object";
            }

            var typeName = TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            // Fallback to a safe type if the type name is empty or invalid
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return "object";
            }
            return typeName;
        }
    }

    /// <summary>
    /// Analyzes an expression and creates a DtoProperty from it
    /// </summary>
    /// <param name="propertyName">The name of the property</param>
    /// <param name="expression">The expression syntax to analyze</param>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="targetProperty">Optional target property symbol (for predefined DTOs)</param>
    /// <param name="accessibility">Optional accessibility modifier (e.g., "public", "internal")</param>
    /// <returns>A DtoProperty instance or null if analysis fails</returns>
    public static DtoProperty? AnalyzeExpression(
        string propertyName,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IPropertySymbol? targetProperty = null,
        string? accessibility = null
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        var propertyType = typeInfo.Type ?? typeInfo.ConvertedType;
        if (propertyType is null)
            return null;

        // Determine nullability: prefer the expression's type over the targetProperty's type
        // This is important for lambda expressions where the anonymous type may lose nullability info
        NullableAnnotation nullableAnnotation;
        
        // For direct member access (e.g., s.Name), get nullability from the source member
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (memberSymbol is IPropertySymbol sourceProp)
            {
                (propertyType, nullableAnnotation) = GetTypeInfoFromSymbol(sourceProp);
            }
            else if (memberSymbol is IFieldSymbol sourceField)
            {
                (propertyType, nullableAnnotation) = GetTypeInfoFromSymbol(sourceField);
            }
            else if (targetProperty is not null)
            {
                (propertyType, nullableAnnotation) = GetTypeInfoFromSymbol(targetProperty);
            }
            else
            {
                nullableAnnotation = propertyType.NullableAnnotation;
            }
        }
        // For other expressions (method calls, operators, etc.), use targetProperty if available
        else if (targetProperty is not null)
        {
            (propertyType, nullableAnnotation) = GetTypeInfoFromSymbol(targetProperty);
        }
        else
        {
            nullableAnnotation = propertyType.NullableAnnotation;
        }

        var isNullable = nullableAnnotation == NullableAnnotation.Annotated;

        // Check if nullable operator ?. is used
        var hasNullableAccess = HasNullableAccess(expression);

        // Syntax-based nullable detection (fallback for Visual Studio issues)
        // If Roslyn returns None (unreliable in VS), use syntax-based heuristics
        var shouldGenerateNullCheck = ShouldGenerateNullCheckFromSyntax(expression);
        if (nullableAnnotation == NullableAnnotation.None && shouldGenerateNullCheck)
        {
            // Override: treat as nullable based on syntax
            isNullable = true;
        }

        // Detect nested Select (e.g., s.Childs.Select(...) or s.Childs.Select(...).ToList())
        DtoStructure? nestedStructure = null;
        // First, try to find Select invocation (handles both direct Select and chained methods like ToList)
        var selectInvocation = FindSelectInvocation(expression);
        if (selectInvocation is not null && selectInvocation.ArgumentList.Arguments.Count > 0)
        {
            var lambdaArg = selectInvocation.ArgumentList.Arguments[0].Expression;
            if (lambdaArg is LambdaExpressionSyntax nestedLambda)
            {
                // Get collection element type from the Select's source
                ITypeSymbol? collectionType = null;

                if (selectInvocation.Expression is MemberAccessExpressionSyntax selectMemberAccess)
                {
                    collectionType = semanticModel.GetTypeInfo(selectMemberAccess.Expression).Type;
                }
                else if (selectInvocation.Expression is MemberBindingExpressionSyntax)
                {
                    // For conditional access (?.Select), we need to find the base expression
                    // Look for ConditionalAccessExpressionSyntax in ancestors
                    var conditionalAccess = expression
                        .DescendantNodesAndSelf()
                        .OfType<ConditionalAccessExpressionSyntax>()
                        .FirstOrDefault();
                    if (conditionalAccess is not null)
                    {
                        collectionType = semanticModel
                            .GetTypeInfo(conditionalAccess.Expression)
                            .Type;
                    }
                }

                if (
                    collectionType is INamedTypeSymbol namedCollectionType
                    && namedCollectionType.TypeArguments.Length > 0
                )
                {
                    var elementType = namedCollectionType.TypeArguments[0];

                    // Support both anonymous types and named types
                    if (
                        nestedLambda.Body is AnonymousObjectCreationExpressionSyntax nestedAnonymous
                    )
                    {
                        nestedStructure = DtoStructure.AnalyzeAnonymousType(
                            nestedAnonymous,
                            semanticModel,
                            elementType
                        );
                    }
                    else if (nestedLambda.Body is ObjectCreationExpressionSyntax nestedNamed)
                    {
                        nestedStructure = DtoStructure.AnalyzeNamedType(
                            nestedNamed,
                            semanticModel,
                            elementType
                        );
                    }
                }
            }
        }

        // Detect nested SelectMany (e.g., s.Childs.SelectMany(c => c.GrandChilds))
        if (nestedStructure is null)
        {
            var selectManyInvocation = FindSelectManyInvocation(expression);
            if (
                selectManyInvocation is not null
                && selectManyInvocation.ArgumentList.Arguments.Count > 0
            )
            {
                var lambdaArg = selectManyInvocation.ArgumentList.Arguments[0].Expression;
                if (lambdaArg is LambdaExpressionSyntax nestedLambda)
                {
                    // Get collection element type from the SelectMany's source
                    ITypeSymbol? collectionType = null;

                    if (
                        selectManyInvocation.Expression
                        is MemberAccessExpressionSyntax selectManyMemberAccess
                    )
                    {
                        collectionType = semanticModel
                            .GetTypeInfo(selectManyMemberAccess.Expression)
                            .Type;
                    }
                    else if (selectManyInvocation.Expression is MemberBindingExpressionSyntax)
                    {
                        // For conditional access (?.SelectMany), we need to find the base expression
                        var conditionalAccess = expression
                            .DescendantNodesAndSelf()
                            .OfType<ConditionalAccessExpressionSyntax>()
                            .FirstOrDefault();
                        if (conditionalAccess is not null)
                        {
                            collectionType = semanticModel
                                .GetTypeInfo(conditionalAccess.Expression)
                                .Type;
                        }
                    }

                    if (
                        collectionType is INamedTypeSymbol namedCollectionType
                        && namedCollectionType.TypeArguments.Length > 0
                    )
                    {
                        var elementType = namedCollectionType.TypeArguments[0];

                        // For SelectMany, the lambda body should be a member access or invocation
                        // that returns a collection. We need to analyze what the lambda returns.
                        // The lambda body could be:
                        // 1. Simple member access: c => c.GrandChildren
                        // 2. Projection with Select: c => c.GrandChildren.Select(gc => new { ... })
                        // 3. Anonymous type with Select inside: c => new { Grands = c.GrandChildren.Select(...) }

                        // Check if the lambda body contains a Select
                        var innerSelectInvocation = nestedLambda.Body
                            is ExpressionSyntax lambdaBodyExpr
                            ? FindSelectInvocation(lambdaBodyExpr)
                            : null;
                        if (
                            innerSelectInvocation is not null
                            && innerSelectInvocation.ArgumentList.Arguments.Count > 0
                        )
                        {
                            var innerLambdaArg = innerSelectInvocation
                                .ArgumentList
                                .Arguments[0]
                                .Expression;
                            if (innerLambdaArg is LambdaExpressionSyntax innerLambda)
                            {
                                // Get the inner collection type
                                var innerCollectionType = nestedLambda.Body
                                    is ExpressionSyntax lambdaBodyForType
                                    ? semanticModel.GetTypeInfo(lambdaBodyForType).Type
                                    : null;
                                if (
                                    innerCollectionType is INamedTypeSymbol innerNamedType
                                    && innerNamedType.TypeArguments.Length > 0
                                )
                                {
                                    var innerElementType = innerNamedType.TypeArguments[0];

                                    if (
                                        innerLambda.Body
                                        is AnonymousObjectCreationExpressionSyntax innerAnonymous
                                    )
                                    {
                                        nestedStructure = DtoStructure.AnalyzeAnonymousType(
                                            innerAnonymous,
                                            semanticModel,
                                            innerElementType
                                        );
                                    }
                                    else if (
                                        innerLambda.Body
                                        is ObjectCreationExpressionSyntax innerNamed
                                    )
                                    {
                                        nestedStructure = DtoStructure.AnalyzeNamedType(
                                            innerNamed,
                                            semanticModel,
                                            innerElementType
                                        );
                                    }
                                }
                            }
                        }
                        else if (
                            nestedLambda.Body
                            is AnonymousObjectCreationExpressionSyntax anonymousBody
                        )
                        {
                            // Handle: c => new { Grands = c.GrandChildren.Select(...) }
                            nestedStructure = DtoStructure.AnalyzeAnonymousType(
                                anonymousBody,
                                semanticModel,
                                elementType
                            );
                        }
                        // Note: For simple member access like c => c.GrandChildren,
                        // we don't create a nested structure because the result is just
                        // a flattened collection of the existing type.
                    }
                }
            }
        }

        // Detect direct anonymous type creation (e.g., Channel = new { Id = ..., Name = ... })
        // This handles nested anonymous types that are not inside a Select call
        if (
            nestedStructure is null
            && expression is AnonymousObjectCreationExpressionSyntax directAnonymous
        )
        {
            // Get the source type from the anonymous type properties
            // We need to find the base type from which properties are being accessed
            ITypeSymbol? sourceTypeForNested = null;

            // Try to infer the source type from the first property that has a member access
            foreach (var initializer in directAnonymous.Initializers)
            {
                var initExpr = initializer.Expression;
                if (initExpr is MemberAccessExpressionSyntax initMemberAccess)
                {
                    // Get the type of the expression being accessed (e.g., q.Channel)
                    var baseTypeInfo = semanticModel.GetTypeInfo(initMemberAccess.Expression);
                    if (baseTypeInfo.Type is not null)
                    {
                        sourceTypeForNested = baseTypeInfo.Type;
                        break;
                    }
                }
            }

            // If we couldn't infer the source type, use the property type itself
            sourceTypeForNested ??= propertyType;

            nestedStructure = DtoStructure.AnalyzeAnonymousType(
                directAnonymous,
                semanticModel,
                sourceTypeForNested
            );
        }

        // Generalized handling for expressions that result in anonymous types
        // This works for any expression type (ternary operators, method calls, etc.)
        // Step 1: Evaluate the type of the entire expression
        if (nestedStructure is null)
        {
            var expressionTypeInfo = semanticModel.GetTypeInfo(expression);
            var expressionType = expressionTypeInfo.Type ?? expressionTypeInfo.ConvertedType;

            // Step 2: If it is (anonymous) or (anonymous?), we need to generate a DTO for that type
            var underlyingType = expressionType;
            if (
                expressionType is INamedTypeSymbol
                {
                    NullableAnnotation: NullableAnnotation.Annotated
                } namedType
            )
            {
                // For nullable anonymous types, get the underlying type
                underlyingType = namedType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            }

            if (underlyingType != null && underlyingType.IsAnonymousType)
            {
                // Step 3: Find the anonymous type creation expression within this expression
                // It could be anywhere in the syntax tree (e.g., in a ternary branch, method argument, etc.)
                var anonymousCreation = expression
                    .DescendantNodesAndSelf()
                    .OfType<AnonymousObjectCreationExpressionSyntax>()
                    .FirstOrDefault();

                if (anonymousCreation != null)
                {
                    // Analyze the anonymous type using the underlying type as the source
                    nestedStructure = DtoStructure.AnalyzeAnonymousType(
                        anonymousCreation,
                        semanticModel,
                        underlyingType
                    );
                }
            }
        }

        return new DtoProperty(
            Name: propertyName,
            IsNullable: isNullable || hasNullableAccess,
            OriginalExpression: expression.ToString(),
            OriginalSyntax: expression,
            TypeSymbol: propertyType,
            NestedStructure: nestedStructure,
            Accessibility: accessibility
        );
    }

    /// <summary>
    /// Helper method to extract type and nullability annotation from a symbol
    /// </summary>
    private static (ITypeSymbol Type, NullableAnnotation NullableAnnotation) GetTypeInfoFromSymbol(
        ISymbol symbol
    )
    {
        var type = symbol switch
        {
            IPropertySymbol prop => prop.Type,
            IFieldSymbol field => field.Type,
            _ => throw new ArgumentException($"Unsupported symbol type: {symbol.GetType().Name}")
        };
        return (type, type.NullableAnnotation);
    }

    /// <summary>
    /// Syntax-based heuristic to determine if null check should be generated.
    /// This is a fallback for when Roslyn's NullableAnnotation is unreliable (e.g., in Visual Studio).
    /// see https://github.com/arika0093/Linqraft/issues/22
    /// </summary>
    private static bool ShouldGenerateNullCheckFromSyntax(ExpressionSyntax expression)
    {
        return NullConditionalHelper.ShouldGenerateNullCheckFromSyntax(expression);
    }

    private static bool HasNullableAccess(ExpressionSyntax expression)
    {
        return NullConditionalHelper.HasNullConditionalAccess(expression);
    }

    private static InvocationExpressionSyntax? FindSelectInvocation(ExpressionSyntax expression)
    {
        return LinqMethodHelper.FindLinqMethodInvocation(expression, "Select");
    }

    private static InvocationExpressionSyntax? FindSelectManyInvocation(ExpressionSyntax expression)
    {
        return LinqMethodHelper.FindLinqMethodInvocation(expression, "SelectMany");
    }
}
