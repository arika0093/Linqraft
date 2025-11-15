using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft;

internal record DtoProperty(
    string Name,
    bool IsNullable,
    string OriginalExpression,
    ExpressionSyntax OriginalSyntax,
    ITypeSymbol TypeSymbol,
    DtoStructure? NestedStructure
)
{
    public string TypeName => TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static DtoProperty? AnalyzeExpression(
        string propertyName,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IPropertySymbol? targetProperty = null
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type is null)
            return null;

        var propertyType = typeInfo.Type;

        // If targetProperty is provided (for predefined DTOs), use its type information
        NullableAnnotation nullableAnnotation;
        if (targetProperty is not null)
        {
            nullableAnnotation = targetProperty.Type.NullableAnnotation;
            propertyType = targetProperty.Type;
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

        return new DtoProperty(
            Name: propertyName,
            IsNullable: isNullable || hasNullableAccess,
            OriginalExpression: expression.ToString(),
            OriginalSyntax: expression,
            TypeSymbol: propertyType,
            NestedStructure: nestedStructure
        );
    }

    /// <summary>
    /// Syntax-based heuristic to determine if null check should be generated.
    /// This is a fallback for when Roslyn's NullableAnnotation is unreliable (e.g., in Visual Studio).
    /// see https://github.com/arika0093/Linqraft/issues/22
    /// </summary>
    private static bool ShouldGenerateNullCheckFromSyntax(ExpressionSyntax expression)
    {
        // 1. If ?. is used, definitely needs null check
        if (expression.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>().Any())
        {
            return true;
        }

        // 2. Check for nested member access (e.g., x.Child.Name)
        // Count member access depth (excluding lambda parameters)
        var memberAccesses = expression
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(ma =>
            {
                // Exclude nested Select lambdas
                var parentLambda = ma.Ancestors().OfType<LambdaExpressionSyntax>().FirstOrDefault();
                var topLambda = expression
                    .Ancestors()
                    .OfType<LambdaExpressionSyntax>()
                    .FirstOrDefault();
                return parentLambda == topLambda;
            })
            .ToList();

        // If there are multiple levels of member access (e.g., s.Child.Name has 2 levels)
        // generate null check for safety
        if (memberAccesses.Count >= 2)
        {
            // Check if the chain starts from a lambda parameter
            var firstAccess = memberAccesses.FirstOrDefault();
            if (firstAccess?.Expression is IdentifierNameSyntax)
            {
                // s.Child.Name - has intermediate navigation, needs null check
                return true;
            }
        }

        return false;
    }

    private static bool HasNullableAccess(ExpressionSyntax expression)
    {
        // Check if ?. operator is used at the top level (excluding nested lambdas)
        // We need to exclude ConditionalAccessExpressionSyntax that are inside lambda expressions
        // because those apply to the nested properties, not the outer property
        var conditionalAccesses = expression
            .DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>();

        foreach (var conditionalAccess in conditionalAccesses)
        {
            // Check if this conditional access is inside a lambda expression
            var ancestors = conditionalAccess.Ancestors();
            var isInsideLambda = ancestors
                .TakeWhile(n => n != expression) // Only check ancestors up to the root expression
                .OfType<LambdaExpressionSyntax>()
                .Any();

            // If not inside a lambda, this is a top-level nullable access
            if (!isInsideLambda)
            {
                return true;
            }
        }

        return false;
    }

    private static InvocationExpressionSyntax? FindSelectInvocation(ExpressionSyntax expression)
    {
        // Handle binary expressions (e.g., ?? operator): s.OrderItems?.Select(...) ?? []
        if (expression is BinaryExpressionSyntax binaryExpr)
        {
            // Check left side for Select invocation
            var leftResult = FindSelectInvocation(binaryExpr.Left);
            if (leftResult is not null)
                return leftResult;
        }

        // Handle conditional access (?.): s.OrderItems?.Select(...)
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            // The WhenNotNull part contains the actual method call
            return FindSelectInvocation(conditionalAccess.WhenNotNull);
        }

        // Handle member binding expression (part of ?. expression): .Select(...)
        if (expression is MemberBindingExpressionSyntax)
        {
            // This is the .Select part of ?.Select - we need to look at the parent
            return null;
        }

        // Handle invocation binding expression (part of ?. expression): Select(...)
        if (
            expression is InvocationExpressionSyntax invocationBinding
            && invocationBinding.Expression is MemberBindingExpressionSyntax memberBinding
            && memberBinding.Name.Identifier.Text == "Select"
        )
        {
            return invocationBinding;
        }

        // Direct Select invocation: s.Childs.Select(...)
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (
                invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "Select"
            )
            {
                return invocation;
            }

            // Chained method call (e.g., ToList, ToArray, etc.): s.Childs.Select(...).ToList()
            // The invocation is for ToList, but we need to find Select in its expression
            if (invocation.Expression is MemberAccessExpressionSyntax chainedMemberAccess)
            {
                // Recursively search in the expression part (before the chained method)
                return FindSelectInvocation(chainedMemberAccess.Expression);
            }
        }

        return null;
    }
}
