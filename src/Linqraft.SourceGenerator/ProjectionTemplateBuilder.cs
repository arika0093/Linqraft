using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Linqraft.Core.Collections;
using Linqraft.Core.Configuration;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.SourceGenerator;

internal static class ProjectionTemplateBuilder
{
    public static ProjectionSourceTemplateModel? AnalyzeProjectionInvocation(
        GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken
    )
    {
        return syntaxContext.Node is InvocationExpressionSyntax invocation
            ? AnalyzeQueryProjectionInvocation(
                invocation,
                syntaxContext.SemanticModel,
                cancellationToken,
                allowInterceptor: true,
                ownerHintName: null
            )
            : null;
    }

    public static ObjectGenerationSourceTemplateModel? AnalyzeObjectGenerationInvocation(
        GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken
    )
    {
        return syntaxContext.Node is InvocationExpressionSyntax invocation
            ? AnalyzeObjectGenerationInvocationCore(
                invocation,
                syntaxContext.SemanticModel,
                cancellationToken
            )
            : null;
    }

    public static MappingSourceTemplateModel? AnalyzeMappingClass(
        GeneratorAttributeSyntaxContext attributeContext,
        CancellationToken cancellationToken
    )
    {
        if (
            attributeContext.TargetNode is not ClassDeclarationSyntax declaration
            || attributeContext.TargetSymbol is not INamedTypeSymbol classSymbol
        )
        {
            return null;
        }

        if (!InheritsFromMappingDeclare(classSymbol))
        {
            return null;
        }

        var defineMapping = declaration
            .Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "DefineMapping");
        if (defineMapping is null)
        {
            return null;
        }

        var selectExpr = defineMapping
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(IsSelectExprInvocation);
        if (selectExpr is null)
        {
            return null;
        }

        var mappingHintName =
            $"Mapping_{HashingHelper.ComputeHash(classSymbol.ToDisplayString(), 16)}";
        var projection = AnalyzeQueryProjectionInvocation(
            selectExpr,
            attributeContext.SemanticModel,
            cancellationToken,
            allowInterceptor: false,
            ownerHintName: mappingHintName
        );
        if (projection is null || projection.Request.Captures.Length != 0)
        {
            return null;
        }

        var mappingAttribute = GetMappingGenerateAttribute(classSymbol.GetAttributes());
        var methodName = GetMappingMethodName(mappingAttribute);
        var accessibilityKeyword = GetMappingGeneratedAccessibilityKeyword(
            classSymbol.DeclaredAccessibility,
            selectExpr,
            attributeContext.SemanticModel,
            GetMappingVisibilityKeyword(mappingAttribute)
        );
        return new MappingSourceTemplateModel
        {
            Request = new MappingRequestTemplate
            {
                HintName = mappingHintName,
                Namespace = SymbolNameHelper.GetNamespace(classSymbol.ContainingNamespace),
                ContainingTypeName =
                    $"{classSymbol.Name}_{HashingHelper.ComputeHash(classSymbol.ToDisplayString(), 8)}",
                AccessibilityKeyword = accessibilityKeyword,
                MethodAccessibilityKeyword = accessibilityKeyword,
                MethodName = string.IsNullOrWhiteSpace(methodName)
                    ? $"ProjectTo{classSymbol.BaseType?.TypeArguments.FirstOrDefault()?.Name}"
                    : methodName!,
                ReceiverKind = projection.Request.ReceiverKind,
                SourceTypeName = projection.Request.SourceTypeName,
                ResultTypeTemplate = projection.Request.ResultTypeTemplate,
                SelectorParameterName = projection.Request.SelectorParameterName,
                CanUsePrebuiltExpressionWhenConfigured = projection
                    .Request
                    .CanUsePrebuiltExpressionWhenConfigured,
                Projection = projection.Request.Projection!,
            },
            GeneratedDtos = projection.GeneratedDtos,
        };
    }

    public static MappingSourceTemplateModel? AnalyzeMappingMethod(
        GeneratorAttributeSyntaxContext attributeContext,
        CancellationToken cancellationToken
    )
    {
        if (
            attributeContext.TargetNode is not MethodDeclarationSyntax declaration
            || attributeContext.TargetSymbol is not IMethodSymbol methodSymbol
            || methodSymbol.ContainingType is null
        )
        {
            return null;
        }

        if (
            !methodSymbol.ContainingType.IsStatic
            || !methodSymbol.ContainingType.DeclaringSyntaxReferences.Any()
        )
        {
            return null;
        }

        var selectExpr = declaration
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(IsSelectExprInvocation);
        if (selectExpr is null)
        {
            return null;
        }

        var mappingHintName =
            $"Mapping_{HashingHelper.ComputeHash(methodSymbol.ToDisplayString(), 16)}";
        var projection = AnalyzeQueryProjectionInvocation(
            selectExpr,
            attributeContext.SemanticModel,
            cancellationToken,
            allowInterceptor: false,
            ownerHintName: mappingHintName
        );
        if (projection is null || projection.Request.Captures.Length != 0)
        {
            return null;
        }

        var mappingAttribute = GetMappingGenerateAttribute(methodSymbol.GetAttributes());
        return new MappingSourceTemplateModel
        {
            Request = new MappingRequestTemplate
            {
                HintName = mappingHintName,
                Namespace = SymbolNameHelper.GetNamespace(
                    methodSymbol.ContainingType.ContainingNamespace
                ),
                ContainingTypeName = methodSymbol.ContainingType.Name,
                AccessibilityKeyword = SymbolNameHelper.GetAccessibilityKeyword(
                    methodSymbol.ContainingType.DeclaredAccessibility
                ),
                MethodAccessibilityKeyword = GetMappingGeneratedAccessibilityKeyword(
                    methodSymbol.DeclaredAccessibility,
                    selectExpr,
                    attributeContext.SemanticModel,
                    GetMappingVisibilityKeyword(mappingAttribute)
                ),
                MethodName =
                    GetMappingMethodName(mappingAttribute) ?? declaration.Identifier.ValueText,
                ReceiverKind = projection.Request.ReceiverKind,
                SourceTypeName = projection.Request.SourceTypeName,
                ResultTypeTemplate = projection.Request.ResultTypeTemplate,
                SelectorParameterName = projection.Request.SelectorParameterName,
                CanUsePrebuiltExpressionWhenConfigured = projection
                    .Request
                    .CanUsePrebuiltExpressionWhenConfigured,
                Projection = projection.Request.Projection!,
            },
            GeneratedDtos = projection.GeneratedDtos,
        };
    }

    private static ProjectionSourceTemplateModel? AnalyzeQueryProjectionInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool allowInterceptor,
        string? ownerHintName
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operationKind = GetProjectionOperationKind(invocation);
        if (operationKind is null)
        {
            return null;
        }

        if (allowInterceptor && IsInsideMappingDeclaration(invocation))
        {
            return null;
        }

        var receiver = GetReceiverExpression(invocation);
        if (receiver is null)
        {
            return null;
        }

        var receiverType =
            semanticModel.GetTypeInfo(receiver, cancellationToken).ConvertedType
            ?? semanticModel.GetTypeInfo(receiver, cancellationToken).Type;
        var receiverKind = ResolveReceiverKind(receiverType);
        if (receiverKind is null)
        {
            return null;
        }

        var lambdas = invocation
            .ArgumentList.Arguments.Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .ToArray();
        if (lambdas.Length == 0)
        {
            return null;
        }

        var nameSyntax = GetInvocationNameSyntax(invocation.Expression);
        var typeArguments = nameSyntax is GenericNameSyntax genericName
            ? genericName.TypeArgumentList.Arguments
            : default(SeparatedSyntaxList<TypeSyntax>);

        var sourceType = ResolveSourceType(semanticModel, receiverType, typeArguments);
        if (sourceType is null)
        {
            return null;
        }

        var captureEntries = AnalyzeCaptures(invocation, semanticModel, cancellationToken);
        var callerNamespace = ResolveCallerNamespace(invocation, semanticModel);
        var methodHash = HashingHelper.ComputeHash(
            $"{invocation.SyntaxTree.FilePath}|{invocation.SpanStart}|{invocation}",
            16
        );
        var effectiveOwnerHintName = ownerHintName ?? $"SelectExpr_{methodHash}";
        var buildContext = new ProjectionBuildContext(
            semanticModel,
            captureEntries,
            cancellationToken
        );
        var analyzedProjection = operationKind.Value switch
        {
            ProjectionOperationKind.Select => AnalyzeSelectProjection(
                invocation,
                semanticModel,
                cancellationToken,
                typeArguments,
                sourceType,
                callerNamespace,
                effectiveOwnerHintName,
                buildContext,
                lambdas[0]
            ),
            ProjectionOperationKind.SelectMany => AnalyzeSelectManyProjection(
                invocation,
                semanticModel,
                cancellationToken,
                typeArguments,
                sourceType,
                callerNamespace,
                effectiveOwnerHintName,
                buildContext,
                lambdas[0]
            ),
            ProjectionOperationKind.GroupBy when lambdas.Length >= 2 => AnalyzeGroupByProjection(
                invocation,
                semanticModel,
                cancellationToken,
                typeArguments,
                sourceType,
                callerNamespace,
                effectiveOwnerHintName,
                buildContext,
                lambdas[0],
                lambdas[1]
            ),
            _ => null,
        };
        if (analyzedProjection is null)
        {
            return null;
        }

        var interceptableLocation = allowInterceptor
            ? semanticModel.GetInterceptableLocation(invocation, cancellationToken)
            : null;
        return new ProjectionSourceTemplateModel
        {
            Request = new ProjectionRequestTemplate
            {
                HintName = $"{GetInvocationName(invocation.Expression)}_{methodHash}",
                MethodName = $"{GetInvocationName(invocation.Expression)}_{methodHash}",
                OperationKind = operationKind.Value,
                ReceiverKind = receiverKind.Value,
                Pattern = analyzedProjection.Pattern,
                SourceTypeName = sourceType.ToFullyQualifiedTypeName(),
                ResultTypeTemplate = analyzedProjection.ResultTypeTemplate,
                SelectorParameterName = analyzedProjection.SelectorParameterName,
                KeySelectorParameterName = analyzedProjection.KeySelectorParameterName,
                KeySelectorBodyTemplate = analyzedProjection.KeySelectorBodyTemplate,
                UseObjectSelectorSignature = analyzedProjection.UseObjectSelectorSignature,
                CanUsePrebuiltExpressionWhenConfigured =
                    operationKind == ProjectionOperationKind.Select
                    && analyzedProjection.ProjectionTemplate is not null
                    && receiverKind == ReceiverKind.IQueryable
                    && analyzedProjection.Pattern != ProjectionPattern.Anonymous
                    && captureEntries.Length == 0,
                InterceptableLocationVersion = interceptableLocation?.Version,
                InterceptableLocationData = interceptableLocation?.Data,
                Captures = captureEntries
                    .Select(capture => new CaptureParameterModel
                    {
                        PropertyName = capture.PropertyName,
                        LocalName = capture.LocalName,
                        TypeName = capture.TypeName,
                    })
                    .ToArray(),
                Projection = analyzedProjection.ProjectionTemplate,
                ProjectionBodyTemplate = analyzedProjection.ProjectionBodyTemplate,
            },
            GeneratedDtos = buildContext.GetGeneratedDtos(),
        };
    }

    private static AnalyzedProjection? AnalyzeSelectProjection(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SeparatedSyntaxList<TypeSyntax> typeArguments,
        ITypeSymbol sourceType,
        string callerNamespace,
        string ownerHintName,
        ProjectionBuildContext buildContext,
        LambdaExpressionSyntax selectorLambda
    )
    {
        var selectorBody = GetLambdaBodyExpression(selectorLambda);
        if (selectorBody is null)
        {
            return null;
        }

        var pattern = ResolveProjectionPattern(selectorBody, typeArguments.Count);
        if (pattern is null)
        {
            return null;
        }

        return CreateObjectProjection(
            invocation,
            semanticModel,
            cancellationToken,
            typeArguments,
            sourceType,
            callerNamespace,
            ownerHintName,
            buildContext,
            selectorLambda,
            selectorBody,
            pattern.Value
        );
    }

    private static AnalyzedProjection? AnalyzeGroupByProjection(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SeparatedSyntaxList<TypeSyntax> typeArguments,
        ITypeSymbol sourceType,
        string callerNamespace,
        string ownerHintName,
        ProjectionBuildContext buildContext,
        LambdaExpressionSyntax keySelectorLambda,
        LambdaExpressionSyntax selectorLambda
    )
    {
        var selectorBody = GetLambdaBodyExpression(selectorLambda);
        var keySelectorBody = GetLambdaBodyExpression(keySelectorLambda);
        if (selectorBody is null || keySelectorBody is null)
        {
            return null;
        }

        var pattern = ResolveProjectionPattern(selectorBody, typeArguments.Count);
        if (pattern is null)
        {
            return null;
        }

        var projection = CreateObjectProjection(
            invocation,
            semanticModel,
            cancellationToken,
            typeArguments,
            sourceType,
            callerNamespace,
            ownerHintName,
            buildContext,
            selectorLambda,
            selectorBody,
            pattern.Value
        );
        if (projection is null)
        {
            return null;
        }

        var keyType =
            semanticModel.GetTypeInfo(keySelectorBody, cancellationToken).ConvertedType
            ?? semanticModel.GetTypeInfo(keySelectorBody, cancellationToken).Type;
        var keyBodyTemplate = buildContext.CreateStandaloneBodyTemplate(
            keySelectorBody,
            keyType?.ToFullyQualifiedTypeName() ?? "object",
            replacementTypes: null
        );

        return projection with
        {
            KeySelectorParameterName = GetLambdaParameterName(keySelectorLambda),
            KeySelectorBodyTemplate = keyBodyTemplate,
        };
    }

    private static AnalyzedProjection? AnalyzeSelectManyProjection(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SeparatedSyntaxList<TypeSyntax> typeArguments,
        ITypeSymbol sourceType,
        string callerNamespace,
        string ownerHintName,
        ProjectionBuildContext buildContext,
        LambdaExpressionSyntax selectorLambda
    )
    {
        var selectorBody = GetLambdaBodyExpression(selectorLambda);
        if (selectorBody is null)
        {
            return null;
        }

        var replacementTypes = new Dictionary<TextSpan, string>();
        string resultTypeTemplate;
        var useObjectSelectorSignature = false;
        if (typeArguments.Count >= 2)
        {
            var explicitTypeSyntax = typeArguments[1];
            resultTypeTemplate =
                ResolveNamedType(explicitTypeSyntax, semanticModel, callerNamespace)
                ?? "TResult";
            if (
                TryFindProjectedAnonymousBody(selectorBody, out var projectedAnonymousBody)
                && projectedAnonymousBody is not null
            )
            {
                useObjectSelectorSignature = true;
                var rootDto = CreateRootDtoTemplate(
                    explicitTypeSyntax,
                    semanticModel,
                    invocation,
                    sourceType as INamedTypeSymbol,
                    ownerHintName,
                    cancellationToken
                );
                var buildResult = buildContext.BuildProjectionTemplate(
                    projectedAnonymousBody,
                    rootDto.PlaceholderToken,
                    rootDto,
                    new HashSet<string>(
                        rootDto
                            .Properties.Where(property => property.IsSuppressed)
                            .Select(property => property.Name),
                        StringComparer.Ordinal
                    ),
                    namedContext: true,
                    defaultNamespace: rootDto.PreferredNamespace,
                    useGlobalNamespaceFallback: rootDto.UseGlobalNamespaceFallback,
                    ownerHintName: ownerHintName
                );
                replacementTypes[projectedAnonymousBody.Span] = rootDto.PlaceholderToken;
                foreach (var pair in buildResult.ReplacementTypes)
                {
                    replacementTypes[pair.Key] = pair.Value;
                }

                buildContext.RegisterDto(buildResult.DtoTemplate!);
                resultTypeTemplate = rootDto.PlaceholderToken;
            }
        }
        else
        {
            resultTypeTemplate = "TResult";
        }

        var collectionTypeTemplate = buildContext.AnalyzeStandaloneType(
            selectorBody,
            memberName: "Item",
            replacementTypes,
            namedContext: typeArguments.Count >= 2,
            defaultNamespace: callerNamespace,
            useGlobalNamespaceFallback: string.IsNullOrWhiteSpace(callerNamespace),
            ownerHintName: ownerHintName
        );
        var bodyTemplate = buildContext.CreateStandaloneBodyTemplate(
            selectorBody,
            collectionTypeTemplate,
            replacementTypes
        );

        return new AnalyzedProjection
        {
            Pattern =
                typeArguments.Count >= 2 ? ProjectionPattern.ExplicitDto : ProjectionPattern.Anonymous,
            ResultTypeTemplate = resultTypeTemplate,
            SelectorParameterName = GetLambdaParameterName(selectorLambda),
            KeySelectorParameterName = null,
            KeySelectorBodyTemplate = null,
            UseObjectSelectorSignature = useObjectSelectorSignature,
            ProjectionTemplate = null,
            ProjectionBodyTemplate = bodyTemplate,
        };
    }

    private static AnalyzedProjection? CreateObjectProjection(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SeparatedSyntaxList<TypeSyntax> typeArguments,
        ITypeSymbol sourceType,
        string callerNamespace,
        string ownerHintName,
        ProjectionBuildContext buildContext,
        LambdaExpressionSyntax selectorLambda,
        ExpressionSyntax selectorBody,
        ProjectionPattern pattern
    )
    {
        string resultTypeTemplate;
        ProjectionTemplateModel projectionTemplate;
        switch (pattern)
        {
            case ProjectionPattern.Anonymous:
            {
                var buildResult = buildContext.BuildProjectionTemplate(
                    selectorBody,
                    replacementTypeToken: null,
                    dtoTemplate: null,
                    existingPropertyNames: null,
                    namedContext: false,
                    defaultNamespace: callerNamespace,
                    useGlobalNamespaceFallback: false,
                    ownerHintName: ownerHintName
                );
                projectionTemplate = buildResult.Projection;
                resultTypeTemplate = "TResult";
                break;
            }
            case ProjectionPattern.ExplicitDto:
            {
                var resultTypeSyntax =
                    typeArguments.Count >= 2 ? typeArguments[^1] : typeArguments[0];
                var rootDto = CreateRootDtoTemplate(
                    resultTypeSyntax,
                    semanticModel,
                    invocation,
                    sourceType as INamedTypeSymbol,
                    ownerHintName,
                    cancellationToken
                );
                var existingProperties = new HashSet<string>(
                    rootDto
                        .Properties.Where(property => property.IsSuppressed)
                        .Select(property => property.Name),
                    StringComparer.Ordinal
                );
                var buildResult = buildContext.BuildProjectionTemplate(
                    selectorBody,
                    rootDto.PlaceholderToken,
                    rootDto,
                    existingProperties,
                    namedContext: true,
                    defaultNamespace: rootDto.PreferredNamespace,
                    useGlobalNamespaceFallback: rootDto.UseGlobalNamespaceFallback,
                    ownerHintName: ownerHintName
                );
                projectionTemplate = buildResult.Projection;
                buildContext.RegisterDto(buildResult.DtoTemplate!);
                resultTypeTemplate = rootDto.PlaceholderToken;
                break;
            }
            case ProjectionPattern.PredefinedDto:
            {
                var predefinedTypeSyntax = ((ObjectCreationExpressionSyntax)selectorBody).Type;
                resultTypeTemplate =
                    ResolveNamedType(predefinedTypeSyntax, semanticModel, callerNamespace)
                    ?? predefinedTypeSyntax.ToString();
                var buildResult = buildContext.BuildProjectionTemplate(
                    selectorBody,
                    replacementTypeToken: null,
                    dtoTemplate: null,
                    existingPropertyNames: null,
                    namedContext: true,
                    defaultNamespace: callerNamespace,
                    useGlobalNamespaceFallback: string.IsNullOrWhiteSpace(callerNamespace),
                    ownerHintName: ownerHintName
                );
                projectionTemplate = buildResult.Projection;
                break;
            }
            default:
                return null;
        }

        return new AnalyzedProjection
        {
            Pattern = pattern,
            ResultTypeTemplate = resultTypeTemplate,
            SelectorParameterName = GetLambdaParameterName(selectorLambda),
            KeySelectorParameterName = null,
            KeySelectorBodyTemplate = null,
            UseObjectSelectorSignature = pattern == ProjectionPattern.ExplicitDto,
            ProjectionTemplate = projectionTemplate,
            ProjectionBodyTemplate = null,
        };
    }

    private static bool TryFindProjectedAnonymousBody(
        ExpressionSyntax expression,
        out AnonymousObjectCreationExpressionSyntax? anonymousBody
    )
    {
        anonymousBody = null;
        if (!TryFindProjectionInvocation(expression, out var invocation))
        {
            return false;
        }

        var lambda = invocation.ArgumentList.Arguments.Select(argument => argument.Expression).OfType<LambdaExpressionSyntax>().FirstOrDefault();
        if (lambda is null)
        {
            return false;
        }

        var body = GetLambdaBodyExpression(lambda);
        if (body is AnonymousObjectCreationExpressionSyntax directAnonymous)
        {
            anonymousBody = directAnonymous;
            return true;
        }

        return body is not null && TryFindProjectedAnonymousBody(body, out anonymousBody);
    }

    private static ObjectGenerationSourceTemplateModel? AnalyzeObjectGenerationInvocationCore(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsGenerateInvocation(invocation, semanticModel, cancellationToken))
        {
            return null;
        }

        var nameSyntax = GetInvocationNameSyntax(invocation.Expression) as GenericNameSyntax;
        if (nameSyntax is null || nameSyntax.TypeArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (argument is not AnonymousObjectCreationExpressionSyntax anonymousObject)
        {
            return null;
        }

        var methodHash = HashingHelper.ComputeHash(
            $"{invocation.SyntaxTree.FilePath}|{invocation.SpanStart}|{argument}",
            16
        );
        var ownerHintName = $"Generate_{methodHash}";
        var rootDto = CreateRootDtoTemplate(
            nameSyntax.TypeArgumentList.Arguments[0],
            semanticModel,
            invocation,
            sourceType: null,
            ownerHintName,
            cancellationToken
        );
        var buildContext = new ProjectionBuildContext(
            semanticModel,
            Array.Empty<ProjectionExpressionEmitter.CaptureEntry>(),
            cancellationToken
        );
        var existingProperties = new HashSet<string>(
            rootDto
                .Properties.Where(property => property.IsSuppressed)
                .Select(property => property.Name),
            StringComparer.Ordinal
        );
        var buildResult = buildContext.BuildProjectionTemplate(
            anonymousObject,
            rootDto.PlaceholderToken,
            rootDto,
            existingProperties,
            namedContext: true,
            defaultNamespace: rootDto.PreferredNamespace,
            useGlobalNamespaceFallback: rootDto.UseGlobalNamespaceFallback,
            ownerHintName: ownerHintName
        );
        buildContext.RegisterDto(buildResult.DtoTemplate!);
        var interceptableLocation = semanticModel.GetInterceptableLocation(invocation, cancellationToken);

        return new ObjectGenerationSourceTemplateModel
        {
            Request = new ObjectGenerationRequestTemplate
            {
                HintName = ownerHintName,
                MethodName = ownerHintName,
                ResultTypeTemplate = rootDto.PlaceholderToken,
                Projection = buildResult.Projection,
                InterceptableLocationVersion = interceptableLocation?.Version,
                InterceptableLocationData = interceptableLocation?.Data,
            },
            GeneratedDtos = buildContext.GetGeneratedDtos(),
        };
    }

    private sealed record AnalyzedProjection
    {
        public required ProjectionPattern Pattern { get; init; }

        public required string ResultTypeTemplate { get; init; }

        public required string SelectorParameterName { get; init; }

        public required string? KeySelectorParameterName { get; init; }

        public required string? KeySelectorBodyTemplate { get; init; }

        public required bool UseObjectSelectorSignature { get; init; }

        public ProjectionTemplateModel? ProjectionTemplate { get; init; }

        public string? ProjectionBodyTemplate { get; init; }
    }

    private sealed class ProjectionBuildContext
    {
        private readonly SemanticModel _semanticModel;
        private readonly IReadOnlyList<ProjectionExpressionEmitter.CaptureEntry> _captureEntries;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, GeneratedDtoTemplateModel> _generatedDtos = new(
            StringComparer.Ordinal
        );

        public ProjectionBuildContext(
            SemanticModel semanticModel,
            IReadOnlyList<ProjectionExpressionEmitter.CaptureEntry> captureEntries,
            CancellationToken cancellationToken
        )
        {
            _semanticModel = semanticModel;
            _captureEntries = captureEntries;
            _cancellationToken = cancellationToken;
        }

        public ProjectionBuildResult BuildProjectionTemplate(
            ExpressionSyntax expression,
            string? replacementTypeToken,
            GeneratedDtoTemplateModel? dtoTemplate,
            HashSet<string>? existingPropertyNames,
            bool namedContext,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName
        )
        {
            var members = GetProjectionMembers(expression).ToList();
            var replacementTypes = new Dictionary<TextSpan, string>();
            if (
                replacementTypeToken is not null
                && expression is AnonymousObjectCreationExpressionSyntax anonymousRoot
            )
            {
                replacementTypes[anonymousRoot.Span] = replacementTypeToken;
            }

            var projectedMembers = new List<ProjectionMemberTemplateModel>(members.Count);
            foreach (var member in members)
            {
                var memberReplacements = new Dictionary<TextSpan, string>();
                var typeTemplate = AnalyzeMemberType(
                    member.Expression,
                    member.Name,
                    memberReplacements,
                    namedContext,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName
                );
                var documentation = DocumentationExtractor.GetExpressionDocumentation(
                    member.Expression,
                    _semanticModel,
                    LinqraftCommentOutput.All,
                    _cancellationToken
                );
                var canUseCollectionFallback =
                    dtoTemplate is not null
                    && QualifiesForCollectionNullabilityRemoval(member.Expression);
                var fallbackTypeTemplate = canUseCollectionFallback
                    ? RemoveNullableAnnotation(typeTemplate)
                    : typeTemplate;

                foreach (var pair in memberReplacements)
                {
                    replacementTypes[pair.Key] = pair.Value;
                }

                var valueTemplate = CreateValueTemplate(
                    member.Expression,
                    typeTemplate,
                    useEmptyCollectionFallback: false,
                    replacementTypes
                );
                var fallbackValueTemplate = canUseCollectionFallback
                    ? CreateValueTemplate(
                        member.Expression,
                        fallbackTypeTemplate,
                        useEmptyCollectionFallback: true,
                        replacementTypes
                    )
                    : valueTemplate;

                projectedMembers.Add(
                    new ProjectionMemberTemplateModel
                    {
                        Name = member.Name,
                        TypeTemplate = typeTemplate,
                        FallbackTypeTemplate = fallbackTypeTemplate,
                        Documentation = documentation,
                        IsSuppressed = existingPropertyNames?.Contains(member.Name) == true,
                        ValueTemplate = valueTemplate,
                        FallbackValueTemplate = fallbackValueTemplate,
                    }
                );
            }

            var projection = new ProjectionTemplateModel { Members = projectedMembers.ToArray() };
            if (dtoTemplate is null)
            {
                return new ProjectionBuildResult
                {
                    Projection = projection,
                    DtoTemplate = null,
                    ReplacementTypes = replacementTypes,
                };
            }

            var properties = projectedMembers
                .Select(member => new GeneratedPropertyTemplateModel
                {
                    Name = member.Name,
                    TypeTemplate = member.TypeTemplate,
                    FallbackTypeTemplate = member.FallbackTypeTemplate,
                    Documentation = member.Documentation,
                    IsSuppressed = member.IsSuppressed,
                })
                .ToArray();

            return new ProjectionBuildResult
            {
                Projection = projection,
                DtoTemplate = dtoTemplate with
                {
                    Properties = properties,
                    ShapeSignature =
                        $"{dtoTemplate.TemplateId}|{string.Join(";", properties.Select(property => $"{property.Name}:{property.TypeTemplate}:{property.FallbackTypeTemplate}:{property.IsSuppressed}"))}",
                },
                ReplacementTypes = replacementTypes,
            };
        }

        public string AnalyzeStandaloneType(
            ExpressionSyntax expression,
            string memberName,
            Dictionary<TextSpan, string> replacementTypes,
            bool namedContext,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName
        )
        {
            return AnalyzeMemberType(
                expression,
                memberName,
                replacementTypes,
                namedContext,
                defaultNamespace,
                useGlobalNamespaceFallback,
                ownerHintName
            );
        }

        public string CreateStandaloneBodyTemplate(
            ExpressionSyntax expression,
            string rootTypeName,
            IReadOnlyDictionary<TextSpan, string>? replacementTypes
        )
        {
            return CreateValueTemplate(
                expression,
                rootTypeName,
                useEmptyCollectionFallback: false,
                replacementTypes ?? new Dictionary<TextSpan, string>()
            );
        }

        public void RegisterDto(GeneratedDtoTemplateModel dtoTemplate)
        {
            if (_generatedDtos.TryGetValue(dtoTemplate.TemplateId, out var existing))
            {
                _generatedDtos[dtoTemplate.TemplateId] = MergeDtoTemplate(existing, dtoTemplate);
                return;
            }

            _generatedDtos.Add(dtoTemplate.TemplateId, dtoTemplate);
        }

        public EquatableArray<GeneratedDtoTemplateModel> GetGeneratedDtos()
        {
            return _generatedDtos
                .Values.OrderByDescending(dto => dto.IsRoot)
                .ThenBy(dto => dto.TemplateId, StringComparer.Ordinal)
                .ToArray();
        }

        private string AnalyzeMemberType(
            ExpressionSyntax expression,
            string memberName,
            Dictionary<TextSpan, string> replacementTypes,
            bool namedContext,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName
        )
        {
            if (replacementTypes.TryGetValue(expression.Span, out var existingReplacement))
            {
                return existingReplacement;
            }

            if (
                expression is ConditionalExpressionSyntax conditionalExpression
                && TryGetNullGuardBranch(conditionalExpression, out var nonNullBranch)
            )
            {
                return MakeNullable(
                    AnalyzeMemberType(
                        nonNullBranch,
                        memberName,
                        replacementTypes,
                        namedContext,
                        defaultNamespace,
                        useGlobalNamespaceFallback,
                        ownerHintName
                    )
                );
            }

            if (expression is AnonymousObjectCreationExpressionSyntax anonymousObject)
            {
                if (!namedContext)
                {
                    var inferred = _semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
                    return inferred?.ToFullyQualifiedTypeName() ?? "object";
                }

                var nestedDto = CreateNestedDtoTemplate(
                    memberName,
                    anonymousObject,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName
                );
                replacementTypes[anonymousObject.Span] = nestedDto.PlaceholderToken;
                var nestedBuildResult = BuildProjectionTemplate(
                    anonymousObject,
                    nestedDto.PlaceholderToken,
                    nestedDto,
                    existingPropertyNames: null,
                    namedContext: true,
                    defaultNamespace: nestedDto.PreferredNamespace,
                    useGlobalNamespaceFallback: nestedDto.UseGlobalNamespaceFallback,
                    ownerHintName: ownerHintName
                );
                MergeReplacementTypes(replacementTypes, nestedBuildResult.ReplacementTypes);
                RegisterDto(nestedBuildResult.DtoTemplate!);
                return nestedDto.PlaceholderToken;
            }

            if (expression is ObjectCreationExpressionSyntax namedObject)
            {
                if (namedObject.Initializer is not null)
                {
                    foreach (var nestedMember in GetProjectionMembers(namedObject))
                    {
                        AnalyzeMemberType(
                            nestedMember.Expression,
                            nestedMember.Name,
                            replacementTypes,
                            namedContext: true,
                            defaultNamespace,
                            useGlobalNamespaceFallback,
                            ownerHintName
                        );
                    }
                }

                var typeSymbol = _semanticModel
                    .GetTypeInfo(namedObject.Type, _cancellationToken)
                    .Type;
                return ResolveNamedType(namedObject.Type, _semanticModel, defaultNamespace)
                    ?? typeSymbol?.ToFullyQualifiedTypeName()
                    ?? namedObject.Type.ToString();
            }

            if (TryGetCollectionProjection(expression, out var collectionProjection))
            {
                string projectedTypeTemplate;
                if (collectionProjection.IsNestedExplicitDto)
                {
                    var nestedResultType = ResolveNamedType(
                        collectionProjection.NestedResultTypeSyntax!,
                        _semanticModel,
                        defaultNamespace
                    );
                    if (nestedResultType is not null)
                    {
                        if (
                            collectionProjection.LambdaBody
                            is AnonymousObjectCreationExpressionSyntax nestedAnonymous
                        )
                        {
                            var nestedDto = CreateRootDtoTemplate(
                                collectionProjection.NestedResultTypeSyntax!,
                                _semanticModel,
                                collectionProjection.Invocation,
                                _semanticModel
                                    .GetTypeInfo(
                                        collectionProjection.SourceTypeSyntax!,
                                        _cancellationToken
                                    )
                                    .Type as INamedTypeSymbol,
                                ownerHintName,
                                _cancellationToken
                            );
                            replacementTypes[nestedAnonymous.Span] = nestedDto.PlaceholderToken;
                            var nestedBuildResult = BuildProjectionTemplate(
                                nestedAnonymous,
                                nestedDto.PlaceholderToken,
                                nestedDto,
                                new HashSet<string>(StringComparer.Ordinal),
                                namedContext: true,
                                defaultNamespace: nestedDto.PreferredNamespace,
                                useGlobalNamespaceFallback: nestedDto.UseGlobalNamespaceFallback,
                                ownerHintName: ownerHintName
                            );
                            MergeReplacementTypes(
                                replacementTypes,
                                nestedBuildResult.ReplacementTypes
                            );
                            RegisterDto(nestedBuildResult.DtoTemplate!);
                            projectedTypeTemplate = nestedDto.PlaceholderToken;
                            return BuildProjectedResultType(
                                expression,
                                collectionProjection.ExpressionType,
                                collectionProjection.LambdaBody,
                                projectedTypeTemplate,
                                collectionProjection.ProjectionMethodName
                            );
                        }

                        projectedTypeTemplate = nestedResultType;
                        return BuildProjectedResultType(
                            expression,
                            collectionProjection.ExpressionType,
                            collectionProjection.LambdaBody,
                            projectedTypeTemplate,
                            collectionProjection.ProjectionMethodName
                        );
                    }
                }

                if (
                    collectionProjection.LambdaBody
                        is AnonymousObjectCreationExpressionSyntax anonymousBody
                    && namedContext
                )
                {
                    if (replacementTypes.TryGetValue(anonymousBody.Span, out var existingProjectedType))
                    {
                        projectedTypeTemplate = existingProjectedType;
                        return BuildProjectedResultType(
                            expression,
                            collectionProjection.ExpressionType,
                            collectionProjection.LambdaBody,
                            projectedTypeTemplate,
                            collectionProjection.ProjectionMethodName
                        );
                    }

                    var nestedDto = CreateNestedDtoTemplate(
                        memberName,
                        anonymousBody,
                        defaultNamespace,
                        useGlobalNamespaceFallback,
                        ownerHintName
                    );
                    replacementTypes[anonymousBody.Span] = nestedDto.PlaceholderToken;
                    var nestedBuildResult = BuildProjectionTemplate(
                        anonymousBody,
                        nestedDto.PlaceholderToken,
                        nestedDto,
                        existingPropertyNames: null,
                        namedContext: true,
                        defaultNamespace: nestedDto.PreferredNamespace,
                        useGlobalNamespaceFallback: nestedDto.UseGlobalNamespaceFallback,
                        ownerHintName: ownerHintName
                    );
                    MergeReplacementTypes(replacementTypes, nestedBuildResult.ReplacementTypes);
                    RegisterDto(nestedBuildResult.DtoTemplate!);
                    projectedTypeTemplate = nestedDto.PlaceholderToken;
                    return BuildProjectedResultType(
                        expression,
                        collectionProjection.ExpressionType,
                        collectionProjection.LambdaBody,
                        projectedTypeTemplate,
                        collectionProjection.ProjectionMethodName
                    );
                }

                if (collectionProjection.LambdaBody is ObjectCreationExpressionSyntax namedBody)
                {
                    foreach (var nestedMember in GetProjectionMembers(namedBody))
                    {
                        AnalyzeMemberType(
                            nestedMember.Expression,
                            nestedMember.Name,
                            replacementTypes,
                            namedContext: true,
                            defaultNamespace,
                            useGlobalNamespaceFallback,
                            ownerHintName
                        );
                    }

                    var elementType = _semanticModel
                        .GetTypeInfo(namedBody.Type, _cancellationToken)
                        .Type;
                    projectedTypeTemplate =
                        ResolveNamedType(namedBody.Type, _semanticModel, defaultNamespace)
                        ?? elementType?.ToFullyQualifiedTypeName()
                        ?? namedBody.Type.ToString();
                    return BuildProjectedResultType(
                        expression,
                        collectionProjection.ExpressionType,
                        collectionProjection.LambdaBody,
                        projectedTypeTemplate,
                        collectionProjection.ProjectionMethodName
                    );
                }

                projectedTypeTemplate = AnalyzeMemberType(
                    collectionProjection.LambdaBody,
                    memberName,
                    replacementTypes,
                    namedContext,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName
                );
                return BuildProjectedResultType(
                    expression,
                    collectionProjection.ExpressionType,
                    collectionProjection.LambdaBody,
                    projectedTypeTemplate,
                    collectionProjection.ProjectionMethodName
                );
            }

            var type =
                _semanticModel.GetTypeInfo(expression, _cancellationToken).ConvertedType
                ?? _semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
            return type?.ToFullyQualifiedTypeName() ?? "object";
        }

        private string CreateValueTemplate(
            ExpressionSyntax expression,
            string rootTypeName,
            bool useEmptyCollectionFallback,
            IReadOnlyDictionary<TextSpan, string> replacementTypes
        )
        {
            var emitter = new ProjectionExpressionEmitter(
                _semanticModel,
                expression,
                rootTypeName,
                useEmptyCollectionFallback,
                replacementTypes,
                _captureEntries
            );
            return emitter.Emit(expression);
        }

        private static void MergeReplacementTypes(
            IDictionary<TextSpan, string> target,
            IReadOnlyDictionary<TextSpan, string> source
        )
        {
            foreach (var replacement in source)
            {
                target[replacement.Key] = replacement.Value;
            }
        }

        private GeneratedDtoTemplateModel CreateNestedDtoTemplate(
            string memberName,
            AnonymousObjectCreationExpressionSyntax syntax,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName
        )
        {
            var dtoBaseName = memberName.EndsWith("Dto", StringComparison.Ordinal)
                ? memberName
                : $"{memberName}Dto";
            var signature =
                $"{defaultNamespace}|{dtoBaseName}|{CreateAnonymousObjectSignature(syntax)}";
            var hash = HashingHelper.ComputeHash(signature, 8);
            var templateId = $"nested:{defaultNamespace}|{dtoBaseName}|{hash}";
            return new GeneratedDtoTemplateModel
            {
                TemplateId = templateId,
                PlaceholderToken = CreateDtoPlaceholderToken(templateId),
                Kind = GeneratedDtoTemplateKind.NestedAuto,
                PreferredNamespace = defaultNamespace,
                UseGlobalNamespaceFallback = useGlobalNamespaceFallback,
                Name = dtoBaseName,
                ShapeHash = hash,
                AccessibilityKeyword = "public",
                DeclaredIsRecord = null,
                IsRoot = false,
                IsAutoGeneratedNested = true,
                Documentation = null,
                OwnerHintName = ownerHintName,
                ShapeSignature = signature,
                ContainingTypes = Array.Empty<ContainingTypeInfo>(),
                Properties = Array.Empty<GeneratedPropertyTemplateModel>(),
            };
        }

        private string CreateAnonymousObjectSignature(
            AnonymousObjectCreationExpressionSyntax syntax
        )
        {
            var typeInfo = _semanticModel.GetTypeInfo(syntax, _cancellationToken);
            var rootType = typeInfo.ConvertedType ?? typeInfo.Type;
            var rootTypeName =
                rootType is null || rootType is IErrorTypeSymbol || rootType.IsAnonymousType
                    ? "object"
                    : rootType.ToFullyQualifiedTypeName();
            var emitter = new ProjectionExpressionEmitter(
                _semanticModel,
                syntax,
                rootTypeName,
                useEmptyCollectionFallback: false
            );
            return emitter.Emit(syntax);
        }

        private bool TryGetCollectionProjection(
            ExpressionSyntax expression,
            out CollectionProjectionInfo projectionInfo
        )
        {
            if (!TryFindProjectionInvocation(expression, out var invocation))
            {
                projectionInfo = default;
                return false;
            }

            var lambda = invocation
                .ArgumentList.Arguments.Select(argument => argument.Expression)
                .OfType<LambdaExpressionSyntax>()
                .FirstOrDefault();
            if (lambda is null)
            {
                projectionInfo = default;
                return false;
            }

            var body = GetLambdaBodyExpression(lambda);
            if (body is null)
            {
                projectionInfo = default;
                return false;
            }

            var nameSyntax = GetInvocationNameSyntax(invocation.Expression);
            var genericArgs = nameSyntax is GenericNameSyntax genericName
                ? genericName.TypeArgumentList.Arguments
                : default(SeparatedSyntaxList<TypeSyntax>);

            projectionInfo = new CollectionProjectionInfo
            {
                Invocation = invocation,
                LambdaBody = body,
                ExpressionType =
                    _semanticModel.GetTypeInfo(expression, _cancellationToken).Type
                    ?? _semanticModel.GetTypeInfo(expression, _cancellationToken).ConvertedType,
                ProjectionMethodName = GetInvocationName(invocation.Expression),
                IsNestedExplicitDto =
                    IsSelectExprInvocation(invocation)
                    && genericArgs.Count >= 2
                    && body is AnonymousObjectCreationExpressionSyntax,
                NestedResultTypeSyntax = genericArgs.Count >= 2 ? genericArgs[1] : null,
                SourceTypeSyntax = genericArgs.Count >= 1 ? genericArgs[0] : null,
            };
            return true;
        }

        private bool QualifiesForCollectionNullabilityRemoval(ExpressionSyntax expression)
        {
            if (expression.DescendantNodesAndSelf().OfType<ConditionalExpressionSyntax>().Any())
            {
                return false;
            }

            if (
                !expression
                    .DescendantNodesAndSelf()
                    .OfType<ConditionalAccessExpressionSyntax>()
                    .Any()
            )
            {
                return false;
            }

            if (
                !expression
                    .DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(IsProjectionInvocation)
            )
            {
                return false;
            }

            var type = _semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
            type ??= _semanticModel.GetTypeInfo(expression, _cancellationToken).ConvertedType;
            return type is not null
                && type.SpecialType != SpecialType.System_String
                && SymbolNameHelper.IsEnumerable(type);
        }

        private string BuildProjectedResultType(
            ExpressionSyntax expression,
            ITypeSymbol? expressionType,
            ExpressionSyntax lambdaBody,
            string projectedTypeTemplate,
            string projectionMethodName
        )
        {
            var effectiveTypeName =
                projectionMethodName == "SelectMany"
                    ? UnwrapProjectedTypeName(
                        projectedTypeTemplate,
                        _semanticModel.GetTypeInfo(lambdaBody, _cancellationToken).Type
                            ?? _semanticModel
                                .GetTypeInfo(lambdaBody, _cancellationToken)
                                .ConvertedType
                    )
                    : projectedTypeTemplate;

            if (
                IsCollectionLikeResultExpression(expression) || IsCollectionLikeType(expressionType)
            )
            {
                return BuildCollectionTypeName(expressionType, effectiveTypeName);
            }

            return ApplyExpressionNullability(effectiveTypeName, expressionType);
        }
    }

    private static GeneratedDtoTemplateModel CreateRootDtoTemplate(
        TypeSyntax resultTypeSyntax,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? sourceType,
        string ownerHintName,
        CancellationToken cancellationToken
    )
    {
        var resultType =
            semanticModel.GetTypeInfo(resultTypeSyntax, cancellationToken).Type as INamedTypeSymbol;
        if (resultType is IErrorTypeSymbol)
        {
            resultType = null;
        }

        var namespaceName =
            resultType is not null && !resultType.ContainingNamespace.IsGlobalNamespace
                ? SymbolNameHelper.GetNamespace(resultType.ContainingNamespace)
                : ResolveCallerNamespace(invocation, semanticModel);
        var dtoName = resultType?.Name ?? GetUnqualifiedTypeName(resultTypeSyntax);
        var containingTypes = GetContainingTypes(resultType);
        var containingTypeKey =
            containingTypes.Length == 0
                ? string.Empty
                : string.Join(".", containingTypes.Select(type => type.Name));
        var templateId = $"root:{namespaceName}|{containingTypeKey}|{dtoName}";
        var existingPropertyNames =
            resultType
                ?.GetMembers()
                .OfType<IPropertySymbol>()
                .Select(property => property.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList()
            ?? new List<string>();

        return new GeneratedDtoTemplateModel
        {
            TemplateId = templateId,
            PlaceholderToken = CreateDtoPlaceholderToken(templateId),
            Kind = GeneratedDtoTemplateKind.RootExplicit,
            PreferredNamespace = namespaceName,
            UseGlobalNamespaceFallback = string.IsNullOrWhiteSpace(namespaceName),
            Name = dtoName,
            ShapeHash = string.Empty,
            AccessibilityKeyword = resultType is null
                ? "public"
                : SymbolNameHelper.GetAccessibilityKeyword(resultType.DeclaredAccessibility),
            DeclaredIsRecord = resultType is null ? null : resultType.IsRecord,
            IsRoot = true,
            IsAutoGeneratedNested = false,
            Documentation = DocumentationExtractor.GetTypeDocumentation(
                sourceType,
                LinqraftCommentOutput.All
            ),
            OwnerHintName = ownerHintName,
            ShapeSignature = templateId,
            ContainingTypes = containingTypes,
            Properties = existingPropertyNames
                .Select(name => new GeneratedPropertyTemplateModel
                {
                    Name = name,
                    TypeTemplate = string.Empty,
                    FallbackTypeTemplate = string.Empty,
                    Documentation = null,
                    IsSuppressed = true,
                })
                .ToArray(),
        };
    }

    private static ProjectionExpressionEmitter.CaptureEntry[] AnalyzeCaptures(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var captureArgument = invocation.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameColon?.Name.Identifier.ValueText == "capture"
            || (
                !argument.Equals(invocation.ArgumentList.Arguments.First())
                && argument.Expression is AnonymousObjectCreationExpressionSyntax
            )
        );
        if (
            captureArgument?.Expression
            is not AnonymousObjectCreationExpressionSyntax anonymousCapture
        )
        {
            return Array.Empty<ProjectionExpressionEmitter.CaptureEntry>();
        }

        return anonymousCapture
            .Initializers.Select(
                (initializer, index) =>
                {
                    var propertyName = GetAnonymousMemberName(initializer);
                    var type = semanticModel
                        .GetTypeInfo(initializer.Expression, cancellationToken)
                        .Type;
                    return new ProjectionExpressionEmitter.CaptureEntry
                    {
                        PropertyName = propertyName,
                        LocalName = CreateCaptureLocalName(propertyName, index),
                        TypeName = type?.ToFullyQualifiedTypeName() ?? "object",
                        ExpressionText = NormalizeExpressionText(initializer.Expression),
                        RootSymbol = GetCaptureRootSymbol(
                            initializer.Expression,
                            semanticModel,
                            cancellationToken
                        ),
                    };
                }
            )
            .ToArray();
    }

    private static GeneratedDtoTemplateModel MergeDtoTemplate(
        GeneratedDtoTemplateModel existing,
        GeneratedDtoTemplateModel incoming
    )
    {
        var merged = existing.Properties.ToDictionary(
            property => property.Name,
            StringComparer.Ordinal
        );
        foreach (var property in incoming.Properties)
        {
            if (!merged.TryGetValue(property.Name, out var current))
            {
                merged.Add(property.Name, property);
                continue;
            }

            merged[property.Name] = new GeneratedPropertyTemplateModel
            {
                Name = property.Name,
                TypeTemplate = MergeTemplateType(current.TypeTemplate, property.TypeTemplate),
                FallbackTypeTemplate = MergeTemplateType(
                    current.FallbackTypeTemplate,
                    property.FallbackTypeTemplate
                ),
                Documentation = current.Documentation ?? property.Documentation,
                IsSuppressed = current.IsSuppressed && property.IsSuppressed,
            };
        }

        var mergedProperties = merged
            .Values.OrderByDescending(property => property.IsSuppressed)
            .ThenBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        return existing with
        {
            Properties = mergedProperties,
            ShapeSignature =
                $"{existing.TemplateId}|{string.Join(";", mergedProperties.Select(property => $"{property.Name}:{property.TypeTemplate}:{property.FallbackTypeTemplate}:{property.IsSuppressed}"))}",
        };
    }

    private static string MergeTemplateType(string existingTypeName, string incomingTypeName)
    {
        if (string.IsNullOrWhiteSpace(existingTypeName))
        {
            return incomingTypeName;
        }

        if (
            string.IsNullOrWhiteSpace(incomingTypeName)
            || string.Equals(existingTypeName, incomingTypeName, StringComparison.Ordinal)
        )
        {
            return existingTypeName;
        }

        return existingTypeName;
    }

    private static string CreateDtoPlaceholderToken(string templateId)
    {
        return $"__LinqraftDto_{HashingHelper.ComputeHash(templateId, 16)}__";
    }

    private static IReadOnlyList<(string Name, ExpressionSyntax Expression)> GetProjectionMembers(
        ExpressionSyntax expression
    )
    {
        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax anonymousObject => anonymousObject
                .Initializers.Select(initializer =>
                    (GetAnonymousMemberName(initializer), initializer.Expression)
                )
                .ToList(),
            ObjectCreationExpressionSyntax objectCreation
                when objectCreation.Initializer is not null => objectCreation
                .Initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                .Select(assignment =>
                    (
                        assignment.Left switch
                        {
                            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                            _ => assignment.Left.ToString(),
                        },
                        assignment.Right
                    )
                )
                .ToList(),
            _ => Array.Empty<(string Name, ExpressionSyntax Expression)>(),
        };
    }

    private static bool TryFindProjectionInvocation(
        ExpressionSyntax expression,
        out InvocationExpressionSyntax invocation
    )
    {
        switch (expression)
        {
            case ConditionalExpressionSyntax conditionalExpression:
                if (TryFindProjectionInvocation(conditionalExpression.WhenTrue, out invocation))
                {
                    return true;
                }

                return TryFindProjectionInvocation(conditionalExpression.WhenFalse, out invocation);
            case BinaryExpressionSyntax binaryExpression
                when binaryExpression.IsKind(SyntaxKind.CoalesceExpression):
                if (TryFindProjectionInvocation(binaryExpression.Left, out invocation))
                {
                    return true;
                }

                return TryFindProjectionInvocation(binaryExpression.Right, out invocation);
            case InvocationExpressionSyntax directInvocation
                when IsProjectionInvocation(directInvocation):
                invocation = directInvocation;
                return true;
            case InvocationExpressionSyntax nestedInvocation
                when nestedInvocation.Expression is MemberAccessExpressionSyntax memberAccess:
                return TryFindProjectionInvocation(memberAccess.Expression, out invocation);
            case MemberAccessExpressionSyntax memberAccess:
                return TryFindProjectionInvocation(memberAccess.Expression, out invocation);
            case ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull
                    is InvocationExpressionSyntax whenNotNullInvocation
                    && IsProjectionInvocation(whenNotNullInvocation):
                invocation = whenNotNullInvocation;
                return true;
            case ConditionalAccessExpressionSyntax conditionalAccess:
                if (
                    conditionalAccess.WhenNotNull is ExpressionSyntax whenNotNull
                    && TryFindProjectionInvocation(whenNotNull, out invocation)
                )
                {
                    return true;
                }

                invocation = null!;
                return false;
            default:
                invocation = null!;
                return false;
        }
    }

    private static bool IsProjectionInvocation(InvocationExpressionSyntax invocation)
    {
        var name = GetInvocationName(invocation.Expression);
        return name is "Select" or "SelectMany" or "SelectExpr" or "SelectManyExpr" or "GroupByExpr";
    }

    private static ProjectionPattern? ResolveProjectionPattern(
        ExpressionSyntax selectorBody,
        int typeArgumentCount
    )
    {
        if (selectorBody is AnonymousObjectCreationExpressionSyntax)
        {
            return typeArgumentCount == 0
                ? ProjectionPattern.Anonymous
                : ProjectionPattern.ExplicitDto;
        }

        if (selectorBody is ObjectCreationExpressionSyntax)
        {
            return ProjectionPattern.PredefinedDto;
        }

        return null;
    }

    private static ExpressionSyntax? GetLambdaBodyExpression(LambdaExpressionSyntax lambda)
    {
        return lambda.Body as ExpressionSyntax;
    }

    private static string GetLambdaParameterName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized
                .ParameterList.Parameters.FirstOrDefault()
                ?.Identifier.ValueText
                ?? "x",
            _ => "x",
        };
    }

    private static ReceiverKind? ResolveReceiverKind(ITypeSymbol? receiverType)
    {
        if (SymbolNameHelper.IsQueryable(receiverType))
        {
            return ReceiverKind.IQueryable;
        }

        if (SymbolNameHelper.IsEnumerable(receiverType))
        {
            return ReceiverKind.IEnumerable;
        }

        return null;
    }

    private static ITypeSymbol? ResolveSourceType(
        SemanticModel semanticModel,
        ITypeSymbol? receiverType,
        SeparatedSyntaxList<TypeSyntax> typeArguments
    )
    {
        if (typeArguments.Count >= 2)
        {
            return semanticModel.GetTypeInfo(typeArguments[0]).Type;
        }

        if (receiverType is INamedTypeSymbol namedType)
        {
            if (namedType.TypeArguments.Length == 1)
            {
                return namedType.TypeArguments[0];
            }

            var interfaceType = namedType.AllInterfaces.FirstOrDefault(candidate =>
                candidate.TypeArguments.Length == 1
                && (
                    candidate.ConstructedFrom.ToDisplayString() == "System.Linq.IQueryable<T>"
                    || candidate.ConstructedFrom.ToDisplayString()
                        == "System.Collections.Generic.IEnumerable<T>"
                )
            );
            return interfaceType?.TypeArguments[0];
        }

        return null;
    }

    private static string CreateCaptureLocalName(string propertyName, int index)
    {
        var sanitized = SymbolNameHelper.SanitizeHintName(propertyName);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "value";
        }

        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
        {
            sanitized = "_" + sanitized;
        }

        return $"__linqraft_capture_{index}_{sanitized}";
    }

    private static string NormalizeExpressionText(ExpressionSyntax expression)
    {
        return expression.WithoutTrivia().ToString();
    }

    private static ISymbol? GetCaptureRootSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        var rootExpression = GetCaptureRootExpression(expression);
        return semanticModel.GetSymbolInfo(rootExpression, cancellationToken).Symbol;
    }

    private static ExpressionSyntax GetCaptureRootExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => GetCaptureRootExpression(
                memberAccess.Expression
            ),
            ConditionalAccessExpressionSyntax conditionalAccess => GetCaptureRootExpression(
                conditionalAccess.Expression
            ),
            ElementAccessExpressionSyntax elementAccess => GetCaptureRootExpression(
                elementAccess.Expression
            ),
            InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax memberAccess =>
                GetCaptureRootExpression(memberAccess.Expression),
            InvocationExpressionSyntax invocation => invocation,
            _ => expression,
        };
    }

    private static string BuildCollectionTypeName(
        ITypeSymbol? collectionType,
        string elementTypeName
    )
    {
        if (collectionType is IArrayTypeSymbol)
        {
            return $"{elementTypeName}[]";
        }

        if (
            collectionType is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.TypeArguments.Length == 1
        )
        {
            var container = namedType.ConstructedFrom.ToFullyQualifiedTypeName();
            var containerName = container[..container.IndexOf('<')];
            return $"{containerName}<{elementTypeName}>{GetNullableSuffix(namedType.NullableAnnotation)}";
        }

        return $"global::System.Collections.Generic.IEnumerable<{elementTypeName}>";
    }

    private static string ApplyExpressionNullability(string typeName, ITypeSymbol? expressionType)
    {
        return
            expressionType?.NullableAnnotation == NullableAnnotation.Annotated
            && !typeName.EndsWith("?", StringComparison.Ordinal)
            ? $"{typeName}?"
            : typeName;
    }

    private static bool IsCollectionLikeType(ITypeSymbol? type)
    {
        return type is IArrayTypeSymbol || SymbolNameHelper.IsEnumerable(type);
    }

    private static bool IsCollectionLikeResultExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            InvocationExpressionSyntax invocation => GetInvocationName(invocation.Expression) switch
            {
                "First"
                or "FirstOrDefault"
                or "Single"
                or "SingleOrDefault"
                or "Last"
                or "LastOrDefault"
                or "ElementAt"
                or "ElementAtOrDefault" => false,
                _ => true,
            },
            ConditionalExpressionSyntax conditionalExpression => IsCollectionLikeResultExpression(
                conditionalExpression.WhenTrue
            ) && IsCollectionLikeResultExpression(conditionalExpression.WhenFalse),
            BinaryExpressionSyntax binaryExpression
                when binaryExpression.IsKind(SyntaxKind.CoalesceExpression) =>
                IsCollectionLikeResultExpression(binaryExpression.Left)
                    || IsCollectionLikeResultExpression(binaryExpression.Right),
            _ => false,
        };
    }

    private static string UnwrapProjectedTypeName(
        string projectedTypeName,
        ITypeSymbol? lambdaBodyType
    )
    {
        if (lambdaBodyType is IArrayTypeSymbol arrayType)
        {
            return
                arrayType.ElementType.IsAnonymousType
                && TryGetSingleGenericArgument(projectedTypeName, out var parsedArrayElement)
                ? parsedArrayElement
                : arrayType.ElementType.ToFullyQualifiedTypeName();
        }

        if (
            lambdaBodyType is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.TypeArguments.Length == 1
        )
        {
            var elementType = namedType.TypeArguments[0];
            return
                elementType.IsAnonymousType
                && TryGetSingleGenericArgument(projectedTypeName, out var parsedElement)
                ? parsedElement
                : elementType.ToFullyQualifiedTypeName();
        }

        return TryGetSingleGenericArgument(projectedTypeName, out var parsedGenericArgument)
            ? parsedGenericArgument
            : projectedTypeName;
    }

    private static bool TryGetSingleGenericArgument(string typeName, out string genericArgument)
    {
        genericArgument = string.Empty;
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

        genericArgument = candidate;
        return true;
    }

    private static string RemoveNullableAnnotation(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName[..^1] : typeName;
    }

    private static string MakeNullable(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName : $"{typeName}?";
    }

    private static bool TryGetNullGuardBranch(
        ConditionalExpressionSyntax conditionalExpression,
        out ExpressionSyntax nonNullBranch
    )
    {
        if (conditionalExpression.WhenTrue.IsKind(SyntaxKind.NullLiteralExpression))
        {
            nonNullBranch = conditionalExpression.WhenFalse;
            return true;
        }

        if (conditionalExpression.WhenFalse.IsKind(SyntaxKind.NullLiteralExpression))
        {
            nonNullBranch = conditionalExpression.WhenTrue;
            return true;
        }

        nonNullBranch = null!;
        return false;
    }

    private static string ResolveCallerNamespace(SyntaxNode node, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetEnclosingSymbol(node.SpanStart);
        return SymbolNameHelper.GetNamespace(symbol?.ContainingNamespace);
    }

    private static string? ResolveNamedType(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        string fallbackNamespace
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        if (type is null)
        {
            type = semanticModel.GetSymbolInfo(typeSyntax).Symbol switch
            {
                ITypeSymbol typeSymbol => typeSymbol,
                IAliasSymbol aliasSymbol => aliasSymbol.Target as ITypeSymbol,
                _ => null,
            };
        }

        if (type is null && typeSyntax is IdentifierNameSyntax identifierName)
        {
            type = semanticModel
                .LookupNamespacesAndTypes(
                    identifierName.SpanStart,
                    name: identifierName.Identifier.ValueText
                )
                .OfType<ITypeSymbol>()
                .FirstOrDefault();
        }

        if (type is not null)
        {
            return type.ToFullyQualifiedTypeName();
        }

        var name = GetUnqualifiedTypeName(typeSyntax);
        return string.IsNullOrWhiteSpace(fallbackNamespace)
            ? $"global::{name}"
            : $"global::{fallbackNamespace}.{name}";
    }

    private static string GetNullableSuffix(NullableAnnotation nullableAnnotation)
    {
        return nullableAnnotation == NullableAnnotation.Annotated ? "?" : string.Empty;
    }

    private static string GetMappingGeneratedAccessibilityKeyword(
        Accessibility declaredAccessibility,
        InvocationExpressionSyntax selectExpr,
        SemanticModel semanticModel,
        string? requestedVisibilityKeyword
    )
    {
        var nameSyntax = GetInvocationNameSyntax(selectExpr.Expression) as GenericNameSyntax;
        if (nameSyntax?.TypeArgumentList.Arguments.Count >= 2)
        {
            var sourceType = semanticModel
                .GetTypeInfo(nameSyntax.TypeArgumentList.Arguments[0])
                .Type;
            var resultType = semanticModel
                .GetTypeInfo(nameSyntax.TypeArgumentList.Arguments[1])
                .Type;
            if (!IsPubliclyAccessible(sourceType) || !IsPubliclyAccessible(resultType))
            {
                return "internal";
            }
        }

        return requestedVisibilityKeyword
            ?? SymbolNameHelper.GetAccessibilityKeyword(declaredAccessibility);
    }

    private static bool IsPubliclyAccessible(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        return typeSymbol switch
        {
            IArrayTypeSymbol arrayType => IsPubliclyAccessible(arrayType.ElementType),
            IDynamicTypeSymbol => true,
            ITypeParameterSymbol => true,
            INamedTypeSymbol namedType => namedType.DeclaredAccessibility == Accessibility.Public
                && (
                    namedType.ContainingType is null
                    || IsPubliclyAccessible(namedType.ContainingType)
                )
                && namedType.TypeArguments.All(IsPubliclyAccessible),
            _ => typeSymbol.DeclaredAccessibility == Accessibility.Public,
        };
    }

    private static ProjectionOperationKind? GetProjectionOperationKind(
        InvocationExpressionSyntax invocation
    )
    {
        return GetInvocationName(invocation.Expression) switch
        {
            "SelectExpr" => ProjectionOperationKind.Select,
            "SelectManyExpr" => ProjectionOperationKind.SelectMany,
            "GroupByExpr" => ProjectionOperationKind.GroupBy,
            _ => null,
        };
    }

    private static bool IsGenerateInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (!string.Equals(GetInvocationName(invocation.Expression), "Generate", StringComparison.Ordinal))
        {
            return false;
        }

        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression.ToString().EndsWith("LinqraftKit", StringComparison.Ordinal) =>
                true,
            _ => (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol)
                    ?.ContainingType.Name == "LinqraftKit",
        };
    }

    private static bool IsSelectExprInvocation(InvocationExpressionSyntax invocation)
    {
        return string.Equals(
            GetInvocationName(invocation.Expression),
            "SelectExpr",
            StringComparison.Ordinal
        );
    }

    private static bool IsInsideMappingDeclaration(SyntaxNode node)
    {
        return node.Ancestors()
            .OfType<MemberDeclarationSyntax>()
            .Any(member =>
                member
                    .AttributeLists.SelectMany(list => list.Attributes)
                    .Any(attribute =>
                        attribute
                            .Name.ToString()
                            .Contains("LinqraftMappingGenerate", StringComparison.Ordinal)
                    )
            );
    }

    private static ExpressionSyntax? GetReceiverExpression(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => null,
            _ => null,
        };
    }

    private static string GetInvocationName(ExpressionSyntax expression)
    {
        return GetInvocationNameSyntax(expression)?.Identifier.ValueText ?? string.Empty;
    }

    private static SimpleNameSyntax? GetInvocationNameSyntax(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            IdentifierNameSyntax identifier => identifier,
            GenericNameSyntax genericName => genericName,
            _ => null,
        };
    }

    private static string GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        return initializer.NameEquals?.Name.Identifier.ValueText
            ?? AnonymousMemberNameResolver.Get(initializer.Expression);
    }

    private static bool InheritsFromMappingDeclare(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.Name == "LinqraftMappingDeclare")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static AttributeData? GetMappingGenerateAttribute(
        ImmutableArray<AttributeData> attributes
    )
    {
        return attributes.FirstOrDefault(attribute =>
            attribute.AttributeClass?.Name == "LinqraftMappingGenerateAttribute"
        );
    }

    private static string? GetMappingMethodName(AttributeData? attribute)
    {
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static string? GetMappingVisibilityKeyword(AttributeData? attribute)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (
                !string.Equals(namedArgument.Key, "Visibility", StringComparison.Ordinal)
                || namedArgument.Value.Value is null
            )
            {
                continue;
            }

            return Convert.ToInt32(namedArgument.Value.Value) switch
            {
                1 => "internal",
                2 => "public",
                _ => null,
            };
        }

        return null;
    }

    private static ContainingTypeInfo[] GetContainingTypes(INamedTypeSymbol? symbol)
    {
        if (symbol?.ContainingType is null)
        {
            return Array.Empty<ContainingTypeInfo>();
        }

        var stack = new Stack<ContainingTypeInfo>();
        var current = symbol.ContainingType;
        while (current is not null)
        {
            stack.Push(
                new ContainingTypeInfo
                {
                    AccessibilityKeyword = SymbolNameHelper.GetAccessibilityKeyword(
                        current.DeclaredAccessibility
                    ),
                    DeclarationKeyword = current.IsRecord ? "record" : "class",
                    Name = current.Name,
                }
            );
            current = current.ContainingType;
        }

        return stack.ToArray();
    }

    private static string GetUnqualifiedTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => qualifiedName.Right.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => typeSyntax.ToString().Split('.').Last(),
        };
    }

    private readonly record struct CollectionProjectionInfo
    {
        public required InvocationExpressionSyntax Invocation { get; init; }

        public required ExpressionSyntax LambdaBody { get; init; }

        public required ITypeSymbol? ExpressionType { get; init; }

        public required string ProjectionMethodName { get; init; }

        public required bool IsNestedExplicitDto { get; init; }

        public required TypeSyntax? NestedResultTypeSyntax { get; init; }

        public required TypeSyntax? SourceTypeSyntax { get; init; }
    }

    private readonly record struct ProjectionBuildResult
    {
        public required ProjectionTemplateModel Projection { get; init; }

        public required GeneratedDtoTemplateModel? DtoTemplate { get; init; }

        public required IReadOnlyDictionary<TextSpan, string> ReplacementTypes { get; init; }
    }
}
