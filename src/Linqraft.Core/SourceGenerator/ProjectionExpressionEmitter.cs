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

internal sealed class AnonymousReplacementRewriter : CSharpSyntaxRewriter
{
    private readonly IReadOnlyDictionary<TextSpan, string> _replacementTypes;

    public AnonymousReplacementRewriter(IReadOnlyDictionary<TextSpan, string> replacementTypes)
    {
        _replacementTypes = replacementTypes;
    }

    public override SyntaxNode? VisitAnonymousObjectCreationExpression(
        AnonymousObjectCreationExpressionSyntax node
    )
    {
        if (!_replacementTypes.TryGetValue(node.Span, out var targetType))
        {
            return base.VisitAnonymousObjectCreationExpression(node);
        }

        var assignments = node.Initializers.Select(initializer =>
        {
            var visited = Visit(initializer.Expression);
            var value = visited as ExpressionSyntax ?? initializer.Expression;
            var memberName = GetMemberName(initializer);
            return $"{memberName} = {value}";
        });

        var text = $"new {targetType} {{ {string.Join(", ", assignments)} }}";
        return SyntaxFactory.ParseExpression(text).WithTriviaFrom(node);
    }

    private static string GetMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        return initializer.NameEquals?.Name.Identifier.ValueText
            ?? AnonymousMemberNameResolver.Get(initializer.Expression);
    }
}

internal sealed class ProjectionExpressionEmitter
{
    private readonly SemanticModel _semanticModel;
    private readonly ExpressionSyntax _rootExpression;
    private readonly string _rootTypeName;
    private readonly bool _useEmptyCollectionFallback;
    private readonly IReadOnlyDictionary<TextSpan, string> _replacementTypes;
    private readonly IReadOnlyList<CaptureEntry> _captureEntries;
    private readonly LinqraftGeneratorOptionsCore _generatorOptions;
    private readonly HashSet<ISymbol> _activeProjectableSymbols;
    private readonly string? _projectionHelperParameterName;
    private readonly string? _projectionHelperParameterTypeName;

    public ProjectionExpressionEmitter(
        SemanticModel semanticModel,
        ExpressionSyntax rootExpression,
        string rootTypeName,
        bool useEmptyCollectionFallback,
        LinqraftGeneratorOptionsCore generatorOptions,
        string? projectionHelperParameterName = null,
        string? projectionHelperParameterTypeName = null,
        IReadOnlyDictionary<TextSpan, string>? replacementTypes = null,
        IReadOnlyList<CaptureEntry>? captureEntries = null,
        HashSet<ISymbol>? activeProjectableSymbols = null
    )
    {
        _semanticModel = semanticModel;
        _rootExpression = rootExpression;
        _rootTypeName = rootTypeName;
        _useEmptyCollectionFallback = useEmptyCollectionFallback;
        _generatorOptions = generatorOptions;
        _projectionHelperParameterName = projectionHelperParameterName;
        _projectionHelperParameterTypeName = projectionHelperParameterTypeName;
        _replacementTypes = replacementTypes ?? new Dictionary<TextSpan, string>();
        _captureEntries = captureEntries ?? global::System.Array.Empty<CaptureEntry>();
        _activeProjectableSymbols =
            activeProjectableSymbols ?? new HashSet<ISymbol>(SymbolEqualityComparer.Default);
    }

    private static CancellationToken ResolveCancellationToken(CancellationToken cancellationToken)
    {
        return cancellationToken;
    }

    public string Emit(ExpressionSyntax expression, CancellationToken cancellationToken = default)
    {
        if (TryEmitCaptureReplacement(expression, out var captureReplacement, cancellationToken))
        {
            return captureReplacement;
        }

        if (TryEmitProjectionHook(expression, out var hookText, cancellationToken))
        {
            return hookText;
        }

        if (TryEmitConditionalChain(expression, out var conditionalText, cancellationToken))
        {
            return conditionalText;
        }

        if (TryEmitFluentChain(expression, out var fluentChain, cancellationToken))
        {
            return fluentChain;
        }

        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax anonymousObject => EmitAnonymousObject(
                anonymousObject,
                cancellationToken
            ),
            ObjectCreationExpressionSyntax objectCreation => EmitObjectCreation(
                objectCreation,
                cancellationToken
            ),
            CollectionExpressionSyntax collectionExpression => EmitCollectionExpression(
                collectionExpression,
                cancellationToken
            ),
            InvocationExpressionSyntax invocation => EmitInvocation(invocation, cancellationToken),
            MemberAccessExpressionSyntax memberAccess =>
                $"{Emit(memberAccess.Expression, cancellationToken)}.{EmitSimpleName(memberAccess.Name)}",
            ElementAccessExpressionSyntax elementAccess =>
                $"{Emit(elementAccess.Expression, cancellationToken)}{EmitBracketArguments(elementAccess.ArgumentList, cancellationToken)}",
            ConditionalExpressionSyntax conditionalExpression => EmitConditionalExpression(
                conditionalExpression,
                cancellationToken
            ),
            BinaryExpressionSyntax binaryExpression => EmitBinaryExpression(
                binaryExpression,
                cancellationToken
            ),
            PrefixUnaryExpressionSyntax prefixUnary =>
                $"{prefixUnary.OperatorToken.Text}{Emit(prefixUnary.Operand, cancellationToken)}",
            PostfixUnaryExpressionSyntax postfixUnary =>
                $"{Emit(postfixUnary.Operand, cancellationToken)}{postfixUnary.OperatorToken.Text}",
            ParenthesizedExpressionSyntax parenthesized =>
                $"({Emit(parenthesized.Expression, cancellationToken)})",
            CastExpressionSyntax castExpression => EmitCastExpression(
                castExpression,
                cancellationToken
            ),
            AssignmentExpressionSyntax assignment =>
                $"{Emit(assignment.Left, cancellationToken)} {assignment.OperatorToken.Text} {Emit(assignment.Right, cancellationToken)}",
            SimpleLambdaExpressionSyntax lambda => EmitLambda(
                lambda.Parameter.Identifier.ValueText,
                lambda.Body,
                cancellationToken
            ),
            ParenthesizedLambdaExpressionSyntax lambda => EmitLambda(
                $"({string.Join(", ", lambda.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText))})",
                lambda.Body,
                cancellationToken
            ),
            IdentifierNameSyntax identifier => EmitIdentifier(identifier, cancellationToken),
            GenericNameSyntax genericName => EmitSimpleName(genericName),
            ThisExpressionSyntax => "this",
            BaseExpressionSyntax => "base",
            LiteralExpressionSyntax => expression.ToString(),
            InterpolatedStringExpressionSyntax => expression.ToString(),
            TypeOfExpressionSyntax typeOfExpression =>
                $"typeof({QualifyType(typeOfExpression.Type, cancellationToken)})",
            DefaultExpressionSyntax defaultExpression =>
                $"default({QualifyType(defaultExpression.Type, cancellationToken)})",
            InitializerExpressionSyntax initializer => EmitInitializer(
                initializer,
                cancellationToken
            ),
            ImplicitArrayCreationExpressionSyntax => expression.ToString(),
            ArrayCreationExpressionSyntax => expression.ToString(),
            _ => expression.ToString(),
        };
    }

    private string EmitAnonymousObject(
        AnonymousObjectCreationExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var replacementType = _replacementTypes.TryGetValue(expression.Span, out var resolvedType)
            ? resolvedType
            : null;
        var initializers = expression
            .Initializers.Select(initializer =>
            {
                var memberName =
                    initializer.NameEquals?.Name.Identifier.ValueText
                    ?? GetAnonymousMemberName(initializer);
                return AppendValueWithContinuation(
                    $"{memberName} = ",
                    EmitNestedExpression(initializer.Expression, cancellationToken)
                );
            })
            .ToList();

        return BuildInitializerExpression(
            replacementType is null ? "new" : $"new {replacementType}",
            initializers,
            cancellationToken
        );
    }

    private string EmitObjectCreation(
        ObjectCreationExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = expression.ArgumentList is null
            ? string.Empty
            : EmitArgumentList(expression.ArgumentList, cancellationToken);
        var header = $"new {QualifyType(expression.Type, cancellationToken)}{arguments}";
        if (expression.Initializer is null)
        {
            return header;
        }

        return BuildInitializerExpression(
            header,
            expression
                .Initializer.Expressions.Select(item =>
                    EmitInitializerItem(item, cancellationToken)
                )
                .ToList(),
            cancellationToken
        );
    }

    private string EmitInitializer(
        InitializerExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return BuildInitializerBody(
            expression
                .Expressions.Select(item => EmitInitializerItem(item, cancellationToken))
                .ToList(),
            cancellationToken
        );
    }

    private string EmitInitializerItem(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        if (expression is AssignmentExpressionSyntax assignment)
        {
            return AppendValueWithContinuation(
                $"{Emit(assignment.Left, cancellationToken)} {assignment.OperatorToken.Text} ",
                EmitNestedExpression(assignment.Right, cancellationToken)
            );
        }

        return EmitNestedExpression(expression, cancellationToken);
    }

    private string EmitCollectionExpression(
        CollectionExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var targetTypeName = ResolveCollectionTargetTypeName(expression, cancellationToken);
        if (expression.Elements.Count == 0)
        {
            return CreateEmptyCollectionFallback(targetTypeName, cancellationToken);
        }

        var items = expression.Elements.Select(element =>
            element switch
            {
                ExpressionElementSyntax expressionElement => Emit(
                    expressionElement.Expression,
                    cancellationToken
                ),
                SpreadElementSyntax spreadElement => Emit(
                    spreadElement.Expression,
                    cancellationToken
                ),
                _ => element.ToString(),
            }
        );

        var normalizedTypeName = targetTypeName.EndsWith("?", System.StringComparison.Ordinal)
            ? targetTypeName[..^1]
            : targetTypeName;

        if (normalizedTypeName.EndsWith("[]", System.StringComparison.Ordinal))
        {
            return $"new {normalizedTypeName} {{ {string.Join(", ", items)} }}";
        }

        if (
            normalizedTypeName.StartsWith(
                "global::System.Collections.Generic.List<",
                System.StringComparison.Ordinal
            )
        )
        {
            return $"new {normalizedTypeName} {{ {string.Join(", ", items)} }}";
        }

        if (
            TryGetSingleGenericArgument(
                normalizedTypeName,
                out var genericArgument,
                cancellationToken
            )
        )
        {
            return $"new {genericArgument}[] {{ {string.Join(", ", items)} }}";
        }

        return $"new {normalizedTypeName} {{ {string.Join(", ", items)} }}";
    }

    private string EmitConditionalExpression(
        ConditionalExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var condition = Emit(expression.Condition, cancellationToken);
        var whenTrue = Emit(expression.WhenTrue, cancellationToken);
        var whenFalse = Emit(expression.WhenFalse, cancellationToken);
        if (
            !ContainsLineBreak(condition)
            && !ContainsLineBreak(whenTrue)
            && !ContainsLineBreak(whenFalse)
        )
        {
            return $"{condition} ? {whenTrue} : {whenFalse}";
        }

        return string.Join(
            "\n",
            condition,
            IndentAllLines(AppendValueWithContinuation("? ", whenTrue)),
            IndentAllLines(AppendValueWithContinuation(": ", whenFalse))
        );
    }

    private string EmitBinaryExpression(
        BinaryExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (
            expression.IsKind(SyntaxKind.CoalesceExpression)
            && TryEmitCollectionFallbackCoalesce(expression, out var rewritten, cancellationToken)
        )
        {
            return rewritten;
        }

        return $"{EmitBinaryOperand(expression.Left, cancellationToken)} {expression.OperatorToken.Text} {EmitBinaryOperand(expression.Right, cancellationToken)}";
    }

    private string EmitBinaryOperand(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var emitted = Emit(expression, cancellationToken);
        return NeedsParenthesesInBinary(expression) ? $"({emitted})" : emitted;
    }

    private static bool NeedsParenthesesInBinary(ExpressionSyntax expression)
    {
        return expression is not ParenthesizedExpressionSyntax
            && (expression is ConditionalExpressionSyntax || ContainsConditionalAccess(expression));
    }

    private bool TryEmitCollectionFallbackCoalesce(
        BinaryExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (!IsEmptyCollectionExpression(expression.Right))
        {
            return false;
        }

        if (!ContainsConditionalAccess(expression.Left))
        {
            return false;
        }

        var expressionType = GetExpressionType(expression, cancellationToken);
        var rootTypeName = GetExpressionTypeName(expression, expressionType, out _);
        var nestedEmitter = new ProjectionExpressionEmitter(
            _semanticModel,
            expression.Left,
            rootTypeName,
            useEmptyCollectionFallback: true,
            _generatorOptions,
            _projectionHelperParameterName,
            _projectionHelperParameterTypeName,
            _replacementTypes,
            _captureEntries,
            _activeProjectableSymbols
        );
        rewritten = nestedEmitter.Emit(expression.Left, cancellationToken);
        return true;
    }

    private string EmitInvocation(
        InvocationExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (TryEmitProjectableHook(expression, out var projectable, cancellationToken))
        {
            return projectable;
        }

        return expression.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess
                .Name
                .Identifier
                .ValueText switch
            {
                var methodName when methodName == _generatorOptions.SelectExprMethodName =>
                    EmitProjectionInvocation(
                        Emit(memberAccess.Expression, cancellationToken),
                        expression,
                        "Select",
                        cancellationToken
                    ),
                var methodName when methodName == _generatorOptions.SelectManyExprMethodName =>
                    EmitProjectionInvocation(
                        Emit(memberAccess.Expression, cancellationToken),
                        expression,
                        "SelectMany",
                        cancellationToken
                    ),
                var methodName when methodName == _generatorOptions.GroupByExprMethodName =>
                    EmitGroupByExprInvocation(
                        Emit(memberAccess.Expression, cancellationToken),
                        expression,
                        cancellationToken
                    ),
                _ => EmitMemberInvocation(
                    expression,
                    memberAccess,
                    Emit(memberAccess.Expression, cancellationToken),
                    cancellationToken
                ),
            },
            _ =>
                $"{Emit(expression.Expression, cancellationToken)}{EmitArgumentList(expression.ArgumentList, cancellationToken)}",
        };
    }

    private string EmitProjectionInvocation(
        string receiver,
        InvocationExpressionSyntax expression,
        string projectionMethodName,
        CancellationToken cancellationToken = default
    )
    {
        var selector = expression
            .ArgumentList.Arguments.Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
        if (selector is null)
        {
            return $"{receiver}.{projectionMethodName}{EmitArgumentList(expression.ArgumentList, cancellationToken)}";
        }

        return $"{receiver}.{projectionMethodName}({Emit(selector, cancellationToken)})";
    }

    private string EmitGroupByExprInvocation(
        string receiver,
        InvocationExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var lambdas = expression
            .ArgumentList.Arguments.Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .ToArray();
        if (lambdas.Length < 2)
        {
            return $"{receiver}.GroupBy{EmitArgumentList(expression.ArgumentList, cancellationToken)}";
        }

        return $"{receiver}.GroupBy({Emit(lambdas[0], cancellationToken)}).Select({Emit(lambdas[1], cancellationToken)})";
    }

    private string EmitMemberInvocation(
        InvocationExpressionSyntax expression,
        MemberAccessExpressionSyntax memberAccess,
        string receiver,
        CancellationToken cancellationToken = default
    )
    {
        return TryEmitExtensionInvocation(
            expression,
            memberAccess.Name,
            receiver,
            out var rewritten,
            cancellationToken
        )
            ? rewritten
            : $"{receiver}.{EmitSimpleName(memberAccess.Name)}{EmitArgumentList(expression.ArgumentList, cancellationToken)}";
    }

    private string EmitArgumentList(
        ArgumentListSyntax argumentList,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = argumentList.Arguments.Select(argument =>
            EmitArgument(argument, cancellationToken)
        );
        return $"({string.Join(", ", arguments)})";
    }

    private string EmitArgument(
        ArgumentSyntax argument,
        CancellationToken cancellationToken = default
    )
    {
        var prefix = argument.NameColon is null
            ? string.Empty
            : $"{argument.NameColon.Name.Identifier.ValueText}: ";
        var refKind = argument.RefKindKeyword.IsKind(SyntaxKind.None)
            ? string.Empty
            : $"{argument.RefKindKeyword.Text} ";
        return $"{prefix}{refKind}{Emit(argument.Expression, cancellationToken)}";
    }

    private string EmitBracketArguments(
        BracketedArgumentListSyntax argumentList,
        CancellationToken cancellationToken = default
    )
    {
        var arguments = argumentList.Arguments.Select(argument =>
            Emit(argument.Expression, cancellationToken)
        );
        return $"[{string.Join(", ", arguments)}]";
    }

    private string EmitSimpleName(SimpleNameSyntax name)
    {
        if (name is GenericNameSyntax genericName)
        {
            var typeArguments = genericName.TypeArgumentList.Arguments.Select(type =>
                QualifyType(type)
            );
            return $"{genericName.Identifier.ValueText}<{string.Join(", ", typeArguments)}>";
        }

        return name.Identifier.ValueText;
    }

    private string EmitIdentifier(
        IdentifierNameSyntax identifier,
        CancellationToken cancellationToken = default
    )
    {
        if (!BelongsToSemanticModel(identifier))
        {
            return identifier.ToString();
        }

        cancellationToken = ResolveCancellationToken(cancellationToken);
        var symbol = _semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
        if (symbol is ITypeSymbol typeSymbol)
        {
            return typeSymbol.ToFullyQualifiedTypeName();
        }

        return identifier.Identifier.ValueText;
    }

    private string EmitLambdaBody(
        CSharpSyntaxNode body,
        CancellationToken cancellationToken = default
    )
    {
        return body switch
        {
            ExpressionSyntax expression => Emit(expression, cancellationToken),
            BlockSyntax block => block.ToString(),
            _ => body.ToString(),
        };
    }

    private string EmitLambda(
        string parameterList,
        CSharpSyntaxNode body,
        CancellationToken cancellationToken = default
    )
    {
        return AppendValueInline($"{parameterList} => ", EmitLambdaBody(body, cancellationToken));
    }

    private string EmitNestedExpression(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (!BelongsToSemanticModel(expression))
        {
            return Emit(expression, cancellationToken);
        }

        var expressionType = GetExpressionType(expression, cancellationToken);
        if (!ShouldUseCollectionFallback(expression, expressionType))
        {
            return Emit(expression, cancellationToken);
        }

        string rootTypeName;
        if (_replacementTypes.TryGetValue(expression.Span, out var replacementType))
        {
            rootTypeName = replacementType;
        }
        else if (
            expressionType is not null
            && expressionType is not IErrorTypeSymbol
            && !ContainsAnonymousType(expressionType)
        )
        {
            rootTypeName = expressionType.ToFullyQualifiedTypeName();
        }
        else
        {
            rootTypeName = _rootTypeName;
        }
        var nestedEmitter = new ProjectionExpressionEmitter(
            _semanticModel,
            expression,
            rootTypeName,
            ShouldUseCollectionFallback(expression, expressionType),
            _generatorOptions,
            _projectionHelperParameterName,
            _projectionHelperParameterTypeName,
            _replacementTypes,
            _captureEntries,
            _activeProjectableSymbols
        );
        return nestedEmitter.Emit(expression, cancellationToken);
    }

    private string QualifyType(TypeSyntax type, CancellationToken cancellationToken = default)
    {
        if (!BelongsToSemanticModel(type))
        {
            return type.ToString();
        }

        cancellationToken = ResolveCancellationToken(cancellationToken);
        var typeInfo = _semanticModel.GetTypeInfo(type, cancellationToken);
        var symbol = typeInfo.Type ?? typeInfo.ConvertedType;
        if (symbol is null || symbol is IErrorTypeSymbol)
        {
            symbol = ResolveFallbackType(type, cancellationToken);
        }

        return symbol is null ? type.ToString() : symbol.ToFullyQualifiedTypeName();
    }

    private bool TryEmitConditionalChain(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (!ContainsConditionalAccess(expression))
        {
            return false;
        }

        if (
            !TryBuildConditional(
                expression,
                value => value,
                expression,
                out var conditional,
                cancellationToken
            )
        )
        {
            return false;
        }

        rewritten = conditional;
        return true;
    }

    private bool TryBuildConditional(
        ExpressionSyntax expression,
        global::System.Func<string, string> applyTail,
        ExpressionSyntax rootConditionalExpression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        switch (expression)
        {
            case ConditionalAccessExpressionSyntax conditionalAccess:
            {
                var checks = new List<string>();
                var receiver = Emit(conditionalAccess.Expression, cancellationToken);
                checks.Add(receiver);
                var expressionType = GetExpressionType(expression, cancellationToken);
                var expressionTypeName = GetExpressionTypeName(
                    expression,
                    expressionType,
                    out var canCast
                );
                var rootExpressionType = ReferenceEquals(rootConditionalExpression, expression)
                    ? expressionType
                    : GetExpressionType(rootConditionalExpression, cancellationToken);
                var rootExpressionTypeName = ReferenceEquals(rootConditionalExpression, expression)
                    ? expressionTypeName
                    : GetExpressionTypeName(rootConditionalExpression, rootExpressionType, out _);
                var useEmptyFallback =
                    ReferenceEquals(rootConditionalExpression, _rootExpression)
                    && (
                        _useEmptyCollectionFallback
                        || ShouldUseCollectionFallback(
                            rootConditionalExpression,
                            rootExpressionType
                        )
                    );
                var fallback = useEmptyFallback
                    ? CreateEmptyCollectionFallback(rootExpressionTypeName, cancellationToken)
                    : "null";
                var access = applyTail(
                    BindConditionalReceiver(
                        receiver,
                        conditionalAccess.WhenNotNull,
                        checks,
                        cancellationToken
                    )
                );

                var castPrefix =
                    !useEmptyFallback
                    && canCast
                    && !ShouldOmitConditionalCast(expression, expressionType)
                        ? $"({expressionTypeName})"
                        : string.Empty;
                var formattedAccess = access;
                if (
                    TryFormatFluentAccess(
                        receiver,
                        access,
                        out var multilineAccess,
                        cancellationToken
                    )
                )
                {
                    formattedAccess = multilineAccess;
                }

                if (!string.IsNullOrEmpty(castPrefix))
                {
                    formattedAccess = WrapCastExpression(castPrefix, formattedAccess);
                }

                if (ContainsLineBreak(formattedAccess) || ContainsLineBreak(fallback))
                {
                    rewritten = string.Join(
                        "\n",
                        string.Join(" && ", checks.Select(check => $"{check} != null")),
                        IndentAllLines(AppendValueWithContinuation("? ", formattedAccess)),
                        IndentAllLines(AppendValueWithContinuation(": ", fallback))
                    );
                }
                else
                {
                    rewritten =
                        $"{string.Join(" && ", checks.Select(check => $"{check} != null"))} ? {formattedAccess} : {fallback}";
                }
                return true;
            }
            case MemberAccessExpressionSyntax memberAccess
                when ContainsConditionalAccess(memberAccess.Expression):
                return TryBuildConditional(
                    memberAccess.Expression,
                    value => applyTail($"{value}.{EmitSimpleName(memberAccess.Name)}"),
                    rootConditionalExpression,
                    out rewritten,
                    cancellationToken
                );
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberInvocation
                    && ContainsConditionalAccess(memberInvocation.Expression):
                return TryBuildConditional(
                    memberInvocation.Expression,
                    value =>
                        applyTail(
                            EmitInvocationFromReceiver(
                                invocation,
                                memberInvocation.Name,
                                value,
                                cancellationToken
                            )
                        ),
                    rootConditionalExpression,
                    out rewritten,
                    cancellationToken
                );
            case ElementAccessExpressionSyntax elementAccess
                when ContainsConditionalAccess(elementAccess.Expression):
                return TryBuildConditional(
                    elementAccess.Expression,
                    value =>
                        applyTail(
                            $"{value}{EmitBracketArguments(elementAccess.ArgumentList, cancellationToken)}"
                        ),
                    rootConditionalExpression,
                    out rewritten,
                    cancellationToken
                );
            default:
                rewritten = string.Empty;
                return false;
        }
    }

    private string BindConditionalReceiver(
        string receiver,
        ExpressionSyntax whenNotNull,
        IList<string> checks,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        switch (whenNotNull)
        {
            case MemberBindingExpressionSyntax memberBinding:
                return $"{receiver}.{EmitSimpleName(memberBinding.Name)}";
            case MemberAccessExpressionSyntax memberAccess:
                return $"{BindConditionalReceiver(receiver, memberAccess.Expression, checks, cancellationToken)}.{EmitSimpleName(memberAccess.Name)}";
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberBindingExpressionSyntax memberBinding:
                return EmitInvocationFromReceiver(
                    invocation,
                    memberBinding.Name,
                    receiver,
                    cancellationToken
                );
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                return EmitInvocationFromReceiver(
                    invocation,
                    memberAccess.Name,
                    BindConditionalReceiver(
                        receiver,
                        memberAccess.Expression,
                        checks,
                        cancellationToken
                    ),
                    cancellationToken
                );
            case ElementBindingExpressionSyntax elementBinding:
                return $"{receiver}{EmitBracketArguments(elementBinding.ArgumentList, cancellationToken)}";
            case ConditionalAccessExpressionSyntax conditionalAccess:
            {
                var first = BindConditionalReceiver(
                    receiver,
                    conditionalAccess.Expression,
                    checks,
                    cancellationToken
                );
                checks.Add(first);
                return BindConditionalReceiver(
                    first,
                    conditionalAccess.WhenNotNull,
                    checks,
                    cancellationToken
                );
            }
            default:
                return Emit(whenNotNull, cancellationToken);
        }
    }

    private static bool ContainsConditionalAccess(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any();
    }

    private bool TryEmitFluentChain(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (
            ContainsReducedExtensionInvocation(expression, cancellationToken)
            || !TryDecomposeFluentChain(
                expression,
                out var root,
                out var segments,
                out var hasInvocation,
                cancellationToken
            )
            || !hasInvocation
            || segments.Count == 0
        )
        {
            return false;
        }

        if (segments.Count == 1 && segments.All(segment => !ContainsLineBreak(segment)))
        {
            return false;
        }

        var lines = new List<string> { Emit(root, cancellationToken) };
        lines.AddRange(segments.Select(segment => IndentAllLines(segment)));
        rewritten = string.Join("\n", lines);
        return true;
    }

    private bool TryDecomposeFluentChain(
        ExpressionSyntax expression,
        out ExpressionSyntax root,
        out List<string> segments,
        out bool hasInvocation,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                TryDecomposeFluentChain(
                    memberAccess.Expression,
                    out root,
                    out segments,
                    out hasInvocation,
                    cancellationToken
                );
                segments.Add(
                    GetFluentInvocationSegment(invocation, memberAccess, cancellationToken)
                );
                hasInvocation = true;
                return true;
            case MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression
                    is InvocationExpressionSyntax
                        or MemberAccessExpressionSyntax
                        or ElementAccessExpressionSyntax:
                TryDecomposeFluentChain(
                    memberAccess.Expression,
                    out root,
                    out segments,
                    out hasInvocation,
                    cancellationToken
                );
                segments.Add($".{EmitSimpleName(memberAccess.Name)}");
                return true;
            case ElementAccessExpressionSyntax elementAccess
                when elementAccess.Expression
                    is InvocationExpressionSyntax
                        or MemberAccessExpressionSyntax
                        or ElementAccessExpressionSyntax:
                TryDecomposeFluentChain(
                    elementAccess.Expression,
                    out root,
                    out segments,
                    out hasInvocation,
                    cancellationToken
                );
                segments.Add(EmitBracketArguments(elementAccess.ArgumentList, cancellationToken));
                return true;
            default:
                root = expression;
                segments = new List<string>();
                hasInvocation = false;
                return true;
        }
    }

    private string GetFluentInvocationSegment(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        CancellationToken cancellationToken = default
    )
    {
        if (memberAccess.Name.Identifier.ValueText == _generatorOptions.SelectExprMethodName)
        {
            var selector = invocation
                .ArgumentList.Arguments.Select(argument => argument.Expression)
                .OfType<LambdaExpressionSyntax>()
                .FirstOrDefault();
            return selector is null
                ? $".Select{EmitArgumentList(invocation.ArgumentList, cancellationToken)}"
                : $".Select({Emit(selector, cancellationToken)})";
        }

        return $".{EmitSimpleName(memberAccess.Name)}{EmitArgumentList(invocation.ArgumentList, cancellationToken)}";
    }

    private string EmitInvocationFromReceiver(
        InvocationExpressionSyntax invocation,
        SimpleNameSyntax methodName,
        string receiver,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (TryEmitProjectableHook(invocation, receiver, out var projectable, cancellationToken))
        {
            return projectable;
        }

        return methodName.Identifier.ValueText switch
        {
            var name when name == _generatorOptions.SelectExprMethodName =>
                EmitProjectionInvocation(receiver, invocation, "Select", cancellationToken),
            var name when name == _generatorOptions.SelectManyExprMethodName =>
                EmitProjectionInvocation(receiver, invocation, "SelectMany", cancellationToken),
            var name when name == _generatorOptions.GroupByExprMethodName =>
                EmitGroupByExprInvocation(receiver, invocation, cancellationToken),
            _ => TryEmitExtensionInvocation(
                invocation,
                methodName,
                receiver,
                out var rewritten,
                cancellationToken
            )
                ? rewritten
                : $"{receiver}.{EmitSimpleName(methodName)}{EmitArgumentList(invocation.ArgumentList, cancellationToken)}",
        };
    }

    private bool TryEmitProjectionHook(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return TryEmitLeftJoinHook(expression, out rewritten, cancellationToken)
            || TryEmitInnerJoinHook(expression, out rewritten, cancellationToken)
            || TryEmitAsProjectionHook(expression, out rewritten, cancellationToken)
            || TryEmitProjectedValueSelection(expression, out rewritten, cancellationToken)
            || TryEmitProjectableHook(expression, out rewritten, cancellationToken);
    }

    private bool TryEmitLeftJoinHook(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (
            !ContainsProjectionHook(
                expression,
                LinqraftProjectionHookKind.LeftJoin,
                cancellationToken
            )
        )
        {
            return false;
        }

        return TryBuildLeftJoinConditional(
            expression,
            value => value,
            expression,
            out rewritten,
            cancellationToken
        );
    }

    private bool TryEmitInnerJoinHook(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (
            !ContainsProjectionHook(
                expression,
                LinqraftProjectionHookKind.InnerJoin,
                cancellationToken
            )
        )
        {
            return false;
        }

        return TryBuildInnerJoinAccess(expression, value => value, out rewritten, cancellationToken);
    }

    private bool TryBuildInnerJoinAccess(
        ExpressionSyntax expression,
        global::System.Func<string, string> applyTail,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when IsProjectionHookInvocation(
                    invocation,
                    LinqraftProjectionHookKind.InnerJoin,
                    cancellationToken
                ):
            {
                var targetExpression = GetHookTargetExpression(invocation, cancellationToken);
                if (targetExpression is null)
                {
                    rewritten = string.Empty;
                    return false;
                }

                rewritten = applyTail(Emit(targetExpression, cancellationToken));
                return true;
            }
            case MemberAccessExpressionSyntax memberAccess
                when ContainsProjectionHook(
                    memberAccess.Expression,
                    LinqraftProjectionHookKind.InnerJoin,
                    cancellationToken
                ):
                return TryBuildInnerJoinAccess(
                    memberAccess.Expression,
                    value => applyTail($"{value}.{EmitSimpleName(memberAccess.Name)}"),
                    out rewritten,
                    cancellationToken
                );
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberInvocation
                    && ContainsProjectionHook(
                        memberInvocation.Expression,
                        LinqraftProjectionHookKind.InnerJoin,
                        cancellationToken
                    ):
                return TryBuildInnerJoinAccess(
                    memberInvocation.Expression,
                    value =>
                        applyTail(
                            EmitInvocationFromReceiver(
                                invocation,
                                memberInvocation.Name,
                                value,
                                cancellationToken
                            )
                        ),
                    out rewritten,
                    cancellationToken
                );
            case ElementAccessExpressionSyntax elementAccess
                when ContainsProjectionHook(
                    elementAccess.Expression,
                    LinqraftProjectionHookKind.InnerJoin,
                    cancellationToken
                ):
                return TryBuildInnerJoinAccess(
                    elementAccess.Expression,
                    value =>
                        applyTail(
                            $"{value}{EmitBracketArguments(elementAccess.ArgumentList, cancellationToken)}"
                        ),
                    out rewritten,
                    cancellationToken
                );
            default:
                rewritten = string.Empty;
                return false;
        }
    }

    private bool TryBuildLeftJoinConditional(
        ExpressionSyntax expression,
        global::System.Func<string, string> applyTail,
        ExpressionSyntax rootLeftJoinExpression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when IsProjectionHookInvocation(
                    invocation,
                    LinqraftProjectionHookKind.LeftJoin,
                    cancellationToken
                ):
            {
                var targetExpression = GetHookTargetExpression(invocation, cancellationToken);
                if (targetExpression is null)
                {
                    rewritten = string.Empty;
                    return false;
                }

                var receiver = Emit(targetExpression, cancellationToken);
                var access = applyTail(receiver);
                var expressionType = GetExpressionType(rootLeftJoinExpression, cancellationToken);
                var expressionTypeName = GetExpressionTypeName(
                    rootLeftJoinExpression,
                    expressionType,
                    out _
                );
                var fallback = CreateLeftJoinFallback(
                    rootLeftJoinExpression,
                    expressionTypeName,
                    cancellationToken
                );
                if (ContainsLineBreak(access) || ContainsLineBreak(fallback))
                {
                    rewritten = string.Join(
                        "\n",
                        $"{receiver} != null",
                        IndentAllLines(AppendValueWithContinuation("? ", access)),
                        IndentAllLines(AppendValueWithContinuation(": ", fallback))
                    );
                }
                else
                {
                    rewritten = $"{receiver} != null ? {access} : {fallback}";
                }

                return true;
            }
            case MemberAccessExpressionSyntax memberAccess
                when ContainsProjectionHook(
                    memberAccess.Expression,
                    LinqraftProjectionHookKind.LeftJoin,
                    cancellationToken
                ):
                return TryBuildLeftJoinConditional(
                    memberAccess.Expression,
                    value => applyTail($"{value}.{EmitSimpleName(memberAccess.Name)}"),
                    rootLeftJoinExpression,
                    out rewritten,
                    cancellationToken
                );
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberInvocation
                    && ContainsProjectionHook(
                        memberInvocation.Expression,
                        LinqraftProjectionHookKind.LeftJoin,
                        cancellationToken
                    ):
                return TryBuildLeftJoinConditional(
                    memberInvocation.Expression,
                    value =>
                        applyTail(
                            EmitInvocationFromReceiver(
                                invocation,
                                memberInvocation.Name,
                                value,
                                cancellationToken
                            )
                        ),
                    rootLeftJoinExpression,
                    out rewritten,
                    cancellationToken
                );
            case ElementAccessExpressionSyntax elementAccess
                when ContainsProjectionHook(
                    elementAccess.Expression,
                    LinqraftProjectionHookKind.LeftJoin,
                    cancellationToken
                ):
                return TryBuildLeftJoinConditional(
                    elementAccess.Expression,
                    value =>
                        applyTail(
                            $"{value}{EmitBracketArguments(elementAccess.ArgumentList, cancellationToken)}"
                        ),
                    rootLeftJoinExpression,
                    out rewritten,
                    cancellationToken
                );
            default:
                rewritten = string.Empty;
                return false;
        }
    }

    private string CreateLeftJoinFallback(
        ExpressionSyntax expression,
        string expressionTypeName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var expressionType = GetExpressionType(expression, cancellationToken);
        if (ShouldUseCollectionFallback(expression, expressionType))
        {
            return CreateEmptyCollectionFallback(expressionTypeName, cancellationToken);
        }

        return
            expressionType?.IsReferenceType == true
            || expressionType?.NullableAnnotation == NullableAnnotation.Annotated
            ? "null"
            : $"default({expressionTypeName})";
    }

    private bool TryEmitAsProjectionHook(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (
            expression is not InvocationExpressionSyntax invocation
            || !TryGetProjectionHookInvocation(
                invocation,
                LinqraftProjectionHookKind.Projection,
                out var hookInvocation,
                cancellationToken
            )
        )
        {
            return false;
        }

        var targetType = GetProjectionSourceType(
            hookInvocation.TargetExpression,
            cancellationToken
        );
        if (targetType is null)
        {
            return false;
        }

        var useAnonymousProjection =
            !_replacementTypes.TryGetValue(expression.Span, out var replacementType)
            && hookInvocation.GenericTypeArgument is null;
        var dtoTypeName = useAnonymousProjection
            ? "object"
            : replacementType;
        if (!useAnonymousProjection && hookInvocation.GenericTypeArgument is not null)
        {
            dtoTypeName = QualifyType(hookInvocation.GenericTypeArgument, cancellationToken);
        }

        rewritten = BuildAsProjectionExpression(
            hookInvocation.TargetExpression,
            targetType,
            dtoTypeName,
            useAnonymousProjection,
            cancellationToken
        );
        return true;
    }

    private bool TryEmitProjectedValueSelection(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (
            expression is not InvocationExpressionSyntax invocation
            || !TryGetProjectedValueSelection(
                invocation,
                out var selectionInfo,
                cancellationToken
            )
        )
        {
            return false;
        }

        var targetExpression = selectionInfo.ProjectInvocation.TargetExpression;
        var targetType = GetExpressionType(targetExpression, cancellationToken);
        if (targetType is null)
        {
            return false;
        }

        var declarationModel = _semanticModel.Compilation.GetSemanticModel(
            selectionInfo.SelectorBody.SyntaxTree
        );
        var targetNamedType =
            GetProjectionSourceType(targetExpression, cancellationToken)
            ?? _semanticModel.Compilation.ObjectType;
        var parameterBindings = BuildLambdaParameterBindings(
            selectionInfo.Selector,
            declarationModel,
            targetExpression.WithoutTrivia(),
            cancellationToken
        );
        var rewriter = new ProjectableInliningRewriter(
            declarationModel,
            targetNamedType,
            receiverExpression: null,
            parameterBindings
        );
        var expandedBody =
            (ExpressionSyntax?)rewriter.Visit(selectionInfo.SelectorBody)
            ?? selectionInfo.SelectorBody;
        var emittedBody =
            selectionInfo.SelectorBody is AnonymousObjectCreationExpressionSyntax originalAnonymous
            && _replacementTypes.TryGetValue(originalAnonymous.Span, out var projectedType)
            && expandedBody is AnonymousObjectCreationExpressionSyntax expandedAnonymous
                ? EmitProjectedAnonymousObject(expandedAnonymous, projectedType, cancellationToken)
                : Emit(expandedBody, cancellationToken);
        if (targetType.NullableAnnotation != NullableAnnotation.Annotated)
        {
            rewritten = emittedBody;
            return true;
        }

        var receiver = Emit(targetExpression, cancellationToken);
        var fallback = CreateProjectedValueFallback(
            selectionInfo.SelectionInvocation,
            selectionInfo.SelectorBody,
            cancellationToken
        );
        if (ContainsLineBreak(emittedBody) || ContainsLineBreak(fallback))
        {
            rewritten = string.Join(
                "\n",
                $"{receiver} != null",
                IndentAllLines(AppendValueWithContinuation("? ", emittedBody)),
                IndentAllLines(AppendValueWithContinuation(": ", fallback))
            );
        }
        else
        {
            rewritten = $"{receiver} != null ? {emittedBody} : {fallback}";
        }

        return true;
    }

    private string BuildAsProjectionExpression(
        ExpressionSyntax targetExpression,
        INamedTypeSymbol targetType,
        string dtoTypeName,
        bool useAnonymousProjection,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var receiver = Emit(targetExpression, cancellationToken);
        var receiverForAccess = WrapMemberAccessReceiver(receiver);
        var assignments = GetReadableProjectionProperties(targetType)
            .Select(property =>
                $"{property.Name} = {receiverForAccess}.{property.Name}"
            )
            .ToList();
        var projection = useAnonymousProjection
            ? BuildInitializerExpression("new", assignments, cancellationToken)
            : BuildInitializerExpression(
                $"new {RemoveNullableAnnotation(dtoTypeName)}",
                assignments,
                cancellationToken
            );
        if (useAnonymousProjection)
        {
            projection = WrapCastExpression("(object)", projection);
        }
        var targetExpressionType = GetExpressionType(targetExpression, cancellationToken);
        if (targetExpressionType?.NullableAnnotation != NullableAnnotation.Annotated)
        {
            return projection;
        }

        if (ContainsLineBreak(projection))
        {
            return string.Join(
                "\n",
                $"{receiver} != null",
                IndentAllLines(AppendValueWithContinuation("? ", projection)),
                IndentAllLines(": null")
            );
        }

        return $"{receiver} != null ? {projection} : null";
    }

    private string CreateProjectedValueFallback(
        InvocationExpressionSyntax selectionInvocation,
        ExpressionSyntax selectorBody,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (_replacementTypes.TryGetValue(selectorBody.Span, out _))
        {
            return "null";
        }

        var selectorBodyType = GetExpressionType(selectorBody, cancellationToken);
        if (
            selectorBodyType?.IsReferenceType == true
            || selectorBodyType?.NullableAnnotation == NullableAnnotation.Annotated
        )
        {
            return "null";
        }

        var expressionTypeName = GetExpressionTypeName(
            selectionInvocation,
            GetExpressionType(selectionInvocation, cancellationToken),
            out _
        );
        return $"default({RemoveNullableAnnotation(expressionTypeName)})";
    }

    private string EmitProjectedAnonymousObject(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string projectedType,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var initializers = anonymousObject
            .Initializers.Select(initializer =>
            {
                var memberName =
                    initializer.NameEquals?.Name.Identifier.ValueText
                    ?? GetAnonymousMemberName(initializer);
                return AppendValueWithContinuation(
                    $"{memberName} = ",
                    EmitNestedExpression(initializer.Expression, cancellationToken)
                );
            })
            .ToList();

        return BuildInitializerExpression(
            $"new {projectedType}",
            initializers,
            cancellationToken
        );
    }

    private Dictionary<ISymbol, ExpressionSyntax> BuildLambdaParameterBindings(
        LambdaExpressionSyntax lambda,
        SemanticModel declarationModel,
        ExpressionSyntax replacementExpression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var bindings = new Dictionary<ISymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);
        foreach (var parameter in GetLambdaParameters(lambda))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declarationModel.GetDeclaredSymbol(parameter, cancellationToken) is { } symbol)
            {
                bindings[symbol] = replacementExpression;
            }
        }

        return bindings;
    }

    private static IEnumerable<ParameterSyntax> GetLambdaParameters(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized
                .ParameterList.Parameters,
            _ => [],
        };
    }

    private static IReadOnlyList<IPropertySymbol> GetReadableProjectionProperties(
        INamedTypeSymbol sourceType
    )
    {
        return sourceType
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                !property.IsStatic
                && property.GetMethod is not null
                && property.Parameters.Length == 0
                && property.DeclaredAccessibility == Accessibility.Public
                && IsProjectionLeafType(property.Type)
            )
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsProjectionLeafType(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_String
            || type.TypeKind == TypeKind.Enum
            || type.IsValueType;
    }

    private INamedTypeSymbol? GetProjectionSourceType(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var type = GetExpressionType(expression, cancellationToken);
        return type switch
        {
            INamedTypeSymbol namedType
                when namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T =>
                namedType.TypeArguments[0] as INamedTypeSymbol,
            INamedTypeSymbol namedType => namedType,
            _ => null,
        };
    }

    private static string WrapMemberAccessReceiver(string receiver)
    {
        if (
            !ContainsLineBreak(receiver)
            && (
                receiver.Length == 0
                || receiver.All(character =>
                    char.IsLetterOrDigit(character)
                    || character is '_' or '.' or '[' or ']' or '!' or '?'
                )
            )
        )
        {
            return receiver;
        }

        if (!ContainsLineBreak(receiver))
        {
            return $"({receiver})";
        }

        return string.Join("\n", "(", IndentAllLines(receiver), ")");
    }

    private static string RemoveNullableAnnotation(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName[..^1] : typeName;
    }

    private bool TryEmitProjectableHook(
        ExpressionSyntax expression,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        return expression is InvocationExpressionSyntax invocation
            && TryEmitProjectableHook(
                invocation,
                overrideReceiver: null,
                out rewritten,
                cancellationToken
            );
    }

    private bool TryEmitProjectableHook(
        InvocationExpressionSyntax invocation,
        string? overrideReceiver,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (
            !IsProjectionHookInvocation(
                invocation,
                LinqraftProjectionHookKind.Projectable,
                cancellationToken
            )
        )
        {
            return false;
        }

        if (
            !TryExpandProjectableInvocation(
                invocation,
                overrideReceiver,
                out var expanded,
                out var expandedSymbol,
                cancellationToken
            )
        )
        {
            return false;
        }

        if (expandedSymbol is not null && !_activeProjectableSymbols.Add(expandedSymbol))
        {
            throw new global::System.InvalidOperationException(
                $"Detected recursive AsProjectable expansion for '{expandedSymbol.ToDisplayString()}'."
            );
        }

        try
        {
            rewritten = Emit(expanded, cancellationToken);
            return true;
        }
        finally
        {
            if (expandedSymbol is not null)
            {
                _activeProjectableSymbols.Remove(expandedSymbol);
            }
        }
    }

    private bool TryEmitExtensionInvocation(
        InvocationExpressionSyntax invocation,
        SimpleNameSyntax methodName,
        string receiver,
        out string rewritten,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        rewritten = string.Empty;
        if (!BelongsToSemanticModel(invocation))
        {
            return false;
        }

        var symbolInfo = _semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var methodSymbol =
            symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        var reducedFrom = methodSymbol?.ReducedFrom;
        if (reducedFrom is null)
        {
            return false;
        }

        var arguments = invocation
            .ArgumentList.Arguments.Select(argument => EmitArgument(argument, cancellationToken))
            .ToList();
        arguments.Insert(0, receiver);
        rewritten = BuildInvocationExpression(
            $"{reducedFrom.ContainingType.ToFullyQualifiedTypeName()}.{EmitSimpleName(methodName)}",
            arguments,
            cancellationToken
        );
        return true;
    }

    private bool TryExpandProjectableInvocation(
        InvocationExpressionSyntax invocation,
        string? overrideReceiver,
        out ExpressionSyntax expanded,
        out ISymbol? expandedSymbol,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        var targetExpression = GetHookTargetExpression(invocation, cancellationToken);
        if (targetExpression is null)
        {
            expanded = invocation;
            expandedSymbol = null;
            return false;
        }

        if (
            TryExpandProjectableProperty(
                targetExpression,
                overrideReceiver,
                out expanded,
                out expandedSymbol,
                cancellationToken
            )
        )
        {
            return true;
        }

        if (targetExpression is InvocationExpressionSyntax targetInvocation)
        {
            return TryExpandProjectableMethod(
                targetInvocation,
                overrideReceiver,
                out expanded,
                out expandedSymbol,
                cancellationToken
            );
        }

        expanded = invocation;
        expandedSymbol = null;
        return false;
    }

    private bool TryExpandProjectableProperty(
        ExpressionSyntax targetExpression,
        string? overrideReceiver,
        out ExpressionSyntax expanded,
        out ISymbol? expandedSymbol,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (!BelongsToSemanticModel(targetExpression))
        {
            expanded = targetExpression;
            expandedSymbol = null;
            return false;
        }

        var propertySymbol =
            _semanticModel.GetSymbolInfo(targetExpression, cancellationToken).Symbol
                as IPropertySymbol
            ?? _semanticModel
                .GetSymbolInfo(targetExpression, cancellationToken)
                .CandidateSymbols.OfType<IPropertySymbol>()
                .FirstOrDefault();
        if (propertySymbol?.IsStatic == true)
        {
            expanded = targetExpression;
            expandedSymbol = null;
            return false;
        }

        var propertySyntax =
            propertySymbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
            as PropertyDeclarationSyntax;
        var bodyExpression = GetProjectableBodyExpression(propertySyntax);
        if (bodyExpression is null)
        {
            expanded = targetExpression;
            expandedSymbol = null;
            return false;
        }

        var declarationModel = _semanticModel.Compilation.GetSemanticModel(
            bodyExpression.SyntaxTree
        );
        EnsureProjectableExpansionIsAcyclic(
            propertySymbol!,
            bodyExpression,
            declarationModel,
            new HashSet<ISymbol>(_activeProjectableSymbols, SymbolEqualityComparer.Default),
            cancellationToken
        );
        var receiverExpression = overrideReceiver is null
            ? GetProjectableReceiverExpression(targetExpression)
            : SyntaxFactory.ParseExpression(overrideReceiver);
        var rewriter = new ProjectableInliningRewriter(
            declarationModel,
            propertySymbol!.ContainingType,
            receiverExpression,
            parameterBindings: null
        );
        expanded =
            (ExpressionSyntax?)rewriter.Visit(bodyExpression.WithoutTrivia()) ?? bodyExpression;
        expandedSymbol = propertySymbol;
        return true;
    }

    private bool TryExpandProjectableMethod(
        InvocationExpressionSyntax targetInvocation,
        string? overrideReceiver,
        out ExpressionSyntax expanded,
        out ISymbol? expandedSymbol,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (!BelongsToSemanticModel(targetInvocation))
        {
            expanded = targetInvocation;
            expandedSymbol = null;
            return false;
        }

        var methodSymbol =
            _semanticModel.GetSymbolInfo(targetInvocation, cancellationToken).Symbol
                as IMethodSymbol
            ?? _semanticModel
                .GetSymbolInfo(targetInvocation, cancellationToken)
                .CandidateSymbols.OfType<IMethodSymbol>()
                .FirstOrDefault();
        if (methodSymbol is null || methodSymbol.IsStatic)
        {
            expanded = targetInvocation;
            expandedSymbol = null;
            return false;
        }

        var methodSyntax =
            methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
            as MethodDeclarationSyntax;
        var bodyExpression = GetProjectableBodyExpression(methodSyntax);
        if (bodyExpression is null)
        {
            expanded = targetInvocation;
            expandedSymbol = null;
            return false;
        }

        var declarationModel = _semanticModel.Compilation.GetSemanticModel(
            bodyExpression.SyntaxTree
        );
        EnsureProjectableExpansionIsAcyclic(
            methodSymbol,
            bodyExpression,
            declarationModel,
            new HashSet<ISymbol>(_activeProjectableSymbols, SymbolEqualityComparer.Default),
            cancellationToken
        );
        var receiverExpression = overrideReceiver is null
            ? GetProjectableReceiverExpression(targetInvocation)
            : SyntaxFactory.ParseExpression(overrideReceiver);
        var parameterBindings = BuildProjectableParameterBindings(
            targetInvocation,
            methodSymbol,
            declarationModel,
            cancellationToken
        );
        var rewriter = new ProjectableInliningRewriter(
            declarationModel,
            methodSymbol.ContainingType,
            receiverExpression,
            parameterBindings
        );
        expanded =
            (ExpressionSyntax?)rewriter.Visit(bodyExpression.WithoutTrivia()) ?? bodyExpression;
        expandedSymbol = methodSymbol;
        return true;
    }

    private static ExpressionSyntax? GetProjectableBodyExpression(
        PropertyDeclarationSyntax? property
    )
    {
        if (property is null)
        {
            return null;
        }

        if (property.ExpressionBody is not null)
        {
            return property.ExpressionBody.Expression;
        }

        var getter = property.AccessorList?.Accessors.FirstOrDefault(accessor =>
            accessor.IsKind(SyntaxKind.GetAccessorDeclaration)
        );
        return GetProjectableBodyExpression(getter);
    }

    private static ExpressionSyntax? GetProjectableBodyExpression(MethodDeclarationSyntax? method)
    {
        if (method is null)
        {
            return null;
        }

        if (method.ExpressionBody is not null)
        {
            return method.ExpressionBody.Expression;
        }

        return method
            .Body?.Statements.OfType<ReturnStatementSyntax>()
            .Select(statement => statement.Expression)
            .FirstOrDefault(expression => expression is not null);
    }

    private static ExpressionSyntax? GetProjectableBodyExpression(
        AccessorDeclarationSyntax? accessor
    )
    {
        if (accessor is null)
        {
            return null;
        }

        if (accessor.ExpressionBody is not null)
        {
            return accessor.ExpressionBody.Expression;
        }

        return accessor
            .Body?.Statements.OfType<ReturnStatementSyntax>()
            .Select(statement => statement.Expression)
            .FirstOrDefault(expression => expression is not null);
    }

    private static Dictionary<ISymbol, ExpressionSyntax> BuildProjectableParameterBindings(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        SemanticModel declarationModel,
        CancellationToken cancellationToken = default
    )
    {
        var bindings = new Dictionary<ISymbol, ExpressionSyntax>(SymbolEqualityComparer.Default);
        Dictionary<string, ISymbol> parameterSyntaxByName;
        if (
            methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
            is MethodDeclarationSyntax methodSyntax
        )
        {
            parameterSyntaxByName = methodSyntax.ParameterList.Parameters.ToDictionary(
                parameter => parameter.Identifier.ValueText,
                parameter => (ISymbol)declarationModel.GetDeclaredSymbol(parameter)!,
                global::System.StringComparer.Ordinal
            );
        }
        else
        {
            parameterSyntaxByName = new Dictionary<string, ISymbol>(
                global::System.StringComparer.Ordinal
            );
        }

        for (var index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var argument = invocation.ArgumentList.Arguments[index];
            var parameterName =
                argument.NameColon?.Name.Identifier.ValueText
                ?? (
                    index < methodSymbol.Parameters.Length
                        ? methodSymbol.Parameters[index].Name
                        : null
                );
            if (
                parameterName is null
                || !parameterSyntaxByName.TryGetValue(parameterName, out var parameterSymbol)
            )
            {
                continue;
            }

            bindings[parameterSymbol] = argument.Expression.WithoutTrivia();
        }

        return bindings;
    }

    private static ExpressionSyntax? GetProjectableReceiverExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression.WithoutTrivia(),
            InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Expression.WithoutTrivia(),
            _ => null,
        };
    }

    private void EnsureProjectableExpansionIsAcyclic(
        ISymbol symbol,
        ExpressionSyntax bodyExpression,
        SemanticModel semanticModel,
        ISet<ISymbol> activeSymbols,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        if (!activeSymbols.Add(symbol))
        {
            throw new global::System.InvalidOperationException(
                $"Detected recursive AsProjectable expansion for '{symbol.ToDisplayString()}'."
            );
        }

        try
        {
            foreach (
                var invocation in bodyExpression
                    .DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>()
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    !IsProjectionHookInvocation(
                        invocation,
                        LinqraftProjectionHookKind.Projectable,
                        cancellationToken
                    )
                )
                {
                    continue;
                }

                var targetExpression = GetHookTargetExpression(invocation, cancellationToken);
                if (
                    targetExpression is null
                    || !TryGetProjectableTargetSymbol(
                        targetExpression,
                        semanticModel,
                        out var nestedSymbol,
                        out var nestedBodyExpression,
                        out var nestedSemanticModel,
                        cancellationToken
                    )
                )
                {
                    continue;
                }

                EnsureProjectableExpansionIsAcyclic(
                    nestedSymbol,
                    nestedBodyExpression,
                    nestedSemanticModel,
                    activeSymbols,
                    cancellationToken
                );
            }
        }
        finally
        {
            activeSymbols.Remove(symbol);
        }
    }

    private static bool TryGetProjectableTargetSymbol(
        ExpressionSyntax targetExpression,
        SemanticModel semanticModel,
        out ISymbol symbol,
        out ExpressionSyntax bodyExpression,
        out SemanticModel declarationModel,
        CancellationToken cancellationToken = default
    )
    {
        symbol = null!;
        bodyExpression = null!;
        declarationModel = null!;

        switch (targetExpression)
        {
            case InvocationExpressionSyntax targetInvocation:
            {
                var methodSymbol =
                    semanticModel.GetSymbolInfo(targetInvocation, cancellationToken).Symbol
                        as IMethodSymbol
                    ?? semanticModel
                        .GetSymbolInfo(targetInvocation, cancellationToken)
                        .CandidateSymbols.OfType<IMethodSymbol>()
                        .FirstOrDefault();
                if (methodSymbol is null || methodSymbol.IsStatic)
                {
                    return false;
                }

                var methodSyntax =
                    methodSymbol
                        .DeclaringSyntaxReferences.FirstOrDefault()
                        ?.GetSyntax(cancellationToken) as MethodDeclarationSyntax;
                var nestedBodyExpression = GetProjectableBodyExpression(methodSyntax);
                if (nestedBodyExpression is null)
                {
                    return false;
                }

                symbol = methodSymbol;
                bodyExpression = nestedBodyExpression;
                declarationModel = semanticModel.Compilation.GetSemanticModel(
                    nestedBodyExpression.SyntaxTree
                );
                return true;
            }
            default:
            {
                var propertySymbol =
                    semanticModel.GetSymbolInfo(targetExpression, cancellationToken).Symbol
                        as IPropertySymbol
                    ?? semanticModel
                        .GetSymbolInfo(targetExpression, cancellationToken)
                        .CandidateSymbols.OfType<IPropertySymbol>()
                        .FirstOrDefault();
                if (propertySymbol is null || propertySymbol.IsStatic)
                {
                    return false;
                }

                var propertySyntax =
                    propertySymbol
                        .DeclaringSyntaxReferences.FirstOrDefault()
                        ?.GetSyntax(cancellationToken) as PropertyDeclarationSyntax;
                var nestedBodyExpression = GetProjectableBodyExpression(propertySyntax);
                if (nestedBodyExpression is null)
                {
                    return false;
                }

                symbol = propertySymbol;
                bodyExpression = nestedBodyExpression;
                declarationModel = semanticModel.Compilation.GetSemanticModel(
                    nestedBodyExpression.SyntaxTree
                );
                return true;
            }
        }
    }

    private bool IsProjectionHookInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftProjectionHookKind kind,
        CancellationToken cancellationToken = default
    )
    {
        return TryGetProjectionHookInvocation(
            invocation,
            kind,
            out _,
            cancellationToken
        );
    }

    private bool ContainsProjectionHook(
        ExpressionSyntax expression,
        LinqraftProjectionHookKind kind,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsProjectionHookInvocation(invocation, kind, cancellationToken));
    }

    private ExpressionSyntax? GetHookTargetExpression(
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        foreach (
            LinqraftProjectionHookKind kind in Enum.GetValues(
                typeof(LinqraftProjectionHookKind)
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetProjectionHookInvocation(invocation, kind, out var hookInvocation, cancellationToken))
            {
                return hookInvocation.TargetExpression;
            }
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    }

    private bool TryGetProjectionHookInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftProjectionHookKind kind,
        out ProjectionHookSyntaxHelper.HookInvocationInfo hookInvocation,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return ProjectionHookSyntaxHelper.TryGetHookInvocation(
            invocation,
            _semanticModel,
            _generatorOptions,
            _projectionHelperParameterName,
            _projectionHelperParameterTypeName,
            kind,
            out hookInvocation,
            cancellationToken
        );
    }

    private bool TryGetProjectedValueSelection(
        InvocationExpressionSyntax invocation,
        out ProjectionHookSyntaxHelper.ProjectedValueSelectionInfo selectionInfo,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return ProjectionHookSyntaxHelper.TryGetProjectedValueSelection(
            invocation,
            _semanticModel,
            _generatorOptions,
            _projectionHelperParameterName,
            _projectionHelperParameterTypeName,
            out selectionInfo,
            cancellationToken
        );
    }

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

    private static string CreateEmptyCollectionFallback(
        string typeName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    private bool BelongsToSemanticModel(SyntaxNode node)
    {
        return node.SyntaxTree == _semanticModel.SyntaxTree;
    }

    private static string NormalizeExpressionText(ExpressionSyntax expression)
    {
        return expression.WithoutTrivia().ToString();
    }

    private ISymbol? GetRootSymbol(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var rootExpression = GetRootExpression(expression);
        cancellationToken = ResolveCancellationToken(cancellationToken);
        return _semanticModel.GetSymbolInfo(rootExpression, cancellationToken).Symbol;
    }

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

    private static string WrapCastExpression(string castPrefix, string expression)
    {
        if (!ContainsLineBreak(expression))
        {
            return $"{castPrefix}({expression})";
        }

        return string.Join("\n", $"{castPrefix}(", IndentAllLines(expression), ")");
    }

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

    private static bool ShouldExpandInitializer(IReadOnlyList<string> items)
    {
        return items.Count > 1 || items.Any(ContainsLineBreak);
    }

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

    private static string IndentAllLines(string value, int indentLevel = 1)
    {
        var prefix = new string(' ', indentLevel * 4);
        return string.Join("\n", SplitLines(value).Select(line => prefix + line));
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
    }

    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static string GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        return AnonymousMemberNameResolver.Get(initializer.Expression);
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

    private sealed class ProjectableInliningRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly INamedTypeSymbol _declaringType;
        private readonly ExpressionSyntax? _receiverExpression;
        private readonly IReadOnlyDictionary<ISymbol, ExpressionSyntax> _parameterBindings;

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

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node)
        {
            return _receiverExpression is null
                ? base.VisitThisExpression(node)
                : Parenthesize(_receiverExpression).WithTriviaFrom(node);
        }

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

        private static ParenthesizedExpressionSyntax Parenthesize(ExpressionSyntax expression)
        {
            return expression is ParenthesizedExpressionSyntax parenthesized
                ? parenthesized
                : SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia());
        }
    }

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
