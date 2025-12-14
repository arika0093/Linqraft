using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Transformation;

/// <summary>
/// Transformer that fully qualifies all type references within an expression.
/// Handles object creation expressions, generic type arguments, and type references.
/// </summary>
internal class FullyQualifyingTransformer : IExpressionTransformer
{
    /// <inheritdoc/>
    public int Priority => 50;

    /// <inheritdoc/>
    public bool CanTransform(TransformContext context)
    {
        // Always process - this transformer handles multiple scenarios
        return true;
    }

    /// <inheritdoc/>
    public ExpressionSyntax Transform(TransformContext context)
    {
        var syntax = context.Expression;
        var semanticModel = context.SemanticModel;

        // Build replacements
        var replacements = new List<(string Original, string Replacement, int Start)>();

        // Process object creations
        ProcessObjectCreations(syntax, semanticModel, replacements);

        // Process static method invocations
        ProcessStaticInvocations(syntax, semanticModel, replacements);

        // Process collection expressions (C# 12)
        ProcessCollectionExpressions(syntax, semanticModel, replacements);

        // Process static/const/enum member accesses
        ProcessMemberAccesses(syntax, semanticModel, replacements);

        // Process unqualified identifiers
        ProcessIdentifiers(syntax, semanticModel, replacements);

        // Apply replacements
        if (replacements.Count == 0)
        {
            return syntax;
        }

        return ApplyReplacements(syntax, replacements);
    }

    private static void ProcessObjectCreations(
        ExpressionSyntax syntax,
        SemanticModel semanticModel,
        List<(string Original, string Replacement, int Start)> replacements)
    {
        var objectCreations = syntax
            .DescendantNodesAndSelf()
            .OfType<ObjectCreationExpressionSyntax>()
            .ToList();

        foreach (var objectCreation in objectCreations)
        {
            var typeInfo = semanticModel.GetTypeInfo(objectCreation);
            if (typeInfo.Type is not INamedTypeSymbol typeSymbol)
                continue;

            var fullyQualifiedTypeName = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            var originalTypeSyntax = objectCreation.Type;
            if (originalTypeSyntax is null)
                continue;

            var originalTypeName = originalTypeSyntax.ToString();

            if (!originalTypeName.StartsWith("global::"))
            {
                replacements.Add(
                    (originalTypeName, fullyQualifiedTypeName, originalTypeSyntax.SpanStart)
                );
            }
        }
    }

    private static void ProcessStaticInvocations(
        ExpressionSyntax syntax,
        SemanticModel semanticModel,
        List<(string Original, string Replacement, int Start)> replacements)
    {
        var invocations = syntax
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .ToList();

        foreach (var invocation in invocations)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                continue;

            if (!methodSymbol.IsStatic)
                continue;

            var expression = invocation.Expression;
            if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                var containingType = methodSymbol.ContainingType;
                if (containingType is null)
                    continue;

                var fullTypeName = containingType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                var methodName = methodSymbol.Name;

                if (memberAccess.Name is GenericNameSyntax && methodSymbol.IsGenericMethod)
                {
                    var typeArgs = methodSymbol.TypeArguments
                        .Select(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .ToList();
                    var fullyQualifiedInvocation =
                        $"{fullTypeName}.{methodName}<{string.Join(", ", typeArgs)}>";
                    var original = memberAccess.ToString();

                    if (!original.StartsWith("global::"))
                    {
                        replacements.Add(
                            (original, fullyQualifiedInvocation, memberAccess.SpanStart)
                        );
                    }
                }
                else
                {
                    var fullyQualifiedMethod = $"{fullTypeName}.{methodName}";
                    var original = memberAccess.ToString();

                    if (!original.StartsWith("global::"))
                    {
                        replacements.Add((original, fullyQualifiedMethod, memberAccess.SpanStart));
                    }
                }
            }
        }
    }

    private static void ProcessCollectionExpressions(
        ExpressionSyntax syntax,
        SemanticModel semanticModel,
        List<(string Original, string Replacement, int Start)> replacements)
    {
        var collectionExpressions = syntax
            .DescendantNodesAndSelf()
            .OfType<CollectionExpressionSyntax>()
            .ToList();

        foreach (var collectionExpr in collectionExpressions)
        {
            if (collectionExpr.Elements.Count != 0)
                continue;

            var typeInfo = semanticModel.GetTypeInfo(collectionExpr);
            if (typeInfo.ConvertedType is not INamedTypeSymbol targetType)
                continue;

            var elementType = RoslynHelpers.RoslynTypeHelper.GetGenericTypeArgument(targetType, 0);
            if (elementType is null)
                continue;

            var fullyQualifiedElementType = elementType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            var emptyExpression =
                $"global::System.Linq.Enumerable.Empty<{fullyQualifiedElementType}>()";
            var original = collectionExpr.ToString();

            replacements.Add((original, emptyExpression, collectionExpr.SpanStart));
        }
    }

    private static void ProcessMemberAccesses(
        ExpressionSyntax syntax,
        SemanticModel semanticModel,
        List<(string Original, string Replacement, int Start)> replacements)
    {
        var memberAccesses = syntax
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .ToList();

        foreach (var memberAccess in memberAccesses)
        {
            if (memberAccess.Kind() != SyntaxKind.SimpleMemberAccessExpression)
                continue;

            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);

            if (symbolInfo.Symbol is IFieldSymbol fieldSymbol
                && (fieldSymbol.IsStatic || fieldSymbol.IsConst))
            {
                var containingType = fieldSymbol.ContainingType;
                if (containingType is not null)
                {
                    var fullTypeName = containingType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    var memberName = fieldSymbol.Name;
                    var fullyQualified = $"{fullTypeName}.{memberName}";
                    var original = memberAccess.ToString();

                    if (!original.StartsWith("global::"))
                    {
                        replacements.Add((original, fullyQualified, memberAccess.SpanStart));
                    }
                }
            }
            else if (symbolInfo.Symbol is IPropertySymbol propertySymbol && propertySymbol.IsStatic)
            {
                var containingType = propertySymbol.ContainingType;
                if (containingType is not null)
                {
                    var fullTypeName = containingType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    var memberName = propertySymbol.Name;
                    var fullyQualified = $"{fullTypeName}.{memberName}";
                    var original = memberAccess.ToString();

                    if (!original.StartsWith("global::"))
                    {
                        replacements.Add((original, fullyQualified, memberAccess.SpanStart));
                    }
                }
            }
        }
    }

    private static void ProcessIdentifiers(
        ExpressionSyntax syntax,
        SemanticModel semanticModel,
        List<(string Original, string Replacement, int Start)> replacements)
    {
        var identifiers = syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().ToList();

        foreach (var identifier in identifiers)
        {
            if (identifier.Parent is MemberAccessExpressionSyntax)
                continue;

            var symbolInfo = semanticModel.GetSymbolInfo(identifier);

            if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
            {
                // Check for enum values
                if (fieldSymbol.ContainingType?.TypeKind == TypeKind.Enum)
                {
                    var containingType = fieldSymbol.ContainingType;
                    var fullTypeName = containingType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    var memberName = fieldSymbol.Name;
                    var original = identifier.ToString();
                    var fullyQualified = $"{fullTypeName}.{memberName}";

                    if (!original.StartsWith("global::"))
                    {
                        replacements.Add((original, fullyQualified, identifier.SpanStart));
                    }
                }
                // Check for public/internal const fields
                else if (fieldSymbol.IsConst && fieldSymbol.ContainingType is not null)
                {
                    if (fieldSymbol.DeclaredAccessibility == Accessibility.Public
                        || fieldSymbol.DeclaredAccessibility == Accessibility.Internal)
                    {
                        var containingType = fieldSymbol.ContainingType;
                        var fullTypeName = containingType.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        );
                        var memberName = fieldSymbol.Name;
                        var original = identifier.ToString();
                        var fullyQualified = $"{fullTypeName}.{memberName}";

                        if (!original.StartsWith("global::"))
                        {
                            replacements.Add((original, fullyQualified, identifier.SpanStart));
                        }
                    }
                }
                // Check for public/internal static fields
                else if (fieldSymbol.IsStatic && fieldSymbol.ContainingType is not null)
                {
                    if (fieldSymbol.DeclaredAccessibility == Accessibility.Public
                        || fieldSymbol.DeclaredAccessibility == Accessibility.Internal)
                    {
                        var containingType = fieldSymbol.ContainingType;
                        var fullTypeName = containingType.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        );
                        var memberName = fieldSymbol.Name;
                        var original = identifier.ToString();
                        var fullyQualified = $"{fullTypeName}.{memberName}";

                        if (!original.StartsWith("global::"))
                        {
                            replacements.Add((original, fullyQualified, identifier.SpanStart));
                        }
                    }
                }
            }
            else if (symbolInfo.Symbol is IPropertySymbol propertySymbol)
            {
                if (propertySymbol.IsStatic)
                {
                    if (propertySymbol.DeclaredAccessibility == Accessibility.Public
                        || propertySymbol.DeclaredAccessibility == Accessibility.Internal)
                    {
                        var containingType = propertySymbol.ContainingType;
                        var fullTypeName = containingType!.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        );
                        var memberName = propertySymbol.Name;
                        var original = identifier.ToString();
                        var fullyQualified = $"{fullTypeName}.{memberName}";

                        if (!original.StartsWith("global::"))
                        {
                            replacements.Add((original, fullyQualified, identifier.SpanStart));
                        }
                    }
                }
            }
        }
    }

    private static ExpressionSyntax ApplyReplacements(
        ExpressionSyntax syntax,
        List<(string Original, string Replacement, int Start)> replacements)
    {
        // Sort replacements by position (descending) to apply from end to start
        var orderedReplacements = replacements
            .OrderByDescending(r => r.Start)
            .ToList();

        var result = syntax.ToString();

        foreach (var (original, replacement, start) in orderedReplacements)
        {
            var offset = start - syntax.SpanStart;

            if (offset >= 0 && offset + original.Length <= result.Length)
            {
                var substring = result.Substring(offset, original.Length);
                if (substring == original)
                {
                    result =
                        result.Substring(0, offset)
                        + replacement
                        + result.Substring(offset + original.Length);
                }
            }
        }

        // Parse the result back to an expression
        return SyntaxFactory.ParseExpression(result);
    }
}
