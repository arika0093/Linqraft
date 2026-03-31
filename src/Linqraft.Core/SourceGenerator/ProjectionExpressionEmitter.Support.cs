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
    // Support helpers handle capture substitution, fallback typing, and syntax rewriting details.
    /// <summary>
    /// Attempts to emit capture replacement.
    /// </summary>
    private bool TryEmitCaptureReplacement(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (_captureEntries.Count == 0 || !BelongsToSemanticModel(expression))
        {
            return false;
        }

        var normalizedExpression = NormalizeExpressionText(expression);
        var currentRootSymbol = GetRootSymbol(expression, cancellationToken);

        foreach (var captureEntry in _captureEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !string.Equals(
                    captureEntry.ExpressionText,
                    normalizedExpression,
                    System.StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(captureEntry.RootSymbol, currentRootSymbol))
            {
                continue;
            }

            rewritten = captureEntry.LocalName;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates empty collection fallback.
    /// </summary>
    private static string CreateEmptyCollectionFallback(
        string typeName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Collection literals can lose their concrete target shape while the selector is being
        // rewritten, so recover the most specific empty value that still preserves semantics.
        var normalizedTypeName = typeName.EndsWith("?", System.StringComparison.Ordinal)
            ? typeName[..^1]
            : typeName;

        if (normalizedTypeName.EndsWith("[]", System.StringComparison.Ordinal))
        {
            var elementType = normalizedTypeName[..^2];
            return $"global::System.Array.Empty<{elementType}>()";
        }

        if (
            normalizedTypeName.StartsWith(
                "global::System.Collections.Generic.List<",
                System.StringComparison.Ordinal
            )
        )
        {
            return $"new {normalizedTypeName}()";
        }

        if (
            TryGetSingleGenericArgument(
                normalizedTypeName,
                out var genericArgument,
                cancellationToken
            )
        )
        {
            return $"global::System.Linq.Enumerable.Empty<{genericArgument}>()";
        }

        // The public docs do not define a fallback constructor strategy for custom collection types.
        return $"new {normalizedTypeName}()";
    }

    /// <summary>
    /// Resolves collection target type name.
    /// </summary>
    private string ResolveCollectionTargetTypeName(
        CollectionExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var targetType = GetExpressionType(expression, cancellationToken);
        if (
            targetType is not null
            && targetType is not IErrorTypeSymbol
            && !ContainsAnonymousType(targetType)
        )
        {
            return targetType.ToFullyQualifiedTypeName();
        }

        if (
            TryResolveCollectionTargetTypeNameFromContext(
                expression,
                out var contextualTypeName,
                cancellationToken
            )
        )
        {
            return contextualTypeName;
        }

        return _rootTypeName;
    }

    /// <summary>
    /// Attempts to resolve collection target type name from context.
    /// </summary>
    private bool TryResolveCollectionTargetTypeNameFromContext(
        SyntaxNode node,
        out string typeName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        switch (node.Parent)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return TryResolveCollectionTargetTypeNameFromContext(
                    parenthesized,
                    out typeName,
                    cancellationToken
                );
            case BinaryExpressionSyntax binaryExpression
                when binaryExpression.IsKind(SyntaxKind.CoalesceExpression):
            {
                var sibling = ReferenceEquals(binaryExpression.Left, node)
                    ? binaryExpression.Right
                    : binaryExpression.Left;
                return TryGetContextualCollectionTypeName(sibling, out typeName, cancellationToken);
            }
            case ConditionalExpressionSyntax conditionalExpression:
            {
                var sibling = ReferenceEquals(conditionalExpression.WhenTrue, node)
                    ? conditionalExpression.WhenFalse
                    : conditionalExpression.WhenTrue;
                return TryGetContextualCollectionTypeName(sibling, out typeName, cancellationToken);
            }
            case AssignmentExpressionSyntax assignment when ReferenceEquals(assignment.Right, node):
                return TryResolveAssignedTypeName(assignment.Left, out typeName, cancellationToken);
            default:
                typeName = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// Attempts to get contextual collection type name.
    /// </summary>
    private bool TryGetContextualCollectionTypeName(
        ExpressionSyntax expression,
        out string typeName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var expressionType = GetExpressionType(expression, cancellationToken);
        if (
            expressionType is not null
            && expressionType is not IErrorTypeSymbol
            && !ContainsAnonymousType(expressionType)
        )
        {
            typeName = expressionType.ToFullyQualifiedTypeName();
            return true;
        }

        if (expressionType is null || ContainsAnonymousType(expressionType))
        {
            typeName = _rootTypeName;
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to resolve assigned type name.
    /// </summary>
    private bool TryResolveAssignedTypeName(
        ExpressionSyntax expression,
        out string typeName,
        CancellationToken cancellationToken = default
    )
    {
        if (!BelongsToSemanticModel(expression))
        {
            typeName = string.Empty;
            return false;
        }

        cancellationToken = ResolveCancellationToken(cancellationToken);
        var symbolType = _semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            IFieldSymbol fieldSymbol => fieldSymbol.Type,
            ILocalSymbol localSymbol => localSymbol.Type,
            IParameterSymbol parameterSymbol => parameterSymbol.Type,
            IPropertySymbol propertySymbol => propertySymbol.Type,
            _ => null,
        };

        if (symbolType is not null && symbolType is not IErrorTypeSymbol)
        {
            typeName = symbolType.ToFullyQualifiedTypeName();
            return true;
        }

        var expressionType =
            _semanticModel.GetTypeInfo(expression, cancellationToken).Type
            ?? _semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            typeName = expressionType.ToFullyQualifiedTypeName();
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to get single generic argument.
    /// </summary>
    private static bool TryGetSingleGenericArgument(
        string typeName,
        out string argument,
        CancellationToken cancellationToken = default
    )
    {
        argument = string.Empty;
        var start = typeName.IndexOf('<');
        var end = typeName.LastIndexOf('>');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var candidate = typeName[(start + 1)..end];
        var depth = 0;
        foreach (var character in candidate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (character)
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return false;
            }
        }

        argument = candidate;
        return true;
    }

    /// <summary>
    /// Determines whether the node belongs to the semantic model.
    /// </summary>
    private bool BelongsToSemanticModel(SyntaxNode node)
    {
        return node.SyntaxTree == _semanticModel.SyntaxTree;
    }

    /// <summary>
    /// Normalizes expression text.
    /// </summary>
    private static string NormalizeExpressionText(ExpressionSyntax expression)
    {
        return expression.WithoutTrivia().ToString();
    }

    /// <summary>
    /// Gets root symbol.
    /// </summary>
    private ISymbol? GetRootSymbol(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var rootExpression = GetRootExpression(expression);
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return _semanticModel.GetSymbolInfo(rootExpression, cancellationToken).Symbol;
    }

    /// <summary>
    /// Gets root expression.
    /// </summary>
    private static ExpressionSyntax GetRootExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => GetRootExpression(memberAccess.Expression),
            ConditionalAccessExpressionSyntax conditionalAccess => GetRootExpression(
                conditionalAccess.Expression
            ),
            ElementAccessExpressionSyntax elementAccess => GetRootExpression(
                elementAccess.Expression
            ),
            InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess =>
                GetRootExpression(memberAccess.Expression),
            InvocationExpressionSyntax invocation => invocation,
            _ => expression,
        };
    }

    /// <summary>
    /// Gets expression type.
    /// </summary>
    private ITypeSymbol? GetExpressionType(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        if (!BelongsToSemanticModel(expression))
        {
            return null;
        }

        cancellationToken = ResolveCancellationToken(cancellationToken);
        var typeInfo = _semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.Type ?? typeInfo.ConvertedType;
    }

    /// <summary>
    /// Resolves fallback type.
    /// </summary>
    private ITypeSymbol? ResolveFallbackType(
        TypeSyntax type,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var symbol = _semanticModel.GetSymbolInfo(type, cancellationToken).Symbol as ITypeSymbol;
        if (symbol is not null && symbol is not IErrorTypeSymbol)
        {
            return symbol;
        }

        return type switch
        {
            IdentifierNameSyntax identifier => _semanticModel
                .LookupNamespacesAndTypes(type.SpanStart, name: identifier.Identifier.ValueText)
                .OfType<ITypeSymbol>()
                .FirstOrDefault(),
            QualifiedNameSyntax qualifiedName => _semanticModel
                .LookupNamespacesAndTypes(
                    type.SpanStart,
                    name: qualifiedName.Right.Identifier.ValueText
                )
                .OfType<ITypeSymbol>()
                .FirstOrDefault(candidate =>
                    candidate
                        .ToDisplayString()
                        .EndsWith(qualifiedName.ToString(), System.StringComparison.Ordinal)
                ),
            AliasQualifiedNameSyntax aliasQualifiedName => _semanticModel
                .LookupNamespacesAndTypes(
                    type.SpanStart,
                    name: aliasQualifiedName.Name.Identifier.ValueText
                )
                .OfType<ITypeSymbol>()
                .FirstOrDefault(),
            GenericNameSyntax genericName => _semanticModel
                .LookupNamespacesAndTypes(type.SpanStart, name: genericName.Identifier.ValueText)
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(candidate =>
                    candidate.Arity == genericName.TypeArgumentList.Arguments.Count
                ),
            _ => null,
        };
    }

    /// <summary>
    /// Gets expression type name.
    /// </summary>
    private string GetExpressionTypeName(
        ExpressionSyntax expression,
        ITypeSymbol? expressionType,
        out bool canCast
    )
    {
        canCast = true;
        if (!BelongsToSemanticModel(expression))
        {
            if (ReferenceEquals(expression, _rootExpression))
            {
                return _rootTypeName;
            }

            canCast = false;
            return "object";
        }

        var type = expressionType;
        if (type is null)
        {
            canCast = false;
            return ReferenceEquals(expression, _rootExpression) ? _rootTypeName : "object";
        }

        if (ReferenceEquals(expression, _rootExpression) && ContainsAnonymousType(type))
        {
            return _rootTypeName;
        }

        if (type.IsAnonymousType)
        {
            canCast = false;
            return "object";
        }

        return type.ToFullyQualifiedTypeName();
    }

    /// <summary>
    /// Determines whether the type contains an anonymous type.
    /// </summary>
    private static bool ContainsAnonymousType(ITypeSymbol type)
    {
        if (type.IsAnonymousType)
        {
            return true;
        }

        return type switch
        {
            IArrayTypeSymbol arrayType => ContainsAnonymousType(arrayType.ElementType),
            INamedTypeSymbol namedType => namedType.TypeArguments.Any(ContainsAnonymousType),
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether the expression should omit conditional cast.
    /// </summary>
    private static bool ShouldOmitConditionalCast(ExpressionSyntax expression, ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (!ContainsProjectionLikeInvocation(expression))
        {
            return false;
        }

        return type is IArrayTypeSymbol || SymbolNameHelper.IsEnumerable(type);
    }

    /// <summary>
    /// Determines whether the expression contains a projection like invocation.
    /// </summary>
    private static bool ContainsProjectionLikeInvocation(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
            {
                var name = GetInvocationName(invocation.Expression);
                return name is "Select" or "SelectMany" or "ToList" or "ToArray";
            });
    }

    /// <summary>
    /// Determines whether the expression is an empty collection expression.
    /// </summary>
    private static bool IsEmptyCollectionExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            CollectionExpressionSyntax collectionExpression => collectionExpression.Elements.Count
                == 0,
            InvocationExpressionSyntax invocation => GetInvocationName(invocation.Expression)
                == "Empty"
                && invocation.ArgumentList.Arguments.Count == 0,
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether the expression should use collection fallback.
    /// </summary>
    private static bool ShouldUseCollectionFallback(ExpressionSyntax expression, ITypeSymbol? type)
    {
        if (type is null || type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (ContainsAnonymousType(type))
        {
            return false;
        }

        if (!ContainsProjectionLikeInvocation(expression))
        {
            return false;
        }

        return type is IArrayTypeSymbol || SymbolNameHelper.IsEnumerable(type);
    }

    /// <summary>
    /// Attempts to format fluent access.
    /// </summary>
    private static bool TryFormatFluentAccess(
        string receiver,
        string access,
        out string formatted,
        CancellationToken cancellationToken = default
    )
    {
        formatted = access;
        if (!access.StartsWith(receiver, System.StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = access[receiver.Length..];
        if (string.IsNullOrEmpty(remainder) || remainder[0] is not ('.' or '['))
        {
            return false;
        }

        var segments = SplitTopLevelSegments(remainder, cancellationToken);
        if (segments.Count == 0 || !segments.Any(segment => segment.Contains('(')))
        {
            return false;
        }

        formatted = string.Join("\n", new[] { receiver }.Concat(segments));
        return true;
    }

    /// <summary>
    /// Splits top level segments.
    /// </summary>
    private static List<string> SplitTopLevelSegments(
        string value,
        CancellationToken cancellationToken = default
    )
    {
        var segments = new List<string>();
        var start = 0;
        var parentheses = 0;
        var braces = 0;
        var brackets = 0;
        var angles = 0;

        for (var index = 0; index < value.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = value[index];
            switch (character)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '<':
                    angles++;
                    break;
                case '>':
                    if (angles > 0)
                    {
                        angles--;
                    }
                    break;
                case '.'
                    when index != 0
                        && parentheses == 0
                        && braces == 0
                        && brackets == 0
                        && angles == 0:
                    segments.Add(value[start..index]);
                    start = index;
                    break;
            }
        }

        if (start < value.Length)
        {
            segments.Add(value[start..]);
        }

        return segments;
    }

    /// <summary>
    /// Handles wrap cast expression.
    /// </summary>
    private static string WrapCastExpression(string castPrefix, string expression)
    {
        if (castPrefix == "()")
        {
            return expression;
        }

        if (!ContainsLineBreak(expression))
        {
            return $"{castPrefix}({expression})";
        }

        return string.Join("\n", $"{castPrefix}(", IndentAllLines(expression), ")");
    }

    /// <summary>
    /// Emits cast expression.
    /// </summary>
    private string EmitCastExpression(
        CastExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var operand =
            expression.Expression is ParenthesizedExpressionSyntax parenthesized
            && CanOmitParenthesizedCastOperand(parenthesized.Expression)
                ? parenthesized.Expression
                : expression.Expression;

        return WrapCastExpression(
            $"({QualifyType(expression.Type, cancellationToken)})",
            Emit(operand, cancellationToken)
        );
    }

    /// <summary>
    /// Determines whether the expression can omit parenthesized cast operand.
    /// </summary>
    private static bool CanOmitParenthesizedCastOperand(ExpressionSyntax expression)
    {
        return expression
            is IdentifierNameSyntax
                or GenericNameSyntax
                or ThisExpressionSyntax
                or BaseExpressionSyntax
                or LiteralExpressionSyntax
                or InterpolatedStringExpressionSyntax
                or TypeOfExpressionSyntax
                or DefaultExpressionSyntax
                or InvocationExpressionSyntax
                or MemberAccessExpressionSyntax
                or ElementAccessExpressionSyntax
                or ObjectCreationExpressionSyntax
                or AnonymousObjectCreationExpressionSyntax
                or CollectionExpressionSyntax
                or ImplicitArrayCreationExpressionSyntax
                or ArrayCreationExpressionSyntax;
    }

    /// <summary>
    /// Builds initializer expression.
    /// </summary>
    private static string BuildInitializerExpression(
        string header,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default
    )
    {
        if (!ShouldExpandInitializer(items))
        {
            return items.Count == 0
                ? $"{header} {{ }}"
                : $"{header} {{ {string.Join(", ", items)} }}";
        }

        var builder = new IndentedStringBuilder();
        builder.AppendLine($"{header} {{", cancellationToken);
        using (builder.Indent())
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendMultilineItem(builder, item, ",", cancellationToken);
            }
        }

        builder.Append("}");
        return builder.ToString();
    }

    /// <summary>
    /// Builds initializer body.
    /// </summary>
    private static string BuildInitializerBody(
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default
    )
    {
        if (!ShouldExpandInitializer(items))
        {
            return items.Count == 0 ? "{ }" : $"{{ {string.Join(", ", items)} }}";
        }

        var builder = new IndentedStringBuilder();
        builder.AppendLine("{", cancellationToken);
        using (builder.Indent())
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendMultilineItem(builder, item, ",", cancellationToken);
            }
        }

        builder.Append("}");
        return builder.ToString();
    }

    /// <summary>
    /// Determines whether the items should expand initializer.
    /// </summary>
    private static bool ShouldExpandInitializer(IReadOnlyList<string> items)
    {
        return items.Count > 1 || items.Any(ContainsLineBreak);
    }

    /// <summary>
    /// Appends multiline item.
    /// </summary>
    private static void AppendMultilineItem(
        IndentedStringBuilder builder,
        string value,
        string suffix,
        CancellationToken cancellationToken = default
    )
    {
        var lines = SplitLines(value);
        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = index == lines.Length - 1 ? lines[index] + suffix : lines[index];
            builder.AppendLine(line, cancellationToken);
        }
    }

    /// <summary>
    /// Builds invocation expression.
    /// </summary>
    private static string BuildInvocationExpression(
        string header,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default
    )
    {
        if (arguments.Count == 0)
        {
            return $"{header}()";
        }

        if (arguments.Count == 1 && !ContainsLineBreak(arguments[0]))
        {
            return $"{header}({arguments[0]})";
        }

        var builder = new IndentedStringBuilder();
        builder.AppendLine($"{header}(", cancellationToken);
        using (builder.Indent())
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendMultilineItem(
                    builder,
                    arguments[index],
                    index == arguments.Count - 1 ? string.Empty : ",",
                    cancellationToken
                );
            }
        }

        builder.Append(")");
        return builder.ToString();
    }

    /// <summary>
    /// Appends value with continuation.
    /// </summary>
    private static string AppendValueWithContinuation(string prefix, string value)
    {
        var lines = SplitLines(value);
        if (lines.Length == 0)
        {
            return prefix;
        }

        if (lines.Length == 1)
        {
            return prefix + lines[0];
        }

        var formattedLines = new List<string> { prefix + lines[0] };
        formattedLines.AddRange(lines.Skip(1).Select(IndentAllLines));
        return string.Join("\n", formattedLines);
    }

    /// <summary>
    /// Appends value inline.
    /// </summary>
    private static string AppendValueInline(string prefix, string value)
    {
        var lines = SplitLines(value);
        if (lines.Length == 0)
        {
            return prefix;
        }

        lines[0] = prefix + lines[0];
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Handles indent all lines.
    /// </summary>
    private static string IndentAllLines(string value, int indentLevel = 1)
    {
        var prefix = new string(' ', indentLevel * 4);
        return string.Join("\n", SplitLines(value).Select(line => prefix + line));
    }

    /// <summary>
    /// Determines whether the value contains a line break.
    /// </summary>
    private static bool ContainsLineBreak(string value)
    {
        return value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
    }

    /// <summary>
    /// Splits lines.
    /// </summary>
    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    /// <summary>
    /// Gets anonymous member name.
    /// </summary>
    private static string GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        return AnonymousMemberNameResolver.Get(initializer.Expression);
    }

    /// <summary>
    /// Gets invocation name.
    /// </summary>
    private static string GetInvocationName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Determines whether the expression contains a reduced extension invocation.
    /// </summary>
    private bool ContainsReducedExtensionInvocation(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        foreach (
            var invocation in expression
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!BelongsToSemanticModel(invocation))
            {
                continue;
            }

            var symbolInfo = _semanticModel.GetSymbolInfo(invocation, cancellationToken);
            var methodSymbol =
                symbolInfo.Symbol as IMethodSymbol
                ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (methodSymbol?.ReducedFrom is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rewrites projectable inlining.
    /// </summary>
    private sealed class ProjectableInliningRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly INamedTypeSymbol _declaringType;
        private readonly ExpressionSyntax? _receiverExpression;
        private readonly IReadOnlyDictionary<ISymbol, ExpressionSyntax> _parameterBindings;

        /// <summary>
        /// Initializes a new instance of the ProjectableInliningRewriter class.
        /// </summary>
        public ProjectableInliningRewriter(
            SemanticModel semanticModel,
            INamedTypeSymbol declaringType,
            ExpressionSyntax? receiverExpression,
            IReadOnlyDictionary<ISymbol, ExpressionSyntax>? parameterBindings
        )
        {
            _semanticModel = semanticModel;
            _declaringType = declaringType;
            _receiverExpression = receiverExpression;
            _parameterBindings =
                parameterBindings
                ?? new Dictionary<ISymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);
        }

        /// <summary>
        /// Visits this expression.
        /// </summary>
        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node)
        {
            return _receiverExpression is null
                ? base.VisitThisExpression(node)
                : Parenthesize(_receiverExpression).WithTriviaFrom(node);
        }

        /// <summary>
        /// Visits identifier name.
        /// </summary>
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.SyntaxTree != _semanticModel.SyntaxTree)
            {
                return base.VisitIdentifierName(node);
            }

            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is not null && _parameterBindings.TryGetValue(symbol, out var replacement))
            {
                return Parenthesize(replacement).WithTriviaFrom(node);
            }

            if (
                _receiverExpression is not null
                && symbol is IPropertySymbol or IFieldSymbol
                && !symbol.IsStatic
                && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, _declaringType)
            )
            {
                return SyntaxFactory
                    .MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        Parenthesize(_receiverExpression),
                        SyntaxFactory.IdentifierName(node.Identifier)
                    )
                    .WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }

        /// <summary>
        /// Visits member access expression.
        /// </summary>
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is ThisExpressionSyntax && _receiverExpression is not null)
            {
                var rewrittenName = (SimpleNameSyntax)(Visit(node.Name) ?? node.Name);
                return SyntaxFactory
                    .MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        Parenthesize(_receiverExpression),
                        rewrittenName
                    )
                    .WithTriviaFrom(node);
            }

            return base.VisitMemberAccessExpression(node);
        }

        /// <summary>
        /// Handles parenthesize.
        /// </summary>
        private static ParenthesizedExpressionSyntax Parenthesize(ExpressionSyntax expression)
        {
            return expression is ParenthesizedExpressionSyntax parenthesized
                ? parenthesized
                : SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia());
        }
    }

    /// <summary>
    /// Represents capture.
    /// </summary>
    internal sealed record CaptureEntry
    {
        public required string PropertyName { get; init; }

        public required string LocalName { get; init; }

        public required string TypeName { get; init; }

        public required string ExpressionText { get; init; }

        public required ISymbol? RootSymbol { get; init; }

        public string? ValueAccessor { get; init; }
    }
}
