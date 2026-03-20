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

internal static class ProjectionTemplateBuilder
{
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
            .FirstOrDefault(invocation => IsSelectExprInvocation(invocation, generatorOptions));
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
            },
            GeneratedDtos = projection.GeneratedDtos,
        };
    }

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
            .FirstOrDefault(invocation => IsSelectExprInvocation(invocation, generatorOptions));
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
            },
            GeneratedDtos = projection.GeneratedDtos,
        };
    }

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

        var operationKind = GetProjectionOperationKind(invocation, generatorOptions);
        if (operationKind is null)
        {
            return null;
        }

        if (allowInterceptor && IsInsideMappingDeclaration(invocation, generatorOptions))
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

        var sourceType = ResolveSourceType(
            semanticModel,
            receiverType,
            typeArguments,
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
        var buildContext = new ProjectionBuildContext(
            semanticModel,
            captureEntries,
            cancellationToken,
            generatorOptions
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
                        ValueAccessor = capture.ValueAccessor,
                    })
                    .ToArray(),
                CaptureTransportKind = captureInfo.TransportKind,
                CaptureTransportTypeName = captureInfo.TransportTypeName,
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
            replacementTypes: null,
            cancellationToken
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
            namedContext: typeArguments.Count >= 2,
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
            Pattern =
                typeArguments.Count >= 2
                    ? ProjectionPattern.ExplicitDto
                    : ProjectionPattern.Anonymous,
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
            KeySelectorParameterName = null,
            KeySelectorBodyTemplate = null,
            UseObjectSelectorSignature = pattern == ProjectionPattern.ExplicitDto,
            ProjectionTemplate = projectionTemplate,
            ProjectionBodyTemplate = null,
        };
    }

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
            generatorOptions
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
        private readonly LinqraftGeneratorOptionsCore _generatorOptions;
        private readonly Dictionary<string, GeneratedDtoTemplateModel> _generatedDtos = new(
            StringComparer.Ordinal
        );

        public ProjectionBuildContext(
            SemanticModel semanticModel,
            IReadOnlyList<ProjectionExpressionEmitter.CaptureEntry> captureEntries,
            CancellationToken cancellationToken,
            LinqraftGeneratorOptionsCore generatorOptions
        )
        {
            _semanticModel = semanticModel;
            _captureEntries = captureEntries;
            _cancellationToken = cancellationToken;
            _generatorOptions = generatorOptions;
        }

        public LinqraftGeneratorOptionsCore GeneratorOptions => _generatorOptions;

        private CancellationToken ResolveCancellationToken(CancellationToken cancellationToken)
        {
            return cancellationToken == default ? _cancellationToken : cancellationToken;
        }

        public ProjectionBuildResult BuildProjectionTemplate(
            ExpressionSyntax expression,
            string? replacementTypeToken,
            GeneratedDtoTemplateModel? dtoTemplate,
            HashSet<string>? existingPropertyNames,
            bool namedContext,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
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
                cancellationToken.ThrowIfCancellationRequested();
                var memberReplacements = new Dictionary<TextSpan, string>();
                var typeTemplate = AnalyzeMemberType(
                    member.Expression,
                    member.Name,
                    memberReplacements,
                    namedContext,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName,
                    cancellationToken
                );
                var documentation = DocumentationExtractor.GetExpressionDocumentation(
                    member.Expression,
                    _semanticModel,
                    LinqraftCommentOutput.All,
                    cancellationToken
                );
                var canUseCollectionFallback =
                    dtoTemplate is not null
                    && QualifiesForCollectionNullabilityRemoval(
                        member.Expression,
                        cancellationToken
                    );
                var fallbackTypeTemplate = canUseCollectionFallback
                    ? RemoveNullableAnnotation(typeTemplate)
                    : typeTemplate;

                foreach (var pair in memberReplacements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    replacementTypes[pair.Key] = pair.Value;
                }

                var valueTemplate = CreateValueTemplate(
                    member.Expression,
                    typeTemplate,
                    useEmptyCollectionFallback: false,
                    replacementTypes,
                    cancellationToken
                );
                var fallbackValueTemplate = canUseCollectionFallback
                    ? CreateValueTemplate(
                        member.Expression,
                        fallbackTypeTemplate,
                        useEmptyCollectionFallback: true,
                        replacementTypes,
                        cancellationToken
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
            string ownerHintName,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            return AnalyzeMemberType(
                expression,
                memberName,
                replacementTypes,
                namedContext,
                defaultNamespace,
                useGlobalNamespaceFallback,
                ownerHintName,
                cancellationToken
            );
        }

        public string CreateStandaloneBodyTemplate(
            ExpressionSyntax expression,
            string rootTypeName,
            IReadOnlyDictionary<TextSpan, string>? replacementTypes,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            return CreateValueTemplate(
                expression,
                rootTypeName,
                useEmptyCollectionFallback: false,
                replacementTypes ?? new Dictionary<TextSpan, string>(),
                cancellationToken
            );
        }

        public void RegisterDto(
            GeneratedDtoTemplateModel dtoTemplate,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            if (_generatedDtos.TryGetValue(dtoTemplate.TemplateId, out var existing))
            {
                _generatedDtos[dtoTemplate.TemplateId] = MergeDtoTemplate(
                    existing,
                    dtoTemplate,
                    cancellationToken
                );
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
            string ownerHintName,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
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
                        ownerHintName,
                        cancellationToken
                    )
                );
            }

            if (expression is AnonymousObjectCreationExpressionSyntax anonymousObject)
            {
                if (!namedContext)
                {
                    var inferred = _semanticModel.GetTypeInfo(expression, cancellationToken).Type;
                    return inferred?.ToFullyQualifiedTypeName() ?? "object";
                }

                var nestedDto = CreateNestedDtoTemplate(
                    memberName,
                    anonymousObject,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName,
                    cancellationToken
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
                    ownerHintName: ownerHintName,
                    cancellationToken: cancellationToken
                );
                MergeReplacementTypes(
                    replacementTypes,
                    nestedBuildResult.ReplacementTypes,
                    cancellationToken
                );
                RegisterDto(nestedBuildResult.DtoTemplate!, cancellationToken);
                return nestedDto.PlaceholderToken;
            }

            if (expression is ObjectCreationExpressionSyntax namedObject)
            {
                if (namedObject.Initializer is not null)
                {
                    foreach (var nestedMember in GetProjectionMembers(namedObject))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        AnalyzeMemberType(
                            nestedMember.Expression,
                            nestedMember.Name,
                            replacementTypes,
                            namedContext: true,
                            defaultNamespace,
                            useGlobalNamespaceFallback,
                            ownerHintName,
                            cancellationToken
                        );
                    }
                }

                var typeSymbol = _semanticModel
                    .GetTypeInfo(namedObject.Type, cancellationToken)
                    .Type;
                return ResolveNamedType(
                        namedObject.Type,
                        _semanticModel,
                        defaultNamespace,
                        cancellationToken
                    )
                    ?? typeSymbol?.ToFullyQualifiedTypeName()
                    ?? namedObject.Type.ToString();
            }

            if (
                TryGetCollectionProjection(
                    expression,
                    out var collectionProjection,
                    cancellationToken
                )
            )
            {
                string projectedTypeTemplate;
                if (collectionProjection.IsNestedExplicitDto)
                {
                    var nestedResultType = ResolveNamedType(
                        collectionProjection.NestedResultTypeSyntax!,
                        _semanticModel,
                        defaultNamespace,
                        cancellationToken
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
                                        cancellationToken
                                    )
                                    .Type as INamedTypeSymbol,
                                ownerHintName,
                                cancellationToken,
                                _generatorOptions
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
                                ownerHintName: ownerHintName,
                                cancellationToken: cancellationToken
                            );
                            MergeReplacementTypes(
                                replacementTypes,
                                nestedBuildResult.ReplacementTypes,
                                cancellationToken
                            );
                            RegisterDto(nestedBuildResult.DtoTemplate!, cancellationToken);
                            projectedTypeTemplate = nestedDto.PlaceholderToken;
                            return BuildProjectedResultType(
                                expression,
                                collectionProjection.ExpressionType,
                                collectionProjection.LambdaBody,
                                projectedTypeTemplate,
                                collectionProjection.ProjectionMethodName,
                                cancellationToken
                            );
                        }

                        projectedTypeTemplate = nestedResultType;
                        return BuildProjectedResultType(
                            expression,
                            collectionProjection.ExpressionType,
                            collectionProjection.LambdaBody,
                            projectedTypeTemplate,
                            collectionProjection.ProjectionMethodName,
                            cancellationToken
                        );
                    }
                }

                if (
                    collectionProjection.LambdaBody
                        is AnonymousObjectCreationExpressionSyntax anonymousBody
                    && namedContext
                )
                {
                    if (
                        replacementTypes.TryGetValue(
                            anonymousBody.Span,
                            out var existingProjectedType
                        )
                    )
                    {
                        projectedTypeTemplate = existingProjectedType;
                        return BuildProjectedResultType(
                            expression,
                            collectionProjection.ExpressionType,
                            collectionProjection.LambdaBody,
                            projectedTypeTemplate,
                            collectionProjection.ProjectionMethodName,
                            cancellationToken
                        );
                    }

                    var nestedDto = CreateNestedDtoTemplate(
                        memberName,
                        anonymousBody,
                        defaultNamespace,
                        useGlobalNamespaceFallback,
                        ownerHintName,
                        cancellationToken
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
                        ownerHintName: ownerHintName,
                        cancellationToken: cancellationToken
                    );
                    MergeReplacementTypes(
                        replacementTypes,
                        nestedBuildResult.ReplacementTypes,
                        cancellationToken
                    );
                    RegisterDto(nestedBuildResult.DtoTemplate!, cancellationToken);
                    projectedTypeTemplate = nestedDto.PlaceholderToken;
                    return BuildProjectedResultType(
                        expression,
                        collectionProjection.ExpressionType,
                        collectionProjection.LambdaBody,
                        projectedTypeTemplate,
                        collectionProjection.ProjectionMethodName,
                        cancellationToken
                    );
                }

                if (collectionProjection.LambdaBody is ObjectCreationExpressionSyntax namedBody)
                {
                    foreach (var nestedMember in GetProjectionMembers(namedBody))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        AnalyzeMemberType(
                            nestedMember.Expression,
                            nestedMember.Name,
                            replacementTypes,
                            namedContext: true,
                            defaultNamespace,
                            useGlobalNamespaceFallback,
                            ownerHintName,
                            cancellationToken
                        );
                    }

                    var elementType = _semanticModel
                        .GetTypeInfo(namedBody.Type, cancellationToken)
                        .Type;
                    projectedTypeTemplate =
                        ResolveNamedType(
                            namedBody.Type,
                            _semanticModel,
                            defaultNamespace,
                            cancellationToken
                        )
                        ?? elementType?.ToFullyQualifiedTypeName()
                        ?? namedBody.Type.ToString();
                    return BuildProjectedResultType(
                        expression,
                        collectionProjection.ExpressionType,
                        collectionProjection.LambdaBody,
                        projectedTypeTemplate,
                        collectionProjection.ProjectionMethodName,
                        cancellationToken
                    );
                }

                projectedTypeTemplate = AnalyzeMemberType(
                    collectionProjection.LambdaBody,
                    memberName,
                    replacementTypes,
                    namedContext,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName,
                    cancellationToken
                );
                return BuildProjectedResultType(
                    expression,
                    collectionProjection.ExpressionType,
                    collectionProjection.LambdaBody,
                    projectedTypeTemplate,
                    collectionProjection.ProjectionMethodName,
                    cancellationToken
                );
            }

            var type =
                _semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType
                ?? _semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            return type?.ToFullyQualifiedTypeName() ?? "object";
        }

        private string CreateValueTemplate(
            ExpressionSyntax expression,
            string rootTypeName,
            bool useEmptyCollectionFallback,
            IReadOnlyDictionary<TextSpan, string> replacementTypes,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            var emitter = new ProjectionExpressionEmitter(
                _semanticModel,
                expression,
                rootTypeName,
                useEmptyCollectionFallback,
                _generatorOptions,
                replacementTypes,
                _captureEntries
            );
            return emitter.Emit(expression, cancellationToken);
        }

        private static void MergeReplacementTypes(
            IDictionary<TextSpan, string> target,
            IReadOnlyDictionary<TextSpan, string> source,
            CancellationToken cancellationToken = default
        )
        {
            foreach (var replacement in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                target[replacement.Key] = replacement.Value;
            }
        }

        private GeneratedDtoTemplateModel CreateNestedDtoTemplate(
            string memberName,
            AnonymousObjectCreationExpressionSyntax syntax,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            var dtoBaseName = memberName.EndsWith("Dto", StringComparison.Ordinal)
                ? memberName
                : $"{memberName}Dto";
            var signature =
                $"{defaultNamespace}|{dtoBaseName}|{CreateAnonymousObjectSignature(syntax, cancellationToken)}";
            var hash = HashingHelper.ComputeHash(signature, 8, cancellationToken);
            var templateId = $"nested:{defaultNamespace}|{dtoBaseName}|{hash}";
            return new GeneratedDtoTemplateModel
            {
                TemplateId = templateId,
                PlaceholderToken = CreateDtoPlaceholderToken(
                    templateId,
                    _generatorOptions,
                    cancellationToken
                ),
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
                Origins = new[] { CreateOrigin(syntax) },
                ShapeSignature = signature,
                ContainingTypes = Array.Empty<ContainingTypeInfo>(),
                Properties = Array.Empty<GeneratedPropertyTemplateModel>(),
            };
        }

        private string CreateAnonymousObjectSignature(
            AnonymousObjectCreationExpressionSyntax syntax,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            var typeInfo = _semanticModel.GetTypeInfo(syntax, cancellationToken);
            var rootType = typeInfo.ConvertedType ?? typeInfo.Type;
            var rootTypeName =
                rootType is null || rootType is IErrorTypeSymbol || rootType.IsAnonymousType
                    ? "object"
                    : rootType.ToFullyQualifiedTypeName();
            var emitter = new ProjectionExpressionEmitter(
                _semanticModel,
                syntax,
                rootTypeName,
                useEmptyCollectionFallback: false,
                _generatorOptions
            );
            return emitter.Emit(syntax, cancellationToken);
        }

        private bool TryGetCollectionProjection(
            ExpressionSyntax expression,
            out CollectionProjectionInfo projectionInfo,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            if (!TryFindProjectionInvocation(expression, _generatorOptions, out var invocation))
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
                    _semanticModel.GetTypeInfo(expression, cancellationToken).Type
                    ?? _semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType,
                ProjectionMethodName = GetInvocationName(invocation.Expression),
                IsNestedExplicitDto =
                    IsSelectExprInvocation(invocation, _generatorOptions)
                    && genericArgs.Count >= 2
                    && body is AnonymousObjectCreationExpressionSyntax,
                NestedResultTypeSyntax = genericArgs.Count >= 2 ? genericArgs[1] : null,
                SourceTypeSyntax = genericArgs.Count >= 1 ? genericArgs[0] : null,
            };
            return true;
        }

        private bool QualifiesForCollectionNullabilityRemoval(
            ExpressionSyntax expression,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
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
                    .Any(invocation => IsProjectionInvocation(invocation, _generatorOptions))
            )
            {
                return false;
            }

            var type = _semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            type ??= _semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType;
            return type is not null
                && type.SpecialType != SpecialType.System_String
                && SymbolNameHelper.IsEnumerable(type);
        }

        private string BuildProjectedResultType(
            ExpressionSyntax expression,
            ITypeSymbol? expressionType,
            ExpressionSyntax lambdaBody,
            string projectedTypeTemplate,
            string projectionMethodName,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            var effectiveTypeName =
                projectionMethodName == "SelectMany"
                    ? UnwrapProjectedTypeName(
                        projectedTypeTemplate,
                        _semanticModel.GetTypeInfo(lambdaBody, cancellationToken).Type
                            ?? _semanticModel
                                .GetTypeInfo(lambdaBody, cancellationToken)
                                .ConvertedType,
                        cancellationToken
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

        private static GeneratedDtoTemplateModel MergeDtoTemplate(
            GeneratedDtoTemplateModel existing,
            GeneratedDtoTemplateModel incoming,
            CancellationToken cancellationToken = default
        )
        {
            var merged = existing.Properties.ToDictionary(
                property => property.Name,
                StringComparer.Ordinal
            );
            foreach (var property in incoming.Properties)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                Origins = MergeOrigins(existing.Origins, incoming.Origins),
                ShapeSignature =
                    $"{existing.TemplateId}|{string.Join(";", mergedProperties.Select(property => $"{property.Name}:{property.TypeTemplate}:{property.FallbackTypeTemplate}:{property.IsSuppressed}"))}",
            };
        }

        private static IReadOnlyList<(
            string Name,
            ExpressionSyntax Expression
        )> GetProjectionMembers(ExpressionSyntax expression)
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

        private static string ApplyExpressionNullability(
            string typeName,
            ITypeSymbol? expressionType
        )
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
                InvocationExpressionSyntax invocation => GetInvocationName(
                    invocation.Expression
                ) switch
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
                ConditionalExpressionSyntax conditionalExpression =>
                    IsCollectionLikeResultExpression(conditionalExpression.WhenTrue)
                        && IsCollectionLikeResultExpression(conditionalExpression.WhenFalse),
                BinaryExpressionSyntax binaryExpression
                    when binaryExpression.IsKind(SyntaxKind.CoalesceExpression) =>
                    IsCollectionLikeResultExpression(binaryExpression.Left)
                        || IsCollectionLikeResultExpression(binaryExpression.Right),
                _ => false,
            };
        }

        private static string UnwrapProjectedTypeName(
            string projectedTypeName,
            ITypeSymbol? lambdaBodyType,
            CancellationToken cancellationToken = default
        )
        {
            if (lambdaBodyType is IArrayTypeSymbol arrayType)
            {
                return
                    arrayType.ElementType.IsAnonymousType
                    && TryGetSingleGenericArgument(
                        projectedTypeName,
                        out var parsedArrayElement,
                        cancellationToken
                    )
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
                    && TryGetSingleGenericArgument(
                        projectedTypeName,
                        out var parsedElement,
                        cancellationToken
                    )
                    ? parsedElement
                    : elementType.ToFullyQualifiedTypeName();
            }

            return TryGetSingleGenericArgument(
                projectedTypeName,
                out var parsedGenericArgument,
                cancellationToken
            )
                ? parsedGenericArgument
                : projectedTypeName;
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

        private static EquatableArray<GeneratedSourceOriginModel> MergeOrigins(
            EquatableArray<GeneratedSourceOriginModel> existing,
            EquatableArray<GeneratedSourceOriginModel> incoming
        )
        {
            return existing
                .Concat(incoming)
                .Distinct()
                .OrderBy(origin => origin.FileName, StringComparer.Ordinal)
                .ThenBy(origin => origin.LineNumber)
                .ToArray();
        }

        private static bool TryGetSingleGenericArgument(
            string typeName,
            out string genericArgument,
            CancellationToken cancellationToken = default
        )
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

            genericArgument = candidate;
            return true;
        }

        private static string GetNullableSuffix(NullableAnnotation nullableAnnotation)
        {
            return nullableAnnotation == NullableAnnotation.Annotated ? "?" : string.Empty;
        }
    }

    private static GeneratedDtoTemplateModel CreateRootDtoTemplate(
        TypeSyntax resultTypeSyntax,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? sourceType,
        string ownerHintName,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
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
                : ResolveCallerNamespace(invocation, semanticModel, cancellationToken);
        var dtoName = resultType?.Name ?? GetUnqualifiedTypeName(resultTypeSyntax);
        var containingTypes = GetContainingTypes(resultType, cancellationToken);
        var containingTypeKey =
            containingTypes.Length == 0
                ? string.Empty
                : string.Join(".", containingTypes.Select(type => type.Name));
        var templateId = $"root:{namespaceName}|{containingTypeKey}|{dtoName}";
        var existingProperties =
            resultType
                ?.GetMembers()
                .OfType<IPropertySymbol>()
                .GroupBy(property => property.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<IPropertySymbol>();

        return new GeneratedDtoTemplateModel
        {
            TemplateId = templateId,
            PlaceholderToken = CreateDtoPlaceholderToken(
                templateId,
                generatorOptions,
                cancellationToken
            ),
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
                LinqraftCommentOutput.All,
                cancellationToken
            ),
            OwnerHintName = ownerHintName,
            Origins = new[] { CreateOrigin(invocation) },
            ShapeSignature = templateId,
            ContainingTypes = containingTypes,
            Properties = existingProperties
                .Select(property =>
                {
                    var typeTemplate = property.Type is null or IErrorTypeSymbol
                        ? "object"
                        : property.Type.ToFullyQualifiedTypeName();
                    return new GeneratedPropertyTemplateModel
                    {
                        Name = property.Name,
                        TypeTemplate = typeTemplate,
                        FallbackTypeTemplate = typeTemplate,
                        Documentation = null,
                        IsSuppressed = true,
                    };
                })
                .ToArray(),
        };
    }

    private static CaptureAnalysisResult AnalyzeCaptures(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var captureArgument = GetCaptureArgument(invocation, generatorOptions);
        if (captureArgument?.Expression is null)
        {
            return CaptureAnalysisResult.None;
        }

        if (captureArgument.Expression is AnonymousObjectCreationExpressionSyntax anonymousCapture)
        {
            return new CaptureAnalysisResult
            {
                TransportKind = CaptureTransportKind.AnonymousObject,
                TransportTypeName = null,
                Entries = anonymousCapture
                    .Initializers.Select(
                        (initializer, index) =>
                            CreateCaptureEntry(
                                GetAnonymousMemberName(initializer),
                                initializer.Expression,
                                index,
                                semanticModel,
                                cancellationToken
                            )
                    )
                    .ToArray(),
            };
        }

        if (captureArgument.Expression is LambdaExpressionSyntax captureLambda)
        {
            var body = GetLambdaBodyExpression(captureLambda);
            if (body is null)
            {
                return CaptureAnalysisResult.None;
            }

            var entries = body is TupleExpressionSyntax tuple
                ? tuple
                    .Arguments.Select(
                        (argument, index) =>
                            CreateCaptureEntry(
                                GetCaptureMemberName(argument.Expression),
                                argument.Expression,
                                index,
                                semanticModel,
                                cancellationToken,
                                $"Item{index + 1}"
                            )
                    )
                    .ToArray()
                :
                [
                    CreateCaptureEntry(
                        GetCaptureMemberName(body),
                        body,
                        0,
                        semanticModel,
                        cancellationToken
                    ),
                ];
            var bodyType =
                semanticModel.GetTypeInfo(body, cancellationToken).Type
                ?? semanticModel.GetTypeInfo(body, cancellationToken).ConvertedType;

            return new CaptureAnalysisResult
            {
                TransportKind = CaptureTransportKind.Delegate,
                TransportTypeName = bodyType?.ToFullyQualifiedTypeName() ?? "object",
                Entries = entries,
            };
        }

        return CaptureAnalysisResult.None;
    }

    private static GeneratedSourceOriginModel CreateOrigin(SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        var path = string.IsNullOrWhiteSpace(lineSpan.Path)
            ? node.SyntaxTree.FilePath
            : lineSpan.Path;
        return new GeneratedSourceOriginModel
        {
            FileName = string.IsNullOrWhiteSpace(path) ? "unknown" : Path.GetFileName(path),
            LineNumber = lineSpan.StartLinePosition.Line + 1,
        };
    }

    private static string CreateDtoPlaceholderToken(
        string templateId,
        LinqraftGeneratorOptionsCore generatorOptions,
        CancellationToken cancellationToken = default
    )
    {
        return $"{generatorOptions.DtoPlaceholderPrefix}_{HashingHelper.ComputeHash(templateId, 16, cancellationToken)}__";
    }

    private static bool TryFindProjectionInvocation(
        ExpressionSyntax expression,
        LinqraftGeneratorOptionsCore generatorOptions,
        out InvocationExpressionSyntax invocation
    )
    {
        switch (expression)
        {
            case ConditionalExpressionSyntax conditionalExpression:
                if (
                    TryFindProjectionInvocation(
                        conditionalExpression.WhenTrue,
                        generatorOptions,
                        out invocation
                    )
                )
                {
                    return true;
                }

                return TryFindProjectionInvocation(
                    conditionalExpression.WhenFalse,
                    generatorOptions,
                    out invocation
                );
            case BinaryExpressionSyntax binaryExpression
                when binaryExpression.IsKind(SyntaxKind.CoalesceExpression):
                if (
                    TryFindProjectionInvocation(
                        binaryExpression.Left,
                        generatorOptions,
                        out invocation
                    )
                )
                {
                    return true;
                }

                return TryFindProjectionInvocation(
                    binaryExpression.Right,
                    generatorOptions,
                    out invocation
                );
            case InvocationExpressionSyntax directInvocation
                when IsProjectionInvocation(directInvocation, generatorOptions):
                invocation = directInvocation;
                return true;
            case InvocationExpressionSyntax nestedInvocation
                when nestedInvocation.Expression is MemberAccessExpressionSyntax memberAccess:
                return TryFindProjectionInvocation(
                    memberAccess.Expression,
                    generatorOptions,
                    out invocation
                );
            case MemberAccessExpressionSyntax memberAccess:
                return TryFindProjectionInvocation(
                    memberAccess.Expression,
                    generatorOptions,
                    out invocation
                );
            case ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull
                    is InvocationExpressionSyntax whenNotNullInvocation
                    && IsProjectionInvocation(whenNotNullInvocation, generatorOptions):
                invocation = whenNotNullInvocation;
                return true;
            case ConditionalAccessExpressionSyntax conditionalAccess:
                if (
                    conditionalAccess.WhenNotNull is ExpressionSyntax whenNotNull
                    && TryFindProjectionInvocation(whenNotNull, generatorOptions, out invocation)
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

    private static bool IsProjectionInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var name = GetInvocationName(invocation.Expression);
        return name == "Select"
            || name == "SelectMany"
            || name == generatorOptions.SelectExprMethodName
            || name == generatorOptions.SelectManyExprMethodName
            || name == generatorOptions.GroupByExprMethodName;
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
        SeparatedSyntaxList<TypeSyntax> typeArguments,
        CancellationToken cancellationToken = default
    )
    {
        if (typeArguments.Count >= 2)
        {
            return semanticModel.GetTypeInfo(typeArguments[0], cancellationToken).Type;
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

    private static bool IsCaptureExpression(ExpressionSyntax expression)
    {
        return expression is AnonymousObjectCreationExpressionSyntax or LambdaExpressionSyntax;
    }

    private static ArgumentSyntax? GetCaptureArgument(
        InvocationExpressionSyntax invocation,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var namedCapture = invocation.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameColon?.Name.Identifier.ValueText == "capture"
        );
        if (namedCapture is not null)
        {
            return namedCapture;
        }

        var captureIndex = GetPositionalCaptureArgumentIndex(invocation, generatorOptions);
        if (
            captureIndex is not int index
            || index < 0
            || index >= invocation.ArgumentList.Arguments.Count
        )
        {
            return null;
        }

        var argument = invocation.ArgumentList.Arguments[index];
        return IsCaptureExpression(argument.Expression) ? argument : null;
    }

    private static int? GetPositionalCaptureArgumentIndex(
        InvocationExpressionSyntax invocation,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        return GetInvocationName(invocation.Expression) switch
        {
            var name when name == generatorOptions.ObjectGenerationMethodName => 1,
            var name when name == generatorOptions.SelectExprMethodName => 1,
            var name when name == generatorOptions.SelectManyExprMethodName => 1,
            var name when name == generatorOptions.GroupByExprMethodName => 2,
            _ => null,
        };
    }

    private static string GetCaptureMemberName(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => NormalizeExpressionText(expression),
        };
    }

    private static ProjectionExpressionEmitter.CaptureEntry CreateCaptureEntry(
        string propertyName,
        ExpressionSyntax expression,
        int index,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? valueAccessor = null
    )
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        return new ProjectionExpressionEmitter.CaptureEntry
        {
            PropertyName = propertyName,
            LocalName = CreateCaptureLocalName(propertyName, index),
            TypeName = type?.ToFullyQualifiedTypeName() ?? "object",
            ExpressionText = NormalizeExpressionText(expression),
            RootSymbol = GetCaptureRootSymbol(expression, semanticModel, cancellationToken),
            ValueAccessor = valueAccessor,
        };
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

    private sealed record CaptureAnalysisResult
    {
        public static CaptureAnalysisResult None { get; } =
            new()
            {
                TransportKind = CaptureTransportKind.AnonymousObject,
                TransportTypeName = null,
                Entries = Array.Empty<ProjectionExpressionEmitter.CaptureEntry>(),
            };

        public required CaptureTransportKind TransportKind { get; init; }

        public string? TransportTypeName { get; init; }

        public required ProjectionExpressionEmitter.CaptureEntry[] Entries { get; init; }
    }

    private static string ResolveCallerNamespace(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default
    )
    {
        var symbol = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken);
        return SymbolNameHelper.GetNamespace(symbol?.ContainingNamespace);
    }

    private static string? ResolveNamedType(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        string fallbackNamespace,
        CancellationToken cancellationToken = default
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(typeSyntax, cancellationToken);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        if (type is IErrorTypeSymbol)
        {
            type = null;
        }

        if (type is null)
        {
            type = semanticModel.GetSymbolInfo(typeSyntax, cancellationToken).Symbol switch
            {
                ITypeSymbol typeSymbol when typeSymbol is not IErrorTypeSymbol => typeSymbol,
                IAliasSymbol aliasSymbol
                    when aliasSymbol.Target is ITypeSymbol typeSymbol
                        && typeSymbol is not IErrorTypeSymbol => typeSymbol,
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
                .FirstOrDefault(candidate => candidate is not IErrorTypeSymbol);
        }

        if (type is not null)
        {
            return type.ToFullyQualifiedTypeName();
        }

        if (TryResolveSyntacticallyQualifiedTypeName(typeSyntax, out var qualifiedTypeName))
        {
            return qualifiedTypeName;
        }

        var name = GetUnqualifiedTypeName(typeSyntax);
        return string.IsNullOrWhiteSpace(fallbackNamespace)
            ? $"global::{name}"
            : $"global::{fallbackNamespace}.{name}";
    }

    private static bool TryResolveSyntacticallyQualifiedTypeName(
        TypeSyntax typeSyntax,
        out string qualifiedTypeName
    )
    {
        qualifiedTypeName = typeSyntax.WithoutTrivia().ToString();
        if (string.IsNullOrWhiteSpace(qualifiedTypeName))
        {
            return false;
        }

        if (qualifiedTypeName.StartsWith("global::", StringComparison.Ordinal))
        {
            return true;
        }

        if (typeSyntax is QualifiedNameSyntax or AliasQualifiedNameSyntax)
        {
            qualifiedTypeName = $"global::{qualifiedTypeName}";
            return true;
        }

        qualifiedTypeName = string.Empty;
        return false;
    }

    private static string GetMappingGeneratedAccessibilityKeyword(
        Accessibility declaredAccessibility,
        InvocationExpressionSyntax selectExpr,
        SemanticModel semanticModel,
        string? requestedVisibilityKeyword,
        CancellationToken cancellationToken = default
    )
    {
        var nameSyntax = GetInvocationNameSyntax(selectExpr.Expression) as GenericNameSyntax;
        if (nameSyntax?.TypeArgumentList.Arguments.Count >= 2)
        {
            var sourceType = semanticModel
                .GetTypeInfo(nameSyntax.TypeArgumentList.Arguments[0], cancellationToken)
                .Type;
            var resultType = semanticModel
                .GetTypeInfo(nameSyntax.TypeArgumentList.Arguments[1], cancellationToken)
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
        InvocationExpressionSyntax invocation,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        return GetInvocationName(invocation.Expression) switch
        {
            var name when name == generatorOptions.SelectExprMethodName =>
                ProjectionOperationKind.Select,
            var name when name == generatorOptions.SelectManyExprMethodName =>
                ProjectionOperationKind.SelectMany,
            var name when name == generatorOptions.GroupByExprMethodName =>
                ProjectionOperationKind.GroupBy,
            _ => null,
        };
    }

    private static bool IsGenerateInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        if (generatorOptions.GeneratorKitMetadataName is not { } generatorKitMetadataName)
        {
            return false;
        }

        if (
            !string.Equals(
                GetInvocationName(invocation.Expression),
                generatorOptions.ObjectGenerationMethodName,
                StringComparison.Ordinal
            )
        )
        {
            return false;
        }

        var methodSymbol =
            semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        if (methodSymbol is null)
        {
            return false;
        }

        var targetMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        return string.Equals(
                targetMethod.ContainingType.ToDisplayString(),
                generatorKitMetadataName,
                StringComparison.Ordinal
            )
            && string.Equals(
                targetMethod.Name,
                generatorOptions.ObjectGenerationMethodName,
                StringComparison.Ordinal
            );
    }

    private static bool IsSelectExprInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        return string.Equals(
            GetInvocationName(invocation.Expression),
            generatorOptions.SelectExprMethodName,
            StringComparison.Ordinal
        );
    }

    private static bool IsInsideMappingDeclaration(
        SyntaxNode node,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        if (generatorOptions.MappingGenerateAttributeName is not { } mappingGenerateAttributeName)
        {
            return false;
        }

        return node.Ancestors()
            .OfType<MemberDeclarationSyntax>()
            .Any(member =>
                member
                    .AttributeLists.SelectMany(list => list.Attributes)
                    .Any(attribute =>
                        attribute
                            .Name.ToString()
                            .Contains(
                                mappingGenerateAttributeName.Replace("Attribute", string.Empty),
                                StringComparison.Ordinal
                            )
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

    private static bool InheritsFromMappingDeclare(
        INamedTypeSymbol symbol,
        LinqraftGeneratorOptionsCore generatorOptions,
        CancellationToken cancellationToken = default
    )
    {
        if (generatorOptions.MappingDeclareClassName is not { } mappingDeclareClassName)
        {
            return false;
        }

        var current = symbol.BaseType;
        while (current is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Name == mappingDeclareClassName)
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static AttributeData? GetMappingGenerateAttribute(
        ImmutableArray<AttributeData> attributes,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        if (generatorOptions.MappingGenerateAttributeName is not { } mappingGenerateAttributeName)
        {
            return null;
        }

        return attributes.FirstOrDefault(attribute =>
            attribute.AttributeClass?.Name == mappingGenerateAttributeName
        );
    }

    private static string? GetMappingMethodName(AttributeData? attribute)
    {
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static string? GetMappingVisibilityKeyword(
        AttributeData? attribute,
        CancellationToken cancellationToken = default
    )
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static ContainingTypeInfo[] GetContainingTypes(
        INamedTypeSymbol? symbol,
        CancellationToken cancellationToken = default
    )
    {
        if (symbol?.ContainingType is null)
        {
            return Array.Empty<ContainingTypeInfo>();
        }

        var stack = new Stack<ContainingTypeInfo>();
        var current = symbol.ContainingType;
        while (current is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            _ => typeSyntax.ToString().Split('.')[^1],
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
