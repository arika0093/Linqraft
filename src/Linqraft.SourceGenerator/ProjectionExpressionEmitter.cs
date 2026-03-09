using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

internal sealed class AnonymousReplacementRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<SyntaxNode, string> _replacementTypes;

    public AnonymousReplacementRewriter(Dictionary<SyntaxNode, string> replacementTypes)
    {
        _replacementTypes = replacementTypes;
    }

    public override SyntaxNode? VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
    {
        if (!_replacementTypes.TryGetValue(node, out var targetType))
        {
            return base.VisitAnonymousObjectCreationExpression(node);
        }

        var assignments = node.Initializers
            .Select(
                initializer =>
                {
                    var visited = Visit(initializer.Expression);
                    var value = visited as ExpressionSyntax ?? initializer.Expression;
                    var memberName = GetMemberName(initializer);
                    return $"{memberName} = {value}";
                }
            );

        var text = $"new {targetType} {{ {string.Join(", ", assignments)} }}";
        return SyntaxFactory.ParseExpression(text).WithTriviaFrom(node);
    }

    private static string GetMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
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
}

internal sealed class ProjectionExpressionEmitter
{
    private readonly SemanticModel _semanticModel;
    private readonly ExpressionSyntax _rootExpression;
    private readonly string _rootTypeName;
    private readonly bool _useEmptyCollectionFallback;
    private readonly IReadOnlyDictionary<SyntaxNode, string> _replacementTypes;

    public ProjectionExpressionEmitter(
        SemanticModel semanticModel,
        ExpressionSyntax rootExpression,
        string rootTypeName,
        bool useEmptyCollectionFallback,
        IReadOnlyDictionary<SyntaxNode, string>? replacementTypes = null
    )
    {
        _semanticModel = semanticModel;
        _rootExpression = rootExpression;
        _rootTypeName = rootTypeName;
        _useEmptyCollectionFallback = useEmptyCollectionFallback;
        _replacementTypes = replacementTypes ?? new Dictionary<SyntaxNode, string>();
    }

    public string Emit(ExpressionSyntax expression)
    {
        if (TryEmitConditionalChain(expression, out var conditionalText))
        {
            return conditionalText;
        }

        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax anonymousObject => EmitAnonymousObject(anonymousObject),
            ObjectCreationExpressionSyntax objectCreation => EmitObjectCreation(objectCreation),
            InvocationExpressionSyntax invocation => EmitInvocation(invocation),
            MemberAccessExpressionSyntax memberAccess => $"{Emit(memberAccess.Expression)}.{EmitSimpleName(memberAccess.Name)}",
            ElementAccessExpressionSyntax elementAccess => $"{Emit(elementAccess.Expression)}{EmitBracketArguments(elementAccess.ArgumentList)}",
            ConditionalExpressionSyntax conditionalExpression => $"{Emit(conditionalExpression.Condition)} ? {Emit(conditionalExpression.WhenTrue)} : {Emit(conditionalExpression.WhenFalse)}",
            BinaryExpressionSyntax binaryExpression => $"{Emit(binaryExpression.Left)} {binaryExpression.OperatorToken.Text} {Emit(binaryExpression.Right)}",
            PrefixUnaryExpressionSyntax prefixUnary => $"{prefixUnary.OperatorToken.Text}{Emit(prefixUnary.Operand)}",
            PostfixUnaryExpressionSyntax postfixUnary => $"{Emit(postfixUnary.Operand)}{postfixUnary.OperatorToken.Text}",
            ParenthesizedExpressionSyntax parenthesized => $"({Emit(parenthesized.Expression)})",
            CastExpressionSyntax castExpression => $"({QualifyType(castExpression.Type)}){Emit(castExpression.Expression)}",
            AssignmentExpressionSyntax assignment => $"{Emit(assignment.Left)} {assignment.OperatorToken.Text} {Emit(assignment.Right)}",
            SimpleLambdaExpressionSyntax lambda => $"{lambda.Parameter.Identifier.ValueText} => {EmitLambdaBody(lambda.Body)}",
            ParenthesizedLambdaExpressionSyntax lambda => $"({string.Join(", ", lambda.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText))}) => {EmitLambdaBody(lambda.Body)}",
            IdentifierNameSyntax identifier => EmitIdentifier(identifier),
            GenericNameSyntax genericName => EmitSimpleName(genericName),
            ThisExpressionSyntax => "this",
            BaseExpressionSyntax => "base",
            LiteralExpressionSyntax => expression.ToString(),
            InterpolatedStringExpressionSyntax => expression.ToString(),
            TypeOfExpressionSyntax typeOfExpression => $"typeof({QualifyType(typeOfExpression.Type)})",
            DefaultExpressionSyntax defaultExpression => $"default({QualifyType(defaultExpression.Type)})",
            InitializerExpressionSyntax initializer => EmitInitializer(initializer),
            ImplicitArrayCreationExpressionSyntax => expression.ToString(),
            ArrayCreationExpressionSyntax => expression.ToString(),
            _ => expression.ToString(),
        };
    }

    private string EmitAnonymousObject(AnonymousObjectCreationExpressionSyntax expression)
    {
        var replacementType = _replacementTypes.TryGetValue(expression, out var resolvedType)
            ? resolvedType
            : null;
        var initializers = expression.Initializers
            .Select(
                initializer =>
                {
                    if (replacementType is null && initializer.NameEquals is null)
                    {
                        return Emit(initializer.Expression);
                    }

                    var memberName = initializer.NameEquals?.Name.Identifier.ValueText
                        ?? GetAnonymousMemberName(initializer);
                    return $"{memberName} = {Emit(initializer.Expression)}";
                }
            );

        return replacementType is null
            ? $"new {{ {string.Join(", ", initializers)} }}"
            : $"new {replacementType} {{ {string.Join(", ", initializers)} }}";
    }

    private string EmitObjectCreation(ObjectCreationExpressionSyntax expression)
    {
        var arguments = expression.ArgumentList is null ? string.Empty : EmitArgumentList(expression.ArgumentList);
        if (expression.Initializer is null)
        {
            return $"new {QualifyType(expression.Type)}{arguments}";
        }

        return $"new {QualifyType(expression.Type)}{arguments} {EmitInitializer(expression.Initializer)}";
    }

    private string EmitInitializer(InitializerExpressionSyntax expression)
    {
        var items = expression.Expressions.Select(Emit);
        return $"{{ {string.Join(", ", items)} }}";
    }

    private string EmitInvocation(InvocationExpressionSyntax expression)
    {
        if (GetInvocationName(expression.Expression) == "SelectExpr")
        {
            return EmitSelectExprInvocation(expression);
        }

        return $"{Emit(expression.Expression)}{EmitArgumentList(expression.ArgumentList)}";
    }

    private string EmitSelectExprInvocation(InvocationExpressionSyntax expression)
    {
        var receiver = expression.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => Emit(memberAccess.Expression),
            _ => Emit(expression.Expression),
        };
        var selector = expression.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
        if (selector is null)
        {
            return $"{receiver}.Select{EmitArgumentList(expression.ArgumentList)}";
        }

        return $"{receiver}.Select({Emit(selector)})";
    }

    private string EmitArgumentList(ArgumentListSyntax argumentList)
    {
        var arguments = argumentList.Arguments.Select(
            argument =>
            {
                var prefix = argument.NameColon is null ? string.Empty : $"{argument.NameColon.Name.Identifier.ValueText}: ";
                var refKind = argument.RefKindKeyword.IsKind(SyntaxKind.None)
                    ? string.Empty
                    : $"{argument.RefKindKeyword.Text} ";
                return $"{prefix}{refKind}{Emit(argument.Expression)}";
            }
        );
        return $"({string.Join(", ", arguments)})";
    }

    private string EmitBracketArguments(BracketedArgumentListSyntax argumentList)
    {
        var arguments = argumentList.Arguments.Select(argument => Emit(argument.Expression));
        return $"[{string.Join(", ", arguments)}]";
    }

    private string EmitSimpleName(SimpleNameSyntax name)
    {
        if (name is GenericNameSyntax genericName)
        {
            var typeArguments = genericName.TypeArgumentList.Arguments.Select(QualifyType);
            return $"{genericName.Identifier.ValueText}<{string.Join(", ", typeArguments)}>";
        }

        return name.Identifier.ValueText;
    }

    private string EmitIdentifier(IdentifierNameSyntax identifier)
    {
        if (!BelongsToSemanticModel(identifier))
        {
            return identifier.ToString();
        }

        var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
        if (symbol is ITypeSymbol typeSymbol)
        {
            return typeSymbol.ToFullyQualifiedTypeName();
        }

        return identifier.Identifier.ValueText;
    }

    private string EmitLambdaBody(CSharpSyntaxNode body)
    {
        return body switch
        {
            ExpressionSyntax expression => Emit(expression),
            BlockSyntax block => block.ToString(),
            _ => body.ToString(),
        };
    }

    private string QualifyType(TypeSyntax type)
    {
        if (!BelongsToSemanticModel(type))
        {
            return type.ToString();
        }

        var typeInfo = _semanticModel.GetTypeInfo(type);
        var symbol = typeInfo.Type ?? typeInfo.ConvertedType;
        if (symbol is null || symbol is IErrorTypeSymbol)
        {
            symbol = ResolveFallbackType(type);
        }

        return symbol is null ? type.ToString() : symbol.ToFullyQualifiedTypeName();
    }

    private bool TryEmitConditionalChain(ExpressionSyntax expression, out string rewritten)
    {
        rewritten = string.Empty;
        if (!ContainsConditionalAccess(expression))
        {
            return false;
        }

        if (!TryBuildConditional(expression, string.Empty, out var conditional))
        {
            return false;
        }

        rewritten = conditional;
        return true;
    }

    private bool TryBuildConditional(ExpressionSyntax expression, string tail, out string rewritten)
    {
        switch (expression)
        {
            case ConditionalAccessExpressionSyntax conditionalAccess:
            {
                var checks = new List<string>();
                var receiver = Emit(conditionalAccess.Expression);
                checks.Add(receiver);
                var access = BindConditionalReceiver(receiver, conditionalAccess.WhenNotNull, checks) + tail;
                var expressionType = GetExpressionType(expression);
                var expressionTypeName = GetExpressionTypeName(expression, expressionType, out var canCast);
                var useEmptyFallback = ReferenceEquals(expression, _rootExpression) && _useEmptyCollectionFallback;
                var fallback = useEmptyFallback
                    ? CreateEmptyCollectionFallback(expressionTypeName)
                    : "null";

                var castPrefix = !useEmptyFallback && canCast && !ShouldOmitConditionalCast(expression, expressionType)
                    ? $"({expressionTypeName})"
                    : string.Empty;
                rewritten =
                    $"{string.Join(" && ", checks.Select(check => $"{check} != null"))} ? {castPrefix}{access} : {fallback}";
                return true;
            }
            case MemberAccessExpressionSyntax memberAccess when ContainsConditionalAccess(memberAccess.Expression):
                return TryBuildConditional(
                    memberAccess.Expression,
                    "." + EmitSimpleName(memberAccess.Name) + tail,
                    out rewritten
                );
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberInvocation
                    && ContainsConditionalAccess(memberInvocation.Expression):
                return TryBuildConditional(
                    memberInvocation.Expression,
                    "." + EmitSimpleName(memberInvocation.Name) + EmitArgumentList(invocation.ArgumentList) + tail,
                    out rewritten
                );
            case ElementAccessExpressionSyntax elementAccess when ContainsConditionalAccess(elementAccess.Expression):
                return TryBuildConditional(
                    elementAccess.Expression,
                    EmitBracketArguments(elementAccess.ArgumentList) + tail,
                    out rewritten
                );
            default:
                rewritten = string.Empty;
                return false;
        }
    }

    private string BindConditionalReceiver(string receiver, ExpressionSyntax whenNotNull, IList<string> checks)
    {
        switch (whenNotNull)
        {
            case MemberBindingExpressionSyntax memberBinding:
                return $"{receiver}.{EmitSimpleName(memberBinding.Name)}";
            case MemberAccessExpressionSyntax memberAccess:
                return $"{BindConditionalReceiver(receiver, memberAccess.Expression, checks)}.{EmitSimpleName(memberAccess.Name)}";
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberBindingExpressionSyntax memberBinding:
                return $"{receiver}.{EmitSimpleName(memberBinding.Name)}{EmitArgumentList(invocation.ArgumentList)}";
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                return $"{BindConditionalReceiver(receiver, memberAccess.Expression, checks)}.{EmitSimpleName(memberAccess.Name)}{EmitArgumentList(invocation.ArgumentList)}";
            case ElementBindingExpressionSyntax elementBinding:
                return $"{receiver}{EmitBracketArguments(elementBinding.ArgumentList)}";
            case ConditionalAccessExpressionSyntax conditionalAccess:
            {
                var first = BindConditionalReceiver(receiver, conditionalAccess.Expression, checks);
                checks.Add(first);
                return BindConditionalReceiver(first, conditionalAccess.WhenNotNull, checks);
            }
            default:
                return Emit(whenNotNull);
        }
    }

    private static bool ContainsConditionalAccess(ExpressionSyntax expression)
    {
        return expression.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>().Any();
    }

    private static string CreateEmptyCollectionFallback(string typeName)
    {
        var normalizedTypeName = typeName.EndsWith("?", System.StringComparison.Ordinal)
            ? typeName[..^1]
            : typeName;

        if (normalizedTypeName.EndsWith("[]", System.StringComparison.Ordinal))
        {
            var elementType = normalizedTypeName[..^2];
            return $"global::System.Array.Empty<{elementType}>()";
        }

        if (normalizedTypeName.StartsWith("global::System.Collections.Generic.List<", System.StringComparison.Ordinal))
        {
            return $"new {normalizedTypeName}()";
        }

        if (TryGetSingleGenericArgument(normalizedTypeName, out var genericArgument))
        {
            return $"global::System.Linq.Enumerable.Empty<{genericArgument}>()";
        }

        // TODO: The public docs do not define a fallback constructor strategy for custom collection types.
        return $"new {normalizedTypeName}()";
    }

    private static bool TryGetSingleGenericArgument(string typeName, out string argument)
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

    private bool BelongsToSemanticModel(SyntaxNode node)
    {
        return node.SyntaxTree == _semanticModel.SyntaxTree;
    }

    private ITypeSymbol? GetExpressionType(ExpressionSyntax expression)
    {
        if (!BelongsToSemanticModel(expression))
        {
            return null;
        }

        var typeInfo = _semanticModel.GetTypeInfo(expression);
        return typeInfo.Type ?? typeInfo.ConvertedType;
    }

    private ITypeSymbol? ResolveFallbackType(TypeSyntax type)
    {
        var symbol = _semanticModel.GetSymbolInfo(type).Symbol as ITypeSymbol;
        if (symbol is not null && symbol is not IErrorTypeSymbol)
        {
            return symbol;
        }

        return type switch
        {
            IdentifierNameSyntax identifier => _semanticModel.LookupNamespacesAndTypes(type.SpanStart, name: identifier.Identifier.ValueText)
                .OfType<ITypeSymbol>()
                .FirstOrDefault(),
            QualifiedNameSyntax qualifiedName => _semanticModel.LookupNamespacesAndTypes(type.SpanStart, name: qualifiedName.Right.Identifier.ValueText)
                .OfType<ITypeSymbol>()
                .FirstOrDefault(candidate => candidate.ToDisplayString().EndsWith(qualifiedName.ToString(), System.StringComparison.Ordinal)),
            AliasQualifiedNameSyntax aliasQualifiedName => _semanticModel.LookupNamespacesAndTypes(type.SpanStart, name: aliasQualifiedName.Name.Identifier.ValueText)
                .OfType<ITypeSymbol>()
                .FirstOrDefault(),
            GenericNameSyntax genericName => _semanticModel.LookupNamespacesAndTypes(type.SpanStart, name: genericName.Identifier.ValueText)
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(candidate => candidate.Arity == genericName.TypeArgumentList.Arguments.Count),
            _ => null,
        };
    }

    private string GetExpressionTypeName(ExpressionSyntax expression, ITypeSymbol? expressionType, out bool canCast)
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

        if (type.IsAnonymousType)
        {
            canCast = false;
            return "object";
        }

        return type.ToFullyQualifiedTypeName();
    }

    private bool ShouldOmitConditionalCast(ExpressionSyntax expression, ITypeSymbol? type)
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

    private static bool ContainsProjectionLikeInvocation(ExpressionSyntax expression)
    {
        return expression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
            {
                var name = GetInvocationName(invocation.Expression);
                return name is "Select" or "SelectMany" or "ToList" or "ToArray";
            });
    }

    private static string GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        return initializer.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => initializer.Expression.ToString(),
        };
    }

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
}
