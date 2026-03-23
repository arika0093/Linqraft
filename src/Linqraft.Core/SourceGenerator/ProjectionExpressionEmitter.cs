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
    // The emitter rewrites selector syntax into concrete C# snippets that generated interceptors can embed.

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

    /// <summary>
    /// Initializes a new instance of the ProjectionExpressionEmitter class.
    /// </summary>
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

    /// <summary>
    /// Resolves cancellation token.
    /// </summary>
    private static CancellationToken ResolveCancellationToken(CancellationToken cancellationToken)
    {
        return cancellationToken;
    }

    /// <summary>
    /// Emits the supplied expression as generated C# source.
    /// </summary>
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

    /// <summary>
    /// Emits anonymous object.
    /// </summary>
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

    /// <summary>
    /// Emits object creation.
    /// </summary>
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

    /// <summary>
    /// Emits initializer.
    /// </summary>
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

    /// <summary>
    /// Emits initializer item.
    /// </summary>
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

    /// <summary>
    /// Emits collection expression.
    /// </summary>
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

    /// <summary>
    /// Emits conditional expression.
    /// </summary>
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

    /// <summary>
    /// Emits binary expression.
    /// </summary>
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

    /// <summary>
    /// Emits binary operand.
    /// </summary>
    private string EmitBinaryOperand(
        ExpressionSyntax expression,
        CancellationToken cancellationToken = default
    )
    {
        var emitted = Emit(expression, cancellationToken);
        return NeedsParenthesesInBinary(expression) ? $"({emitted})" : emitted;
    }

    /// <summary>
    /// Determines whether the expression needs parentheses in binary.
    /// </summary>
    private static bool NeedsParenthesesInBinary(ExpressionSyntax expression)
    {
        return expression is not ParenthesizedExpressionSyntax
            && (expression is ConditionalExpressionSyntax || ContainsConditionalAccess(expression));
    }

    /// <summary>
    /// Attempts to emit collection fallback coalesce.
    /// </summary>
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

    /// <summary>
    /// Emits invocation.
    /// </summary>
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

    /// <summary>
    /// Emits projection invocation.
    /// </summary>
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

    /// <summary>
    /// Emits group by expr invocation.
    /// </summary>
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

    /// <summary>
    /// Emits member invocation.
    /// </summary>
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

    /// <summary>
    /// Emits argument list.
    /// </summary>
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

    /// <summary>
    /// Emits argument.
    /// </summary>
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

    /// <summary>
    /// Emits bracket arguments.
    /// </summary>
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

    /// <summary>
    /// Emits simple name.
    /// </summary>
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

    /// <summary>
    /// Emits identifier.
    /// </summary>
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

    /// <summary>
    /// Emits lambda body.
    /// </summary>
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

    /// <summary>
    /// Emits lambda.
    /// </summary>
    private string EmitLambda(
        string parameterList,
        CSharpSyntaxNode body,
        CancellationToken cancellationToken = default
    )
    {
        return AppendValueInline($"{parameterList} => ", EmitLambdaBody(body, cancellationToken));
    }

    /// <summary>
    /// Emits nested expression.
    /// </summary>
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

    /// <summary>
    /// Qualifies type.
    /// </summary>
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

    /// <summary>
    /// Attempts to emit conditional chain.
    /// </summary>
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

    /// <summary>
    /// Attempts to build conditional.
    /// </summary>
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

    /// <summary>
    /// Binds conditional receiver.
    /// </summary>
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

    /// <summary>
    /// Determines whether the expression contains a conditional access.
    /// </summary>
    private static bool ContainsConditionalAccess(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any();
    }

    /// <summary>
    /// Attempts to emit fluent chain.
    /// </summary>
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

    /// <summary>
    /// Attempts to handle decompose fluent chain.
    /// </summary>
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

    /// <summary>
    /// Gets fluent invocation segment.
    /// </summary>
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

    /// <summary>
    /// Emits invocation from receiver.
    /// </summary>
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

    /// <summary>
    /// Attempts to emit projection hook.
    /// </summary>
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

    /// <summary>
    /// Attempts to emit left join hook.
    /// </summary>
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

    /// <summary>
    /// Attempts to emit inner join hook.
    /// </summary>
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

        return TryBuildInnerJoinAccess(
            expression,
            value => value,
            out rewritten,
            cancellationToken
        );
    }

    /// <summary>
    /// Attempts to build inner join access.
    /// </summary>
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

    /// <summary>
    /// Attempts to build left join conditional.
    /// </summary>
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

    /// <summary>
    /// Creates left join fallback.
    /// </summary>
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

    /// <summary>
    /// Attempts to emit as projection hook.
    /// </summary>
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
        var dtoTypeName = useAnonymousProjection ? "object" : replacementType;
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

    /// <summary>
    /// Attempts to emit projected value selection.
    /// </summary>
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
            || !TryGetProjectedValueSelection(invocation, out var selectionInfo, cancellationToken)
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

    /// <summary>
    /// Builds as projection expression.
    /// </summary>
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
            .Select(property => $"{property.Name} = {receiverForAccess}.{property.Name}")
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

    /// <summary>
    /// Creates projected value fallback.
    /// </summary>
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

    /// <summary>
    /// Emits projected anonymous object.
    /// </summary>
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

        return BuildInitializerExpression($"new {projectedType}", initializers, cancellationToken);
    }
}
