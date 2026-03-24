using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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

/// <summary>
/// Analyzes projection syntax and builds template models for later source generation.
/// </summary>
internal static partial class ProjectionTemplateBuilder
{
    // Entry points classify projection syntax and collect the metadata needed for later template expansion.

    /// <summary>
    /// Analyzes a projection invocation discovered by the incremental generator.
    /// </summary>
    public static ProjectionSourceTemplateModel? AnalyzeProjectionInvocation(
        GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        return syntaxContext.Node is InvocationExpressionSyntax invocation
            ? AnalyzeQueryProjectionInvocation(
                invocation,
                syntaxContext.SemanticModel,
                cancellationToken,
                allowInterceptor: true,
                ownerHintName: null,
                generatorOptions
            )
            : null;
    }

    /// <summary>
    /// Analyzes an object-generation invocation discovered by the incremental generator.
    /// </summary>
    public static ObjectGenerationSourceTemplateModel? AnalyzeObjectGenerationInvocation(
        GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        return syntaxContext.Node is InvocationExpressionSyntax invocation
            ? AnalyzeObjectGenerationInvocationCore(
                invocation,
                syntaxContext.SemanticModel,
                cancellationToken,
                generatorOptions
            )
            : null;
    }

    /// <summary>
    /// Analyzes a mapping declaration class and extracts its generated projection template.
    /// </summary>
    public static MappingSourceTemplateModel? AnalyzeMappingClass(
        GeneratorAttributeSyntaxContext attributeContext,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        if (
            attributeContext.TargetNode is not ClassDeclarationSyntax declaration
            || attributeContext.TargetSymbol is not INamedTypeSymbol classSymbol
        )
        {
            return null;
        }

        if (!InheritsFromMappingDeclare(classSymbol, generatorOptions, cancellationToken))
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
            .FirstOrDefault(invocation =>
                IsSelectExprInvocation(
                    invocation,
                    attributeContext.SemanticModel,
                    generatorOptions,
                    cancellationToken
                )
            );
        if (selectExpr is null)
        {
            return null;
        }

        var mappingHintName =
            $"{generatorOptions.MappingHintNamePrefix}_{HashingHelper.ComputeHash(classSymbol.ToDisplayString(), 16, cancellationToken)}";
        var projection = AnalyzeQueryProjectionInvocation(
            selectExpr,
            attributeContext.SemanticModel,
            cancellationToken,
            allowInterceptor: false,
            ownerHintName: mappingHintName,
            generatorOptions
        );
        if (projection is null)
        {
            return null;
        }

        var mappingAttribute = GetMappingGenerateAttribute(
            classSymbol.GetAttributes(),
            generatorOptions
        );
        var methodName = GetMappingMethodName(mappingAttribute);
        var accessibilityKeyword = GetMappingGeneratedAccessibilityKeyword(
            classSymbol.DeclaredAccessibility,
            selectExpr,
            attributeContext.SemanticModel,
            GetMappingVisibilityKeyword(mappingAttribute, cancellationToken),
            cancellationToken
        );
        return new MappingSourceTemplateModel
        {
            Request = new MappingRequestTemplate
            {
                HintName = mappingHintName,
                Origin = CreateOrigin(selectExpr),
                Namespace = SymbolNameHelper.GetNamespace(classSymbol.ContainingNamespace),
                ContainingTypeName =
                    $"{classSymbol.Name}_{HashingHelper.ComputeHash(classSymbol.ToDisplayString(), 8, cancellationToken)}",
                AccessibilityKeyword = accessibilityKeyword,
                MethodAccessibilityKeyword = accessibilityKeyword,
                MethodName = string.IsNullOrWhiteSpace(methodName)
                    ? $"ProjectTo{classSymbol.BaseType?.TypeArguments.FirstOrDefault()?.Name}"
                    : methodName!,
                ReceiverKind = projection.Request.ReceiverKind,
                SourceTypeName = projection.Request.SourceTypeName,
                ResultTypeTemplate = projection.Request.ResultTypeTemplate,
                SelectorParameterName = projection.Request.SelectorParameterName,
                Captures = projection.Request.Captures,
                CanUsePrebuiltExpressionWhenConfigured = projection
                    .Request
                    .CanUsePrebuiltExpressionWhenConfigured,
                Projection = projection.Request.Projection!,
                InnerJoinFilterBodyTemplate = projection.Request.InnerJoinFilterBodyTemplate,
            },
            GeneratedDtos = projection.GeneratedDtos,
        };
    }

    /// <summary>
    /// Analyzes a mapping declaration method and extracts its generated projection template.
    /// </summary>
    public static MappingSourceTemplateModel? AnalyzeMappingMethod(
        GeneratorAttributeSyntaxContext attributeContext,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
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
            .FirstOrDefault(invocation =>
                IsSelectExprInvocation(
                    invocation,
                    attributeContext.SemanticModel,
                    generatorOptions,
                    cancellationToken
                )
            );
        if (selectExpr is null)
        {
            return null;
        }

        var mappingHintName =
            $"{generatorOptions.MappingHintNamePrefix}_{HashingHelper.ComputeHash(methodSymbol.ToDisplayString(), 16, cancellationToken)}";
        var projection = AnalyzeQueryProjectionInvocation(
            selectExpr,
            attributeContext.SemanticModel,
            cancellationToken,
            allowInterceptor: false,
            ownerHintName: mappingHintName,
            generatorOptions
        );
        if (projection is null)
        {
            return null;
        }

        var mappingAttribute = GetMappingGenerateAttribute(
            methodSymbol.GetAttributes(),
            generatorOptions
        );
        return new MappingSourceTemplateModel
        {
            Request = new MappingRequestTemplate
            {
                HintName = mappingHintName,
                Origin = CreateOrigin(selectExpr),
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
                    GetMappingVisibilityKeyword(mappingAttribute, cancellationToken),
                    cancellationToken
                ),
                MethodName =
                    GetMappingMethodName(mappingAttribute) ?? declaration.Identifier.ValueText,
                ReceiverKind = projection.Request.ReceiverKind,
                SourceTypeName = projection.Request.SourceTypeName,
                ResultTypeTemplate = projection.Request.ResultTypeTemplate,
                SelectorParameterName = projection.Request.SelectorParameterName,
                Captures = projection.Request.Captures,
                CanUsePrebuiltExpressionWhenConfigured = projection
                    .Request
                    .CanUsePrebuiltExpressionWhenConfigured,
                Projection = projection.Request.Projection!,
                InnerJoinFilterBodyTemplate = projection.Request.InnerJoinFilterBodyTemplate,
            },
            GeneratedDtos = projection.GeneratedDtos,
        };
    }

    /// <summary>
    /// Analyzes query projection invocation.
    /// </summary>
    private static ProjectionSourceTemplateModel? AnalyzeQueryProjectionInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool allowInterceptor,
        string? ownerHintName,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operationKind = GetProjectionOperationKind(
            invocation,
            semanticModel,
            generatorOptions,
            cancellationToken
        );
        if (operationKind is null)
        {
            return null;
        }

        var useLinqraftOperationKind = GetUseLinqraftProjectionOperationKind(
            invocation,
            semanticModel,
            generatorOptions,
            cancellationToken
        );
        var usesFluentQuerySyntax = useLinqraftOperationKind is not null;

        if (allowInterceptor && IsInsideMappingDeclaration(invocation, generatorOptions))
        {
            return null;
        }

        var receiver = GetReceiverExpression(
            invocation,
            semanticModel,
            generatorOptions,
            cancellationToken
        );
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

        var sourceType = ResolveSourceType(
            semanticModel,
            receiverType,
            usesFluentQuerySyntax ? default : typeArguments,
            cancellationToken
        );
        if (sourceType is null)
        {
            return null;
        }

        var captureInfo = AnalyzeCaptures(
            invocation,
            semanticModel,
            cancellationToken,
            generatorOptions
        );
        var captureEntries = captureInfo.Entries;
        var callerNamespace = ResolveCallerNamespace(invocation, semanticModel, cancellationToken);
        var methodHash = HashingHelper.ComputeHash(
            $"{invocation.SyntaxTree.FilePath}|{invocation.SpanStart}|{invocation}",
            16,
            cancellationToken
        );
        var effectiveOwnerHintName =
            ownerHintName ?? $"{GetInvocationName(invocation.Expression)}_{methodHash}";
        var selectorLambda =
            operationKind.Value == ProjectionOperationKind.GroupBy && lambdas.Length >= 2
                ? lambdas[1]
                : lambdas[0];
        var buildContext = new ProjectionBuildContext(
            semanticModel,
            captureEntries,
            cancellationToken,
            generatorOptions,
            GetLambdaParameterName(selectorLambda),
            sourceType,
            GetProjectionHelperParameterName(selectorLambda),
            GetProjectionHelperParameterTypeName(selectorLambda, semanticModel, cancellationToken)
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
                Origin = CreateOrigin(invocation),
                OperationKind = operationKind.Value,
                ReceiverKind = receiverKind.Value,
                UsesFluentQuerySyntax = usesFluentQuerySyntax,
                Pattern = analyzedProjection.Pattern,
                SourceTypeName = sourceType.ToFullyQualifiedTypeName(),
                ResultTypeTemplate = analyzedProjection.ResultTypeTemplate,
                SelectorParameterName = analyzedProjection.SelectorParameterName,
                UsesProjectionHelperParameter = analyzedProjection.HasProjectionHelperParameter,
                ProjectionHelperParameterName = analyzedProjection.ProjectionHelperParameterName,
                ProjectionHelperParameterTypeName =
                    analyzedProjection.ProjectionHelperParameterTypeName,
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
                        ValueAccessor = capture.ValueAccessor,
                    })
                    .ToArray(),
                CaptureTransportKind = captureInfo.TransportKind,
                CaptureTransportTypeName = captureInfo.TransportTypeName,
                Projection = analyzedProjection.ProjectionTemplate,
                ProjectionBodyTemplate = analyzedProjection.ProjectionBodyTemplate,
                InnerJoinFilterBodyTemplate = analyzedProjection.InnerJoinFilterTemplate,
            },
            GeneratedDtos = buildContext.GetGeneratedDtos(),
        };
    }

    /// <summary>
    /// Analyzes select projection.
    /// </summary>
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

    /// <summary>
    /// Analyzes group by projection.
    /// </summary>
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

        var hasExplicitResultType = HasExplicitResultType(
            invocation,
            typeArguments.Count,
            semanticModel,
            buildContext.GeneratorOptions,
            cancellationToken
        );
        var pattern = ResolveProjectionPattern(selectorBody, hasExplicitResultType ? 1 : 0);
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
            replacementTypes: null,
            cancellationToken
        );

        return projection with
        {
            KeySelectorParameterName = GetLambdaParameterName(keySelectorLambda),
            KeySelectorBodyTemplate = keyBodyTemplate,
        };
    }

    /// <summary>
    /// Analyzes select many projection.
    /// </summary>
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
        var hasExplicitResultType = HasExplicitResultType(
            invocation,
            typeArguments.Count,
            semanticModel,
            buildContext.GeneratorOptions,
            cancellationToken
        );
        if (hasExplicitResultType)
        {
            var explicitTypeSyntax = typeArguments[^1];
            resultTypeTemplate =
                ResolveNamedType(
                    explicitTypeSyntax,
                    semanticModel,
                    callerNamespace,
                    cancellationToken
                ) ?? "TResult";
            if (
                TryFindProjectedAnonymousBody(
                    selectorBody,
                    buildContext.GeneratorOptions,
                    out var projectedAnonymousBody
                ) && projectedAnonymousBody is not null
            )
            {
                useObjectSelectorSignature = true;
                var rootDto = CreateRootDtoTemplate(
                    explicitTypeSyntax,
                    semanticModel,
                    invocation,
                    sourceType as INamedTypeSymbol,
                    ownerHintName,
                    cancellationToken,
                    buildContext.GeneratorOptions
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
                    ownerHintName: ownerHintName,
                    cancellationToken: cancellationToken
                );
                replacementTypes[projectedAnonymousBody.Span] = rootDto.PlaceholderToken;
                foreach (var pair in buildResult.ReplacementTypes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    replacementTypes[pair.Key] = pair.Value;
                }

                buildContext.RegisterDto(buildResult.DtoTemplate!, cancellationToken);
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
            namedContext: hasExplicitResultType,
            defaultNamespace: callerNamespace,
            useGlobalNamespaceFallback: string.IsNullOrWhiteSpace(callerNamespace),
            ownerHintName: ownerHintName,
            cancellationToken: cancellationToken
        );
        var bodyTemplate = buildContext.CreateStandaloneBodyTemplate(
            selectorBody,
            collectionTypeTemplate,
            replacementTypes,
            cancellationToken
        );

        return new AnalyzedProjection
        {
            Pattern = hasExplicitResultType
                ? ProjectionPattern.ExplicitDto
                : ProjectionPattern.Anonymous,
            ResultTypeTemplate = resultTypeTemplate,
            SelectorParameterName = GetLambdaParameterName(selectorLambda),
            HasProjectionHelperParameter = UsesProjectionHelperParameter(selectorLambda),
            ProjectionHelperParameterName = GetProjectionHelperParameterName(selectorLambda),
            ProjectionHelperParameterTypeName = GetProjectionHelperParameterTypeName(
                selectorLambda,
                semanticModel,
                cancellationToken
            ),
            KeySelectorParameterName = null,
            KeySelectorBodyTemplate = null,
            UseObjectSelectorSignature = useObjectSelectorSignature,
            ProjectionTemplate = null,
            ProjectionBodyTemplate = bodyTemplate,
            InnerJoinFilterTemplate = buildContext.CreateInnerJoinFilterTemplate(
                selectorBody,
                cancellationToken
            ),
        };
    }

    /// <summary>
    /// Creates object projection.
    /// </summary>
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
                    ownerHintName: ownerHintName,
                    cancellationToken: cancellationToken
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
                    cancellationToken,
                    buildContext.GeneratorOptions
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
                    ownerHintName: ownerHintName,
                    cancellationToken: cancellationToken
                );
                projectionTemplate = buildResult.Projection;
                buildContext.RegisterDto(buildResult.DtoTemplate!, cancellationToken);
                resultTypeTemplate = rootDto.PlaceholderToken;
                break;
            }
            case ProjectionPattern.PredefinedDto:
            {
                var predefinedTypeSyntax = ((ObjectCreationExpressionSyntax)selectorBody).Type;
                resultTypeTemplate =
                    ResolveNamedType(
                        predefinedTypeSyntax,
                        semanticModel,
                        callerNamespace,
                        cancellationToken
                    ) ?? predefinedTypeSyntax.ToString();
                var buildResult = buildContext.BuildProjectionTemplate(
                    selectorBody,
                    replacementTypeToken: null,
                    dtoTemplate: null,
                    existingPropertyNames: null,
                    namedContext: true,
                    defaultNamespace: callerNamespace,
                    useGlobalNamespaceFallback: string.IsNullOrWhiteSpace(callerNamespace),
                    ownerHintName: ownerHintName,
                    cancellationToken: cancellationToken
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
            HasProjectionHelperParameter = UsesProjectionHelperParameter(selectorLambda),
            ProjectionHelperParameterName = GetProjectionHelperParameterName(selectorLambda),
            ProjectionHelperParameterTypeName = GetProjectionHelperParameterTypeName(
                selectorLambda,
                semanticModel,
                cancellationToken
            ),
            KeySelectorParameterName = null,
            KeySelectorBodyTemplate = null,
            UseObjectSelectorSignature = pattern == ProjectionPattern.ExplicitDto,
            ProjectionTemplate = projectionTemplate,
            ProjectionBodyTemplate = null,
            InnerJoinFilterTemplate = buildContext.CreateInnerJoinFilterTemplate(
                selectorBody,
                cancellationToken
            ),
        };
    }

    /// <summary>
    /// Attempts to handle find projected anonymous body.
    /// </summary>
    private static bool TryFindProjectedAnonymousBody(
        ExpressionSyntax expression,
        LinqraftGeneratorOptionsCore generatorOptions,
        out AnonymousObjectCreationExpressionSyntax? anonymousBody
    )
    {
        anonymousBody = null;
        if (!TryFindProjectionInvocation(expression, generatorOptions, out var invocation))
        {
            return false;
        }

        var lambda = invocation
            .ArgumentList.Arguments.Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
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

        return body is not null
            && TryFindProjectedAnonymousBody(body, generatorOptions, out anonymousBody);
    }

    /// <summary>
    /// Analyzes object generation invocation core.
    /// </summary>
    private static ObjectGenerationSourceTemplateModel? AnalyzeObjectGenerationInvocationCore(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsGenerateInvocation(invocation, semanticModel, cancellationToken, generatorOptions))
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
            16,
            cancellationToken
        );
        var ownerHintName = $"{generatorOptions.ObjectGenerationHintNamePrefix}_{methodHash}";
        var rootDto = CreateRootDtoTemplate(
            nameSyntax.TypeArgumentList.Arguments[0],
            semanticModel,
            invocation,
            sourceType: null,
            ownerHintName,
            cancellationToken,
            generatorOptions
        );
        var captureInfo = AnalyzeCaptures(
            invocation,
            semanticModel,
            cancellationToken,
            generatorOptions
        );
        var captureEntries = captureInfo.Entries;
        var buildContext = new ProjectionBuildContext(
            semanticModel,
            captureEntries,
            cancellationToken,
            generatorOptions,
            selectorParameterName: null,
            selectorParameterType: null,
            projectionHelperParameterName: null,
            projectionHelperParameterTypeName: null
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
            ownerHintName: ownerHintName,
            cancellationToken: cancellationToken
        );
        buildContext.RegisterDto(buildResult.DtoTemplate!, cancellationToken);
        var interceptableLocation = semanticModel.GetInterceptableLocation(
            invocation,
            cancellationToken
        );

        return new ObjectGenerationSourceTemplateModel
        {
            Request = new ObjectGenerationRequestTemplate
            {
                HintName = ownerHintName,
                MethodName = ownerHintName,
                Origin = CreateOrigin(invocation),
                ResultTypeTemplate = rootDto.PlaceholderToken,
                Projection = buildResult.Projection,
                Captures = captureEntries
                    .Select(capture => new CaptureParameterModel
                    {
                        PropertyName = capture.PropertyName,
                        LocalName = capture.LocalName,
                        TypeName = capture.TypeName,
                        ValueAccessor = capture.ValueAccessor,
                    })
                    .ToArray(),
                CaptureTransportKind = captureInfo.TransportKind,
                CaptureTransportTypeName = captureInfo.TransportTypeName,
                InterceptableLocationVersion = interceptableLocation?.Version,
                InterceptableLocationData = interceptableLocation?.Data,
            },
            GeneratedDtos = buildContext.GetGeneratedDtos(),
        };
    }

    /// <summary>
    /// Provides analyzed projection.
    /// </summary>
    private sealed record AnalyzedProjection
    {
        public required ProjectionPattern Pattern { get; init; }

        public required string ResultTypeTemplate { get; init; }

        public required string SelectorParameterName { get; init; }

        public required bool HasProjectionHelperParameter { get; init; }

        public string? ProjectionHelperParameterName { get; init; }

        public string? ProjectionHelperParameterTypeName { get; init; }

        public required string? KeySelectorParameterName { get; init; }

        public required string? KeySelectorBodyTemplate { get; init; }

        public required bool UseObjectSelectorSignature { get; init; }

        public ProjectionTemplateModel? ProjectionTemplate { get; init; }

        public string? ProjectionBodyTemplate { get; init; }

        public string? InnerJoinFilterTemplate { get; init; }
    }
}
