using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCore.ExprGenerator;

internal record DtoProperty(
    string Name,
    bool IsNullable,
    string OriginalExpression,
    ITypeSymbol TypeSymbol,
    DtoStructure? NestedStructure
)
{
    public string TypeName => TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static DtoProperty? AnalyzeExpression(
        string propertyName,
        ExpressionSyntax expression,
        SemanticModel semanticModel
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type is null)
            return null;

        var propertyType = typeInfo.Type;
        var isNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated;

        // Check if nullable operator ?. is used
        var hasNullableAccess = HasNullableAccess(expression);

        // Detect nested Select (e.g., s.Childs.Select(...) or s.Childs.Select(...).ToList())
        DtoStructure? nestedStructure = null;
        // First, try to find Select invocation (handles both direct Select and chained methods like ToList)
        var selectInvocation = FindSelectInvocation(expression);
        if (selectInvocation is not null)
        {
            // Analyze anonymous type in lambda expression
            if (selectInvocation.ArgumentList.Arguments.Count > 0)
            {
                var lambdaArg = selectInvocation.ArgumentList.Arguments[0].Expression;
                if (
                    lambdaArg is LambdaExpressionSyntax nestedLambda
                    && nestedLambda.Body is AnonymousObjectCreationExpressionSyntax nestedAnonymous
                )
                {
                    // Get collection element type from the Select's source
                    if (
                        selectInvocation.Expression
                        is MemberAccessExpressionSyntax selectMemberAccess
                    )
                    {
                        var collectionType = semanticModel
                            .GetTypeInfo(selectMemberAccess.Expression)
                            .Type;
                        if (
                            collectionType is INamedTypeSymbol namedCollectionType
                            && namedCollectionType.TypeArguments.Length > 0
                        )
                        {
                            var elementType = namedCollectionType.TypeArguments[0];
                            nestedStructure = DtoStructure.AnalyzeAnonymousType(
                                nestedAnonymous,
                                semanticModel,
                                elementType
                            );
                        }
                    }
                }
            }
        }

        return new DtoProperty(
            Name: propertyName,
            IsNullable: isNullable || hasNullableAccess,
            OriginalExpression: expression.ToString(),
            TypeSymbol: propertyType,
            NestedStructure: nestedStructure
        );
    }

    private static bool HasNullableAccess(ExpressionSyntax expression)
    {
        // Check if ?. operator is used
        return expression.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().Any();
    }

    private static InvocationExpressionSyntax? FindSelectInvocation(ExpressionSyntax expression)
    {
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
