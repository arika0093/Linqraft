using System.Collections.Generic;
using System.Linq;
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
    private readonly IReadOnlyList<LinqraftExtensionMethodInfo> _extensions;

    public ProjectionExpressionEmitter(
        SemanticModel semanticModel,
        ExpressionSyntax rootExpression,
        string rootTypeName,
        bool useEmptyCollectionFallback,
        IReadOnlyDictionary<TextSpan, string>? replacementTypes = null,
        IReadOnlyList<CaptureEntry>? captureEntries = null,
        IReadOnlyList<LinqraftExtensionMethodInfo>? extensions = null
    )
    {
        _semanticModel = semanticModel;
        _rootExpression = rootExpression;
        _rootTypeName = rootTypeName;
        _useEmptyCollectionFallback = useEmptyCollectionFallback;
        _replacementTypes = replacementTypes ?? new Dictionary<TextSpan, string>();
        _captureEntries = captureEntries ?? global::System.Array.Empty<CaptureEntry>();
        _extensions = extensions ?? global::System.Array.Empty<LinqraftExtensionMethodInfo>();
    }

    public string Emit(ExpressionSyntax expression)
    {
        if (TryEmitCaptureReplacement(expression, out var captureReplacement))
        {
            return captureReplacement;
        }

        if (TryEmitConditionalChain(expression, out var conditionalText))
        {
            return conditionalText;
        }

        if (TryEmitFluentChain(expression, out var fluentChain))
        {
            return fluentChain;
        }

        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax anonymousObject => EmitAnonymousObject(
                anonymousObject
            ),
            ObjectCreationExpressionSyntax objectCreation => EmitObjectCreation(objectCreation),
            CollectionExpressionSyntax collectionExpression => EmitCollectionExpression(
                collectionExpression
            ),
            InvocationExpressionSyntax invocation => EmitInvocation(invocation),
            MemberAccessExpressionSyntax memberAccess
                when TryGetNullConditionalExtensionReceiver(
                    memberAccess.Expression,
                    out var nullCondReceiver
                ) => EmitNullConditionalAccessFromExtension(nullCondReceiver, memberAccess.Name),
            MemberAccessExpressionSyntax memberAccess =>
                $"{Emit(memberAccess.Expression)}.{EmitSimpleName(memberAccess.Name)}",
            ElementAccessExpressionSyntax elementAccess =>
                $"{Emit(elementAccess.Expression)}{EmitBracketArguments(elementAccess.ArgumentList)}",
            ConditionalExpressionSyntax conditionalExpression => EmitConditionalExpression(
                conditionalExpression
            ),
            BinaryExpressionSyntax binaryExpression => EmitBinaryExpression(binaryExpression),
            PrefixUnaryExpressionSyntax prefixUnary =>
                $"{prefixUnary.OperatorToken.Text}{Emit(prefixUnary.Operand)}",
            PostfixUnaryExpressionSyntax postfixUnary =>
                $"{Emit(postfixUnary.Operand)}{postfixUnary.OperatorToken.Text}",
            ParenthesizedExpressionSyntax parenthesized => $"({Emit(parenthesized.Expression)})",
            CastExpressionSyntax castExpression => EmitCastExpression(castExpression),
            AssignmentExpressionSyntax assignment =>
                $"{Emit(assignment.Left)} {assignment.OperatorToken.Text} {Emit(assignment.Right)}",
            SimpleLambdaExpressionSyntax lambda => EmitLambda(
                lambda.Parameter.Identifier.ValueText,
                lambda.Body
            ),
            ParenthesizedLambdaExpressionSyntax lambda => EmitLambda(
                $"({string.Join(", ", lambda.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText))})",
                lambda.Body
            ),
            IdentifierNameSyntax identifier => EmitIdentifier(identifier),
            GenericNameSyntax genericName => EmitSimpleName(genericName),
            ThisExpressionSyntax => "this",
            BaseExpressionSyntax => "base",
            LiteralExpressionSyntax => expression.ToString(),
            InterpolatedStringExpressionSyntax => expression.ToString(),
            TypeOfExpressionSyntax typeOfExpression =>
                $"typeof({QualifyType(typeOfExpression.Type)})",
            DefaultExpressionSyntax defaultExpression =>
                $"default({QualifyType(defaultExpression.Type)})",
            InitializerExpressionSyntax initializer => EmitInitializer(initializer),
            ImplicitArrayCreationExpressionSyntax => expression.ToString(),
            ArrayCreationExpressionSyntax => expression.ToString(),
            _ => expression.ToString(),
        };
    }

    private string EmitAnonymousObject(AnonymousObjectCreationExpressionSyntax expression)
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
                    EmitNestedExpression(initializer.Expression)
                );
            })
            .ToList();

        return BuildInitializerExpression(
            replacementType is null ? "new" : $"new {replacementType}",
            initializers
        );
    }

    private string EmitObjectCreation(ObjectCreationExpressionSyntax expression)
    {
        var arguments = expression.ArgumentList is null
            ? string.Empty
            : EmitArgumentList(expression.ArgumentList);
        var header = $"new {QualifyType(expression.Type)}{arguments}";
        if (expression.Initializer is null)
        {
            return header;
        }

        return BuildInitializerExpression(
            header,
            expression.Initializer.Expressions.Select(EmitInitializerItem).ToList()
        );
    }

    private string EmitInitializer(InitializerExpressionSyntax expression)
    {
        return BuildInitializerBody(expression.Expressions.Select(EmitInitializerItem).ToList());
    }

    private string EmitInitializerItem(ExpressionSyntax expression)
    {
        if (expression is AssignmentExpressionSyntax assignment)
        {
            return AppendValueWithContinuation(
                $"{Emit(assignment.Left)} {assignment.OperatorToken.Text} ",
                EmitNestedExpression(assignment.Right)
            );
        }

        return EmitNestedExpression(expression);
    }

    private string EmitCollectionExpression(CollectionExpressionSyntax expression)
    {
        var targetTypeName = ResolveCollectionTargetTypeName(expression);
        if (expression.Elements.Count == 0)
        {
            return CreateEmptyCollectionFallback(targetTypeName);
        }

        var items = expression.Elements.Select(element =>
            element switch
            {
                ExpressionElementSyntax expressionElement => Emit(expressionElement.Expression),
                SpreadElementSyntax spreadElement => Emit(spreadElement.Expression),
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

        if (TryGetSingleGenericArgument(normalizedTypeName, out var genericArgument))
        {
            return $"new {genericArgument}[] {{ {string.Join(", ", items)} }}";
        }

        return $"new {normalizedTypeName} {{ {string.Join(", ", items)} }}";
    }

    private string EmitConditionalExpression(ConditionalExpressionSyntax expression)
    {
        var condition = Emit(expression.Condition);
        var whenTrue = Emit(expression.WhenTrue);
        var whenFalse = Emit(expression.WhenFalse);
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
            [
                condition,
                IndentAllLines(AppendValueWithContinuation("? ", whenTrue)),
                IndentAllLines(AppendValueWithContinuation(": ", whenFalse)),
            ]
        );
    }

    private string EmitBinaryExpression(BinaryExpressionSyntax expression)
    {
        if (
            expression.IsKind(SyntaxKind.CoalesceExpression)
            && TryEmitCollectionFallbackCoalesce(expression, out var rewritten)
        )
        {
            return rewritten;
        }

        return $"{EmitBinaryOperand(expression.Left)} {expression.OperatorToken.Text} {EmitBinaryOperand(expression.Right)}";
    }

    private string EmitBinaryOperand(ExpressionSyntax expression)
    {
        var emitted = Emit(expression);
        return NeedsParenthesesInBinary(expression) ? $"({emitted})" : emitted;
    }

    private static bool NeedsParenthesesInBinary(ExpressionSyntax expression)
    {
        return expression is not ParenthesizedExpressionSyntax
            && (expression is ConditionalExpressionSyntax || ContainsConditionalAccess(expression));
    }

    private bool TryEmitCollectionFallbackCoalesce(
        BinaryExpressionSyntax expression,
        out string rewritten
    )
    {
        rewritten = string.Empty;
        if (!IsEmptyCollectionExpression(expression.Right))
        {
            return false;
        }

        if (!ContainsConditionalAccess(expression.Left))
        {
            return false;
        }

        var expressionType = GetExpressionType(expression);
        var rootTypeName = GetExpressionTypeName(expression, expressionType, out _);
        var nestedEmitter = new ProjectionExpressionEmitter(
            _semanticModel,
            expression.Left,
            rootTypeName,
            useEmptyCollectionFallback: true,
            _replacementTypes,
            _captureEntries,
            _extensions
        );
        rewritten = nestedEmitter.Emit(expression.Left);
        return true;
    }

    private string EmitInvocation(InvocationExpressionSyntax expression)
    {
        if (
            expression.Expression is MemberAccessExpressionSyntax memberAccess
            && TryGetLinqraftExtensionInfo(expression, out var extensionInfo)
        )
        {
            var receiver = Emit(memberAccess.Expression);
            switch (extensionInfo.Behavior)
            {
                case LinqraftExtensionBehaviorKind.PassThrough:
                case LinqraftExtensionBehaviorKind.NullConditionalNavigation:
                    // Strip the extension call — just return the receiver.
                    // For NullConditionalNavigation, the null-conditional semantics are applied
                    // in the parent MemberAccessExpression case.
                    return receiver;
                case LinqraftExtensionBehaviorKind.CastToFirstTypeArgument:
                    if (
                        memberAccess.Name is GenericNameSyntax genericName
                        && genericName.TypeArgumentList.Arguments.Count > 0
                    )
                    {
                        var typeArg = QualifyType(genericName.TypeArgumentList.Arguments[0]);
                        return $"({typeArg}){receiver}";
                    }
                    return receiver;
            }
        }

        return expression.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess2 => memberAccess2
                .Name
                .Identifier
                .ValueText switch
            {
                "SelectExpr" => EmitProjectionInvocation(
                    Emit(memberAccess2.Expression),
                    expression,
                    "Select"
                ),
                "SelectManyExpr" => EmitProjectionInvocation(
                    Emit(memberAccess2.Expression),
                    expression,
                    "SelectMany"
                ),
                "GroupByExpr" => EmitGroupByExprInvocation(
                    Emit(memberAccess2.Expression),
                    expression
                ),
                _ => EmitMemberInvocation(
                    expression,
                    memberAccess2,
                    Emit(memberAccess2.Expression)
                ),
            },
            _ => $"{Emit(expression.Expression)}{EmitArgumentList(expression.ArgumentList)}",
        };
    }

    private string EmitProjectionInvocation(
        string receiver,
        InvocationExpressionSyntax expression,
        string projectionMethodName
    )
    {
        var selector = expression
            .ArgumentList.Arguments.Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
        if (selector is null)
        {
            return $"{receiver}.{projectionMethodName}{EmitArgumentList(expression.ArgumentList)}";
        }

        return $"{receiver}.{projectionMethodName}({Emit(selector)})";
    }

    private string EmitGroupByExprInvocation(string receiver, InvocationExpressionSyntax expression)
    {
        var lambdas = expression
            .ArgumentList.Arguments.Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .ToArray();
        if (lambdas.Length < 2)
        {
            return $"{receiver}.GroupBy{EmitArgumentList(expression.ArgumentList)}";
        }

        return $"{receiver}.GroupBy({Emit(lambdas[0])}).Select({Emit(lambdas[1])})";
    }

    private string EmitMemberInvocation(
        InvocationExpressionSyntax expression,
        MemberAccessExpressionSyntax memberAccess,
        string receiver
    )
    {
        return TryEmitExtensionInvocation(
            expression,
            memberAccess.Name,
            receiver,
            out var rewritten
        )
            ? rewritten
            : $"{receiver}.{EmitSimpleName(memberAccess.Name)}{EmitArgumentList(expression.ArgumentList)}";
    }

    /// <summary>
    /// Checks if the given expression is an invocation of a NullConditionalNavigation extension method,
    /// and if so, outputs the receiver expression (before the extension call).
    /// </summary>
    private bool TryGetNullConditionalExtensionReceiver(
        ExpressionSyntax expression,
        out ExpressionSyntax receiver
    )
    {
        receiver = null!;
        if (
            expression is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess
        )
        {
            return false;
        }

        if (!TryGetLinqraftExtensionInfo(invocation, out var extInfo))
        {
            return false;
        }

        if (extInfo.Behavior != LinqraftExtensionBehaviorKind.NullConditionalNavigation)
        {
            return false;
        }

        receiver = memberAccess.Expression;
        return true;
    }

    /// <summary>
    /// Emits a null-conditional member access of the form:
    /// <c>receiver != null ? receiver.Member : null</c>
    /// to simulate left-join behavior.
    /// </summary>
    private string EmitNullConditionalAccessFromExtension(
        ExpressionSyntax receiver,
        SimpleNameSyntax memberName
    )
    {
        var receiverText = Emit(receiver);
        var memberText = EmitSimpleName(memberName);
        return $"{receiverText} != null ? {receiverText}.{memberText} : null";
    }

    /// <summary>
    /// Tries to resolve extension info for an invocation by checking the method name
    /// against the list of known Linqraft extensions.
    /// </summary>
    private bool TryGetLinqraftExtensionInfo(
        InvocationExpressionSyntax invocation,
        out LinqraftExtensionMethodInfo extensionInfo
    )
    {
        extensionInfo = null!;
        if (_extensions.Count == 0)
        {
            return false;
        }

        string? methodName = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.ValueText;
        }

        if (methodName is null)
        {
            return false;
        }

        foreach (var ext in _extensions)
        {
            if (string.Equals(ext.MethodName, methodName, System.StringComparison.Ordinal))
            {
                extensionInfo = ext;
                return true;
            }
        }

        return false;
    }

    private string EmitArgumentList(ArgumentListSyntax argumentList)
    {
        var arguments = argumentList.Arguments.Select(EmitArgument);
        return $"({string.Join(", ", arguments)})";
    }

    private string EmitArgument(ArgumentSyntax argument)
    {
        var prefix = argument.NameColon is null
            ? string.Empty
            : $"{argument.NameColon.Name.Identifier.ValueText}: ";
        var refKind = argument.RefKindKeyword.IsKind(SyntaxKind.None)
            ? string.Empty
            : $"{argument.RefKindKeyword.Text} ";
        return $"{prefix}{refKind}{Emit(argument.Expression)}";
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

    private string EmitLambda(string parameterList, CSharpSyntaxNode body)
    {
        return AppendValueInline($"{parameterList} => ", EmitLambdaBody(body));
    }

    private string EmitNestedExpression(ExpressionSyntax expression)
    {
        if (!BelongsToSemanticModel(expression))
        {
            return Emit(expression);
        }

        var expressionType = GetExpressionType(expression);
        if (!ShouldUseCollectionFallback(expression, expressionType))
        {
            return Emit(expression);
        }

        var rootTypeName =
            _replacementTypes.TryGetValue(expression.Span, out var replacementType)
                ? replacementType
            : expressionType is not null
            && expressionType is not IErrorTypeSymbol
            && !ContainsAnonymousType(expressionType)
                ? expressionType.ToFullyQualifiedTypeName()
            : _rootTypeName;
        var nestedEmitter = new ProjectionExpressionEmitter(
            _semanticModel,
            expression,
            rootTypeName,
            ShouldUseCollectionFallback(expression, expressionType),
            _replacementTypes,
            _captureEntries,
            _extensions
        );
        return nestedEmitter.Emit(expression);
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

        if (!TryBuildConditional(expression, value => value, expression, out var conditional))
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
        out string rewritten
    )
    {
        switch (expression)
        {
            case ConditionalAccessExpressionSyntax conditionalAccess:
            {
                var checks = new List<string>();
                var receiver = Emit(conditionalAccess.Expression);
                checks.Add(receiver);
                var expressionType = GetExpressionType(expression);
                var expressionTypeName = GetExpressionTypeName(
                    expression,
                    expressionType,
                    out var canCast
                );
                var rootExpressionType = ReferenceEquals(rootConditionalExpression, expression)
                    ? expressionType
                    : GetExpressionType(rootConditionalExpression);
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
                    ? CreateEmptyCollectionFallback(rootExpressionTypeName)
                    : "null";
                var access = applyTail(
                    BindConditionalReceiver(receiver, conditionalAccess.WhenNotNull, checks)
                );

                var castPrefix =
                    !useEmptyFallback
                    && canCast
                    && !ShouldOmitConditionalCast(expression, expressionType)
                        ? $"({expressionTypeName})"
                        : string.Empty;
                var formattedAccess = access;
                if (TryFormatFluentAccess(receiver, access, out var multilineAccess))
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
                        [
                            string.Join(" && ", checks.Select(check => $"{check} != null")),
                            IndentAllLines(AppendValueWithContinuation("? ", formattedAccess)),
                            IndentAllLines(AppendValueWithContinuation(": ", fallback)),
                        ]
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
                    out rewritten
                );
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberInvocation
                    && ContainsConditionalAccess(memberInvocation.Expression):
                return TryBuildConditional(
                    memberInvocation.Expression,
                    value =>
                        applyTail(
                            EmitInvocationFromReceiver(invocation, memberInvocation.Name, value)
                        ),
                    rootConditionalExpression,
                    out rewritten
                );
            case ElementAccessExpressionSyntax elementAccess
                when ContainsConditionalAccess(elementAccess.Expression):
                return TryBuildConditional(
                    elementAccess.Expression,
                    value =>
                        applyTail($"{value}{EmitBracketArguments(elementAccess.ArgumentList)}"),
                    rootConditionalExpression,
                    out rewritten
                );
            default:
                rewritten = string.Empty;
                return false;
        }
    }

    private string BindConditionalReceiver(
        string receiver,
        ExpressionSyntax whenNotNull,
        IList<string> checks
    )
    {
        switch (whenNotNull)
        {
            case MemberBindingExpressionSyntax memberBinding:
                return $"{receiver}.{EmitSimpleName(memberBinding.Name)}";
            case MemberAccessExpressionSyntax memberAccess:
                return $"{BindConditionalReceiver(receiver, memberAccess.Expression, checks)}.{EmitSimpleName(memberAccess.Name)}";
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberBindingExpressionSyntax memberBinding:
                return EmitInvocationFromReceiver(invocation, memberBinding.Name, receiver);
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                return EmitInvocationFromReceiver(
                    invocation,
                    memberAccess.Name,
                    BindConditionalReceiver(receiver, memberAccess.Expression, checks)
                );
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
        return expression
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any();
    }

    private bool TryEmitFluentChain(ExpressionSyntax expression, out string rewritten)
    {
        rewritten = string.Empty;
        if (
            ContainsReducedExtensionInvocation(expression)
            || !TryDecomposeFluentChain(
                expression,
                out var root,
                out var segments,
                out var hasInvocation
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

        var lines = new List<string> { Emit(root) };
        lines.AddRange(segments.Select(segment => IndentAllLines(segment)));
        rewritten = string.Join("\n", lines);
        return true;
    }

    private bool TryDecomposeFluentChain(
        ExpressionSyntax expression,
        out ExpressionSyntax root,
        out List<string> segments,
        out bool hasInvocation
    )
    {
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                TryDecomposeFluentChain(
                    memberAccess.Expression,
                    out root,
                    out segments,
                    out hasInvocation
                );
                segments.Add(GetFluentInvocationSegment(invocation, memberAccess));
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
                    out hasInvocation
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
                    out hasInvocation
                );
                segments.Add(EmitBracketArguments(elementAccess.ArgumentList));
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
        MemberAccessExpressionSyntax memberAccess
    )
    {
        if (memberAccess.Name.Identifier.ValueText == "SelectExpr")
        {
            var selector = invocation
                .ArgumentList.Arguments.Select(argument => argument.Expression)
                .OfType<LambdaExpressionSyntax>()
                .FirstOrDefault();
            return selector is null
                ? $".Select{EmitArgumentList(invocation.ArgumentList)}"
                : $".Select({Emit(selector)})";
        }

        return $".{EmitSimpleName(memberAccess.Name)}{EmitArgumentList(invocation.ArgumentList)}";
    }

    private string EmitInvocationFromReceiver(
        InvocationExpressionSyntax invocation,
        SimpleNameSyntax methodName,
        string receiver
    )
    {
        return methodName.Identifier.ValueText switch
        {
            "SelectExpr" => EmitProjectionInvocation(receiver, invocation, "Select"),
            "SelectManyExpr" => EmitProjectionInvocation(receiver, invocation, "SelectMany"),
            "GroupByExpr" => EmitGroupByExprInvocation(receiver, invocation),
            _ => TryEmitExtensionInvocation(invocation, methodName, receiver, out var rewritten)
                ? rewritten
                : $"{receiver}.{EmitSimpleName(methodName)}{EmitArgumentList(invocation.ArgumentList)}",
        };
    }

    private bool TryEmitExtensionInvocation(
        InvocationExpressionSyntax invocation,
        SimpleNameSyntax methodName,
        string receiver,
        out string rewritten
    )
    {
        rewritten = string.Empty;
        if (!BelongsToSemanticModel(invocation))
        {
            return false;
        }

        var symbolInfo = _semanticModel.GetSymbolInfo(invocation);
        var methodSymbol =
            symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        var reducedFrom = methodSymbol?.ReducedFrom;
        if (reducedFrom is null)
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments.Select(EmitArgument).ToList();
        arguments.Insert(0, receiver);
        rewritten = BuildInvocationExpression(
            $"{reducedFrom.ContainingType.ToFullyQualifiedTypeName()}.{EmitSimpleName(methodName)}",
            arguments
        );
        return true;
    }

    private bool TryEmitCaptureReplacement(ExpressionSyntax expression, out string rewritten)
    {
        rewritten = string.Empty;
        if (_captureEntries.Count == 0 || !BelongsToSemanticModel(expression))
        {
            return false;
        }

        var normalizedExpression = NormalizeExpressionText(expression);
        var currentRootSymbol = GetRootSymbol(expression);

        foreach (var captureEntry in _captureEntries)
        {
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

        if (
            normalizedTypeName.StartsWith(
                "global::System.Collections.Generic.List<",
                System.StringComparison.Ordinal
            )
        )
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

    private string ResolveCollectionTargetTypeName(CollectionExpressionSyntax expression)
    {
        var targetType = GetExpressionType(expression);
        if (
            targetType is not null
            && targetType is not IErrorTypeSymbol
            && !ContainsAnonymousType(targetType)
        )
        {
            return targetType.ToFullyQualifiedTypeName();
        }

        if (TryResolveCollectionTargetTypeNameFromContext(expression, out var contextualTypeName))
        {
            return contextualTypeName;
        }

        return _rootTypeName;
    }

    private bool TryResolveCollectionTargetTypeNameFromContext(SyntaxNode node, out string typeName)
    {
        switch (node.Parent)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return TryResolveCollectionTargetTypeNameFromContext(parenthesized, out typeName);
            case BinaryExpressionSyntax binaryExpression
                when binaryExpression.IsKind(SyntaxKind.CoalesceExpression):
            {
                var sibling = ReferenceEquals(binaryExpression.Left, node)
                    ? binaryExpression.Right
                    : binaryExpression.Left;
                return TryGetContextualCollectionTypeName(sibling, out typeName);
            }
            case ConditionalExpressionSyntax conditionalExpression:
            {
                var sibling = ReferenceEquals(conditionalExpression.WhenTrue, node)
                    ? conditionalExpression.WhenFalse
                    : conditionalExpression.WhenTrue;
                return TryGetContextualCollectionTypeName(sibling, out typeName);
            }
            case AssignmentExpressionSyntax assignment when ReferenceEquals(assignment.Right, node):
                return TryResolveAssignedTypeName(assignment.Left, out typeName);
            default:
                typeName = string.Empty;
                return false;
        }
    }

    private bool TryGetContextualCollectionTypeName(
        ExpressionSyntax expression,
        out string typeName
    )
    {
        var expressionType = GetExpressionType(expression);
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

    private bool TryResolveAssignedTypeName(ExpressionSyntax expression, out string typeName)
    {
        if (!BelongsToSemanticModel(expression))
        {
            typeName = string.Empty;
            return false;
        }

        var symbolType = _semanticModel.GetSymbolInfo(expression).Symbol switch
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
            _semanticModel.GetTypeInfo(expression).Type
            ?? _semanticModel.GetTypeInfo(expression).ConvertedType;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            typeName = expressionType.ToFullyQualifiedTypeName();
            return true;
        }

        typeName = string.Empty;
        return false;
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

    private static string NormalizeExpressionText(ExpressionSyntax expression)
    {
        return expression.WithoutTrivia().ToString();
    }

    private ISymbol? GetRootSymbol(ExpressionSyntax expression)
    {
        var rootExpression = GetRootExpression(expression);
        return _semanticModel.GetSymbolInfo(rootExpression).Symbol;
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

    private static bool TryFormatFluentAccess(string receiver, string access, out string formatted)
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

        var segments = SplitTopLevelSegments(remainder);
        if (segments.Count == 0 || !segments.Any(segment => segment.Contains('(')))
        {
            return false;
        }

        formatted = string.Join("\n", new[] { receiver }.Concat(segments));
        return true;
    }

    private static List<string> SplitTopLevelSegments(string value)
    {
        var segments = new List<string>();
        var start = 0;
        var parentheses = 0;
        var braces = 0;
        var brackets = 0;
        var angles = 0;

        for (var index = 0; index < value.Length; index++)
        {
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

        return string.Join("\n", [$"{castPrefix}(", IndentAllLines(expression), ")"]);
    }

    private string EmitCastExpression(CastExpressionSyntax expression)
    {
        var operand =
            expression.Expression is ParenthesizedExpressionSyntax parenthesized
            && CanOmitParenthesizedCastOperand(parenthesized.Expression)
                ? parenthesized.Expression
                : expression.Expression;

        return WrapCastExpression($"({QualifyType(expression.Type)})", Emit(operand));
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

    private static string BuildInitializerExpression(string header, IReadOnlyList<string> items)
    {
        if (!ShouldExpandInitializer(items))
        {
            return items.Count == 0
                ? $"{header} {{ }}"
                : $"{header} {{ {string.Join(", ", items)} }}";
        }

        var builder = new IndentedStringBuilder();
        builder.AppendLine($"{header} {{");
        using (builder.Indent())
        {
            foreach (var item in items)
            {
                AppendMultilineItem(builder, item, ",");
            }
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static string BuildInitializerBody(IReadOnlyList<string> items)
    {
        if (!ShouldExpandInitializer(items))
        {
            return items.Count == 0 ? "{ }" : $"{{ {string.Join(", ", items)} }}";
        }

        var builder = new IndentedStringBuilder();
        builder.AppendLine("{");
        using (builder.Indent())
        {
            foreach (var item in items)
            {
                AppendMultilineItem(builder, item, ",");
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
        string suffix
    )
    {
        var lines = SplitLines(value);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = index == lines.Length - 1 ? lines[index] + suffix : lines[index];
            builder.AppendLine(line);
        }
    }

    private static string BuildInvocationExpression(string header, IReadOnlyList<string> arguments)
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
        builder.AppendLine($"{header}(");
        using (builder.Indent())
        {
            for (var index = 0; index < arguments.Count; index++)
            {
                AppendMultilineItem(
                    builder,
                    arguments[index],
                    index == arguments.Count - 1 ? string.Empty : ","
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

    private bool ContainsReducedExtensionInvocation(ExpressionSyntax expression)
    {
        foreach (
            var invocation in expression
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
        )
        {
            if (!BelongsToSemanticModel(invocation))
            {
                continue;
            }

            var symbolInfo = _semanticModel.GetSymbolInfo(invocation);
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
