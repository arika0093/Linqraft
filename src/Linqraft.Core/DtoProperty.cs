using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.RoslynHelpers;
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
    string? Accessibility = null,
    bool IsNestedFromNamedType = false,
    DocumentationCommentHelper.DocumentationInfo? Documentation = null
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
        // For invocation expressions (e.g., .Select(...).ToList()), prefer expression's type
        // The anonymous type may incorrectly mark collections as nullable based on their contents
        else if (expression is InvocationExpressionSyntax invocation)
        {
            // For collection creation methods, ensure the collection itself is not nullable
            // even if it contains nullable elements
            if (ShouldForceNonNullableCollection(invocation))
            {
                // Force the collection to be non-nullable
                propertyType = propertyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                nullableAnnotation = NullableAnnotation.NotAnnotated;
            }
            else
            {
                // Use the expression's own type information (from typeInfo at line 66)
                nullableAnnotation = propertyType.NullableAnnotation;
            }
        }
        // For other expressions (operators, etc.), use targetProperty if available
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
        bool isNestedFromNamedType = false;
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
                    // Check if the base expression is a MemberBindingExpressionSyntax (part of conditional access)
                    // e.g., d.InnerData?.Childs.Select(...) - here .Childs is a MemberBindingExpressionSyntax
                    if (selectMemberAccess.Expression is MemberBindingExpressionSyntax)
                    {
                        // For conditional access chains (e.g., d.InnerData?.Childs.Select),
                        // we need to find the conditional access expression and get the type from there
                        var conditionalAccess = expression
                            .DescendantNodesAndSelf()
                            .OfType<ConditionalAccessExpressionSyntax>()
                            .FirstOrDefault();
                        if (conditionalAccess is not null)
                        {
                            // Get the type of the WhenNotNull part up to the collection
                            // For d.InnerData?.Childs.Select, we need to get the type of d.InnerData.Childs
                            // The semantic model can resolve the type of the conditional access expression result
                            // which includes the null-conditional path
                            var memberBinding = (MemberBindingExpressionSyntax)
                                selectMemberAccess.Expression;
                            var memberName = memberBinding.Name.Identifier.Text;

                            // Get the type of the expression before the ?. operator
                            var baseType = semanticModel
                                .GetTypeInfo(conditionalAccess.Expression)
                                .Type;
                            if (baseType is not null)
                            {
                                // Get the non-nullable underlying type if it's a nullable type
                                // e.g., ChildData? -> ChildData
                                var nonNullableBaseType =
                                    RoslynTypeHelper.GetNonNullableType(baseType) ?? baseType;

                                // Find the member with the specified name on the base type
                                var memberSymbol =
                                    nonNullableBaseType
                                        .GetMembers(memberName)
                                        .OfType<IPropertySymbol>()
                                        .FirstOrDefault()
                                    ?? (ISymbol?)
                                        nonNullableBaseType
                                            .GetMembers(memberName)
                                            .OfType<IFieldSymbol>()
                                            .FirstOrDefault();
                                if (memberSymbol is IPropertySymbol propSymbol)
                                {
                                    collectionType = propSymbol.Type;
                                }
                                else if (memberSymbol is IFieldSymbol fieldSymbol)
                                {
                                    collectionType = fieldSymbol.Type;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Normal case: direct member access
                        collectionType = semanticModel
                            .GetTypeInfo(selectMemberAccess.Expression)
                            .Type;
                    }
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
                            elementType,
                            propertyName
                        );
                    }
                    else if (nestedLambda.Body is ObjectCreationExpressionSyntax nestedNamed)
                    {
                        nestedStructure = DtoStructure.AnalyzeNamedType(
                            nestedNamed,
                            semanticModel,
                            elementType,
                            propertyName
                        );
                        isNestedFromNamedType = true;
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
                                            innerElementType,
                                            propertyName
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
                                            innerElementType,
                                            propertyName
                                        );
                                        isNestedFromNamedType = true;
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
                                elementType,
                                propertyName
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
                sourceTypeForNested,
                propertyName
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

            // Check for direct anonymous type
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
                        underlyingType,
                        propertyName
                    );
                }
            }
            // Also check for collections containing anonymous types (e.g., IEnumerable<anonymous>)
            // This handles cases like: c.X != null ? c.X.Items.Select(i => new { i.Id }) : null
            else if (
                underlyingType is INamedTypeSymbol collectionType
                && collectionType.TypeArguments.Length > 0
            )
            {
                var elementType = collectionType.TypeArguments[0];
                if (elementType.IsAnonymousType)
                {
                    // Find the anonymous type creation expression within this expression
                    var anonymousCreation = expression
                        .DescendantNodesAndSelf()
                        .OfType<AnonymousObjectCreationExpressionSyntax>()
                        .FirstOrDefault();

                    if (anonymousCreation != null)
                    {
                        // Analyze the anonymous type using the element type as the source
                        nestedStructure = DtoStructure.AnalyzeAnonymousType(
                            anonymousCreation,
                            semanticModel,
                            elementType,
                            propertyName
                        );
                    }
                }
            }
        }

        // Determine final nullability
        // Special case: When we have a collection with nullable access that will use Enumerable.Empty<T>()
        // as the fallback value, the property should NOT be nullable.
        // Simplified approach (per maintainer feedback):
        // 1. Expression should NOT contain a ternary operator (if it does, keep nullable)
        // 2. The type must be IEnumerable<T> or derived (List, Array, etc.)
        // 3. If nested structure exists (anonymous type in Select), remove nullable
        var shouldBeNullable = isNullable || hasNullableAccess;
        var finalPropertyType = propertyType;

        // Check if this expression contains a ternary operator - if so, keep nullable
        var hasTernaryOperator = expression
            .DescendantNodesAndSelf()
            .OfType<ConditionalExpressionSyntax>()
            .Any();

        // Apply non-nullable only when:
        // - Currently marked as nullable
        // - No ternary operator present
        // - Type is a collection type (IEnumerable, List, Array, etc.)
        // - Has nested structure (anonymous type in Select/SelectMany)
        if (
            shouldBeNullable
            && !hasTernaryOperator
            && nestedStructure is not null
            && RoslynTypeHelper.IsCollectionType(propertyType)
        )
        {
            // Collection with nested structure and no ternary will use Enumerable.Empty<T>() as fallback
            // So the collection itself should not be nullable
            shouldBeNullable = false;
            // Also remove the nullable annotation from the type symbol if present
            finalPropertyType = RoslynTypeHelper.GetNonNullableType(propertyType) ?? propertyType;
        }

        // Extract documentation from the source symbol
        var documentation = ExtractDocumentation(expression, semanticModel);

        return new DtoProperty(
            Name: propertyName,
            IsNullable: shouldBeNullable,
            OriginalExpression: expression.ToString(),
            OriginalSyntax: expression,
            TypeSymbol: finalPropertyType,
            NestedStructure: nestedStructure,
            Accessibility: accessibility,
            IsNestedFromNamedType: isNestedFromNamedType,
            Documentation: documentation
        );
    }

    /// <summary>
    /// Checks if an expression contains a Select method call with an anonymous type creation in its lambda body
    /// </summary>
    private static bool ContainsSelectWithAnonymousType(ExpressionSyntax expression)
    {
        var selectInvocation = FindSelectInvocation(expression);
        if (selectInvocation is null || selectInvocation.ArgumentList.Arguments.Count == 0)
            return false;

        var lambdaArg = selectInvocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is not LambdaExpressionSyntax lambda)
            return false;

        // Check if the lambda body is an anonymous object creation
        return lambda.Body is AnonymousObjectCreationExpressionSyntax;
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
            _ => throw new ArgumentException($"Unsupported symbol type: {symbol.GetType().Name}"),
        };
        return (type, type.NullableAnnotation);
    }

    /// <summary>
    /// Determines if a collection invocation should be forced to be non-nullable
    /// </summary>
    private static bool ShouldForceNonNullableCollection(InvocationExpressionSyntax invocation)
    {
        // Check if there's a nullable access operator on the collection itself
        var hasNullableOnCollection = HasNullableAccess(invocation);

        // If there's no nullable access on the collection itself and it's a collection creation method,
        // the collection should not be nullable even if it contains nullable elements
        return !hasNullableOnCollection && IsCollectionCreationMethod(invocation);
    }

    /// <summary>
    /// Collection creation method names that produce non-nullable collections
    /// </summary>
    private static readonly HashSet<string> CollectionCreationMethods = new()
    {
        "ToList",
        "ToArray",
        "ToHashSet",
        "ToImmutableList",
        "ToImmutableArray",
        "ToImmutableHashSet",
        "ToImmutableSet",
        "ToDictionary",
        "ToImmutableDictionary",
        "ToLookup",
        "AsEnumerable",
        "AsQueryable",
    };

    /// <summary>
    /// Checks if an invocation is a collection creation method like ToList(), ToArray(), etc.
    /// </summary>
    private static bool IsCollectionCreationMethod(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            return CollectionCreationMethods.Contains(methodName);
        }
        return false;
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

    /// <summary>
    /// Extracts documentation information from the source symbol of an expression
    /// </summary>
    private static DocumentationCommentHelper.DocumentationInfo? ExtractDocumentation(
        ExpressionSyntax expression,
        SemanticModel semanticModel
    )
    {
        // Build source reference from expression (converting lambda param to source type name)
        var sourceReference = BuildSourceReference(expression, semanticModel);

        // Only extract symbol documentation for simple member access expressions
        // For complex expressions (binary, invocations with nested selects, etc.), just use source reference
        var isSimpleMemberAccess = IsSimpleMemberAccessExpression(expression);

        DocumentationCommentHelper.DocumentationInfo? baseDocumentation = null;

        if (isSimpleMemberAccess)
        {
            // Try to get the source symbol from the expression
            var (sourceSymbol, containingTypeName) = GetSourceSymbolFromExpression(
                expression,
                semanticModel
            );

            // Extract documentation from the source symbol
            if (sourceSymbol is IPropertySymbol propertySymbol)
            {
                baseDocumentation = DocumentationCommentHelper.GetPropertyDocumentation(
                    propertySymbol,
                    containingTypeName
                );
            }
            else if (sourceSymbol is IFieldSymbol fieldSymbol)
            {
                baseDocumentation = DocumentationCommentHelper.GetFieldDocumentation(
                    fieldSymbol,
                    containingTypeName
                );
            }
        }

        // If we have documentation, update with the full source reference
        if (baseDocumentation != null)
        {
            return baseDocumentation with
            {
                SourceReference = sourceReference ?? baseDocumentation.SourceReference,
            };
        }

        // If no source symbol found, return just the source reference
        if (!string.IsNullOrEmpty(sourceReference))
        {
            return new DocumentationCommentHelper.DocumentationInfo
            {
                SourceReference = sourceReference,
            };
        }

        return null;
    }

    /// <summary>
    /// Determines if an expression is a simple member access (e.g., s.Name, s.Child?.Name)
    /// Returns false for complex expressions like binary expressions, invocations, etc.
    /// </summary>
    private static bool IsSimpleMemberAccessExpression(ExpressionSyntax expression)
    {
        // Simple member access: s.Name
        if (expression is MemberAccessExpressionSyntax)
            return true;

        // Conditional member access: s.Child?.Name (wrapped in ConditionalAccessExpression)
        if (expression is ConditionalAccessExpressionSyntax)
            return true;

        // All other types (binary, invocation, etc.) are complex
        return false;
    }

    /// <summary>
    /// Gets the source symbol from an expression (the property or field being accessed)
    /// </summary>
    private static (ISymbol? Symbol, string? ContainingTypeName) GetSourceSymbolFromExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel
    )
    {
        // For direct member access (e.g., s.Name or s.Child?.Name)
        // Navigate through the expression to find the leaf member access
        var leafMemberAccess = expression
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .LastOrDefault();

        if (leafMemberAccess != null)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(leafMemberAccess);
            var symbol = symbolInfo.Symbol;
            var containingTypeName = symbol?.ContainingType?.Name;

            // For chained access like s.Child?.Name, we want the type name to be the full path
            // Build the containing type name from the expression path
            var fullContainingTypeName = GetFullContainingTypeName(leafMemberAccess, semanticModel);

            return (symbol, fullContainingTypeName ?? containingTypeName);
        }

        // For conditional access (e.g., s.Child?.Name)
        var conditionalAccess = expression
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .LastOrDefault();

        if (conditionalAccess != null)
        {
            // Get the member binding expression (the part after ?.)
            var memberBinding = conditionalAccess
                .DescendantNodes()
                .OfType<MemberBindingExpressionSyntax>()
                .LastOrDefault();

            if (memberBinding != null)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(memberBinding);
                return (symbolInfo.Symbol, symbolInfo.Symbol?.ContainingType?.Name);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Gets the full containing type name from a member access expression path
    /// </summary>
    private static string? GetFullContainingTypeName(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel
    )
    {
        // Get the type of the expression being accessed
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        var expressionType = typeInfo.Type;

        if (expressionType != null)
        {
            // Remove nullable annotation for the type name
            var nonNullableType =
                RoslynTypeHelper.GetNonNullableType(expressionType) ?? expressionType;
            return nonNullableType.Name;
        }

        return null;
    }

    /// <summary>
    /// Builds the source reference string from an expression
    /// Converts lambda parameter to source type name and handles chained expressions
    /// </summary>
    private static string? BuildSourceReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel
    )
    {
        // First, normalize the expression string: remove comments and normalize whitespace
        var expressionStr = NormalizeExpressionString(expression.ToString());

        // Handle common patterns:
        // 1. Simple member access: s.Id -> SourceType.Id
        // 2. Chained access: s.Child?.Id -> SourceType.Child?.Id
        // 3. Linq chain: s.Children.Select(...).ToList() -> SourceType.Children.Select(...).ToList()

        // Find all identifier names that could be lambda parameters
        var firstIdentifier = expression
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .FirstOrDefault();

        if (firstIdentifier == null)
            return SimplifySourceReference(expressionStr);

        // Check if this identifier is a lambda parameter
        var symbolInfo = semanticModel.GetSymbolInfo(firstIdentifier);
        if (
            symbolInfo.Symbol is IParameterSymbol parameterSymbol
            && parameterSymbol.ContainingSymbol is IMethodSymbol methodSymbol
            && methodSymbol.MethodKind == MethodKind.LambdaMethod
        )
        {
            // This is a lambda parameter, get the type name to replace it
            var sourceTypeName = parameterSymbol.Type.Name;
            var paramName = firstIdentifier.Identifier.Text;

            // Replace all occurrences of the lambda parameter with the type name
            // Match: paramName followed by . or ?. but not as part of another identifier
            var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(paramName)}(?=[.?])";
            var result = System.Text.RegularExpressions.Regex.Replace(
                expressionStr,
                pattern,
                sourceTypeName
            );

            // If replacement happened, simplify and return
            if (result != expressionStr)
            {
                return SimplifySourceReference(result);
            }

            // If the expression is just the parameter, return the type name
            if (expressionStr == paramName)
            {
                return sourceTypeName;
            }
        }

        // Simplify complex expressions (Select, SelectMany, Where, etc.)
        return SimplifySourceReference(expressionStr);
    }

    /// <summary>
    /// Normalizes an expression string by removing comments and collapsing whitespace
    /// </summary>
    private static string NormalizeExpressionString(string expressionStr)
    {
        // Remove single-line comments (// comment)
        var result = System.Text.RegularExpressions.Regex.Replace(
            expressionStr,
            @"//.*$",
            "",
            System.Text.RegularExpressions.RegexOptions.Multiline
        );

        // Remove multi-line comments (/* comment */)
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"/\*.*?\*/",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline
        );

        // Normalize whitespace: replace multiple whitespace chars with a single space
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");

        return result.Trim();
    }

    /// <summary>
    /// Simplifies a source reference by abbreviating any method calls with arguments
    /// </summary>
    private static string SimplifySourceReference(string expressionStr)
    {
        // Abbreviate any method call with arguments using regex pattern: .MethodName(args) -> .MethodName(...)
        // This matches any method call pattern like .Select(x => ...), .Where(...), .CustomMethod(arg1, arg2), etc.
        var result = AbbreviateAllMethodCalls(expressionStr);
        return result;
    }

    /// <summary>
    /// Abbreviates all method calls with arguments (any method pattern like .MethodName(args))
    /// </summary>
    private static string AbbreviateAllMethodCalls(string input)
    {
        var result = new System.Text.StringBuilder();
        int pos = 0;

        while (pos < input.Length)
        {
            // Find the next method call pattern: .MethodName(
            // Method name must start with a letter and contain only letters/digits
            var dotIndex = input.IndexOf('.', pos);
            if (dotIndex < 0 || dotIndex >= input.Length - 2)
            {
                result.Append(input.Substring(pos));
                break;
            }

            // Check if this is a method call (letter follows the dot)
            var nextChar = input[dotIndex + 1];
            if (!char.IsLetter(nextChar))
            {
                result.Append(input.Substring(pos, dotIndex - pos + 1));
                pos = dotIndex + 1;
                continue;
            }

            // Find the end of the method name
            var methodNameEnd = dotIndex + 1;
            while (
                methodNameEnd < input.Length
                && (char.IsLetterOrDigit(input[methodNameEnd]) || input[methodNameEnd] == '_')
            )
            {
                methodNameEnd++;
            }

            // Check if there's an opening parenthesis after the method name
            if (methodNameEnd >= input.Length || input[methodNameEnd] != '(')
            {
                // Not a method call, just a property access
                result.Append(input.Substring(pos, methodNameEnd - pos));
                pos = methodNameEnd;
                continue;
            }

            // This is a method call with arguments
            var methodName = input.Substring(dotIndex + 1, methodNameEnd - dotIndex - 1);

            // Append everything before the method call
            result.Append(input.Substring(pos, dotIndex - pos));

            // Find the matching closing parenthesis
            var parenStart = methodNameEnd;
            var depth = 1;
            var endIndex = parenStart + 1;

            while (endIndex < input.Length && depth > 0)
            {
                if (input[endIndex] == '(')
                    depth++;
                else if (input[endIndex] == ')')
                    depth--;
                endIndex++;
            }

            // Check if the method has arguments (not just empty parentheses)
            var argsLength = Math.Max(0, endIndex - parenStart - 2);
            var argsContent =
                argsLength > 0 ? input.Substring(parenStart + 1, argsLength).Trim() : "";
            if (string.IsNullOrEmpty(argsContent))
            {
                // Empty parentheses, keep as is
                result.Append($".{methodName}()");
            }
            else
            {
                // Has arguments, abbreviate
                result.Append($".{methodName}(...)");
            }
            pos = endIndex;
        }

        return result.ToString();
    }
}
