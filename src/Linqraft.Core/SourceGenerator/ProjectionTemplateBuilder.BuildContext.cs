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
    // ProjectionBuildContext keeps semantic state and deduplicates DTO templates while a selector is analyzed.
    /// <summary>
    /// Tracks per-selector state while projection templates and generated DTOs are assembled.
    /// </summary>
    private sealed class ProjectionBuildContext
    {
        private readonly SemanticModel _semanticModel;
        private readonly IReadOnlyList<ProjectionExpressionEmitter.CaptureEntry> _captureEntries;
        private readonly CancellationToken _cancellationToken;
        private readonly LinqraftGeneratorOptionsCore _generatorOptions;
        private readonly string? _selectorParameterName;
        private readonly ITypeSymbol? _selectorParameterType;
        private readonly string? _projectionHelperParameterName;
        private readonly string? _projectionHelperParameterTypeName;
        private readonly Dictionary<string, GeneratedDtoTemplateModel> _generatedDtos = new(
            StringComparer.Ordinal
        );

        /// <summary>
        /// Initializes a new instance of the ProjectionBuildContext class.
        /// </summary>
        public ProjectionBuildContext(
            SemanticModel semanticModel,
            IReadOnlyList<ProjectionExpressionEmitter.CaptureEntry> captureEntries,
            CancellationToken cancellationToken,
            LinqraftGeneratorOptionsCore generatorOptions,
            string? selectorParameterName,
            ITypeSymbol? selectorParameterType,
            string? projectionHelperParameterName,
            string? projectionHelperParameterTypeName
        )
        {
            _semanticModel = semanticModel;
            _captureEntries = captureEntries;
            _cancellationToken = cancellationToken;
            _generatorOptions = generatorOptions;
            _selectorParameterName = selectorParameterName;
            _selectorParameterType = selectorParameterType;
            _projectionHelperParameterName = projectionHelperParameterName;
            _projectionHelperParameterTypeName = projectionHelperParameterTypeName;
        }

        public LinqraftGeneratorOptionsCore GeneratorOptions => _generatorOptions;

        /// <summary>
        /// Resolves cancellation token.
        /// </summary>
        private CancellationToken ResolveCancellationToken(CancellationToken cancellationToken)
        {
            return cancellationToken == default ? _cancellationToken : cancellationToken;
        }

        /// <summary>
        /// Builds a projection template and any DTO shape information for the supplied selector body.
        /// </summary>
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

            // Each projected member is normalized into a reusable type/value template pair before
            // the optional DTO shape is materialized from the same analysis result.
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

        /// <summary>
        /// Analyzes standalone type.
        /// </summary>
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

        /// <summary>
        /// Creates standalone body template.
        /// </summary>
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

        /// <summary>
        /// Creates inner join filter template.
        /// </summary>
        public string? CreateInnerJoinFilterTemplate(
            ExpressionSyntax expression,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            var conditions = new List<string>();
            var seenConditions = new HashSet<string>(StringComparer.Ordinal);
            foreach (
                var invocation in expression
                    .DescendantNodesAndSelf(descendIntoChildren: node =>
                        node is not LambdaExpressionSyntax
                    )
                    .OfType<InvocationExpressionSyntax>()
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    !ProjectionHookSyntaxHelper.TryGetHookInvocation(
                        invocation,
                        _semanticModel,
                        _generatorOptions,
                        _projectionHelperParameterName,
                        _projectionHelperParameterTypeName,
                        LinqraftProjectionHookKind.InnerJoin,
                        out var hookInvocation,
                        cancellationToken
                    )
                )
                {
                    continue;
                }

                var targetType = _semanticModel
                    .GetTypeInfo(hookInvocation.TargetExpression, cancellationToken)
                    .Type;
                if (
                    targetType is { IsValueType: true }
                    && targetType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                )
                {
                    continue;
                }

                var targetTypeName = targetType?.ToFullyQualifiedTypeName() ?? "object";
                var condition =
                    $"{CreateValueTemplate(
                    hookInvocation.TargetExpression,
                    targetTypeName,
                    useEmptyCollectionFallback: false,
                    new Dictionary<TextSpan, string>(),
                    cancellationToken
                )} != null";
                if (seenConditions.Add(condition))
                {
                    conditions.Add(condition);
                }
            }

            return conditions.Count switch
            {
                0 => null,
                1 => conditions[0],
                _ => string.Join(" && ", conditions),
            };
        }

        /// <summary>
        /// Registers dto.
        /// </summary>
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

        /// <summary>
        /// Gets generated dtos.
        /// </summary>
        public EquatableArray<GeneratedDtoTemplateModel> GetGeneratedDtos()
        {
            return _generatedDtos
                .Values.OrderByDescending(dto => dto.IsRoot)
                .ThenBy(dto => dto.TemplateId, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Analyzes member type.
        /// </summary>
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
                TryAnalyzeAsProjection(
                    expression,
                    namedContext,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName,
                    replacementTypes,
                    out var asProjectionType,
                    cancellationToken
                )
            )
            {
                return asProjectionType;
            }

            if (
                TryAnalyzeProjectedValueSelection(
                    expression,
                    memberName,
                    namedContext,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName,
                    replacementTypes,
                    out var projectedValueSelectionType,
                    cancellationToken
                )
            )
            {
                return projectedValueSelectionType;
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

            if (
                expression is ConditionalAccessExpressionSyntax conditionalAccess
                && TryResolveConditionalAccessTypeName(conditionalAccess, cancellationToken)
                    is { } conditionalAccessType
            )
            {
                return conditionalAccessType;
            }

            var type = GetExpressionType(expression, cancellationToken);
            return type?.ToFullyQualifiedTypeName() ?? "object";
        }

        /// <summary>
        /// Creates value template.
        /// </summary>
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
                _projectionHelperParameterName,
                _projectionHelperParameterTypeName,
                replacementTypes,
                _captureEntries
            );
            return emitter.Emit(expression, cancellationToken);
        }

        /// <summary>
        /// Attempts to analyze as projection.
        /// </summary>
        private bool TryAnalyzeAsProjection(
            ExpressionSyntax expression,
            bool namedContext,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName,
            Dictionary<TextSpan, string> replacementTypes,
            out string typeTemplate,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            typeTemplate = string.Empty;
            if (
                expression is not InvocationExpressionSyntax invocation
                || !ProjectionHookSyntaxHelper.TryGetHookInvocation(
                    invocation,
                    _semanticModel,
                    _generatorOptions,
                    _projectionHelperParameterName,
                    _projectionHelperParameterTypeName,
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

            if (!namedContext && hookInvocation.GenericTypeArgument is null)
            {
                typeTemplate = "object";
                return true;
            }

            GeneratedDtoTemplateModel dtoTemplate;
            HashSet<string> existingPropertyNames;
            if (hookInvocation.GenericTypeArgument is { } explicitResultType)
            {
                dtoTemplate = CreateRootDtoTemplate(
                    explicitResultType,
                    _semanticModel,
                    invocation,
                    targetType,
                    ownerHintName,
                    cancellationToken,
                    _generatorOptions
                );
                existingPropertyNames = new HashSet<string>(
                    dtoTemplate
                        .Properties.Where(property => property.IsSuppressed)
                        .Select(property => property.Name),
                    StringComparer.Ordinal
                );
            }
            else
            {
                dtoTemplate = CreateGeneratedNamedDtoTemplate(
                    CreateProjectionDtoName(targetType.Name),
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName,
                    invocation,
                    targetType,
                    cancellationToken
                );
                existingPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            }

            replacementTypes[expression.Span] = dtoTemplate.PlaceholderToken;
            var properties = CreateProjectionProperties(targetType, existingPropertyNames);
            RegisterDto(
                dtoTemplate with
                {
                    Properties = properties,
                    ShapeSignature = BuildGeneratedDtoShapeSignature(
                        dtoTemplate.TemplateId,
                        properties
                    ),
                },
                cancellationToken
            );
            typeTemplate = ApplyExpressionNullability(
                dtoTemplate.PlaceholderToken,
                GetExpressionType(hookInvocation.TargetExpression, cancellationToken)
            );
            return true;
        }

        /// <summary>
        /// Attempts to analyze projected value selection.
        /// </summary>
        private bool TryAnalyzeProjectedValueSelection(
            ExpressionSyntax expression,
            string memberName,
            bool namedContext,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName,
            Dictionary<TextSpan, string> replacementTypes,
            out string typeTemplate,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            typeTemplate = string.Empty;
            if (
                expression is not InvocationExpressionSyntax invocation
                || !ProjectionHookSyntaxHelper.TryGetProjectedValueSelection(
                    invocation,
                    _semanticModel,
                    _generatorOptions,
                    _projectionHelperParameterName,
                    _projectionHelperParameterTypeName,
                    out var selectionInfo,
                    cancellationToken
                )
            )
            {
                return false;
            }

            var targetExpressionType = GetExpressionType(
                selectionInfo.ProjectInvocation.TargetExpression,
                cancellationToken
            );
            var selectorBody = selectionInfo.SelectorBody;
            if (selectorBody is AnonymousObjectCreationExpressionSyntax anonymousBody)
            {
                if (!namedContext && selectionInfo.ProjectInvocation.GenericTypeArgument is null)
                {
                    var inferredAnonymousType = _semanticModel
                        .GetTypeInfo(anonymousBody, cancellationToken)
                        .Type;
                    typeTemplate = ApplyExpressionNullability(
                        inferredAnonymousType?.ToFullyQualifiedTypeName() ?? "object",
                        targetExpressionType
                    );
                    return true;
                }

                GeneratedDtoTemplateModel dtoTemplate;
                if (selectionInfo.ProjectInvocation.GenericTypeArgument is { } nameHintType)
                {
                    dtoTemplate = CreateGeneratedNamedDtoTemplate(
                        CreateProjectionDtoName(GetUnqualifiedTypeName(nameHintType)),
                        defaultNamespace,
                        useGlobalNamespaceFallback,
                        ownerHintName,
                        selectionInfo.SelectionInvocation,
                        GetProjectionSourceType(
                            selectionInfo.ProjectInvocation.TargetExpression,
                            cancellationToken
                        ),
                        cancellationToken
                    );
                }
                else
                {
                    dtoTemplate = CreateNestedDtoTemplate(
                        memberName,
                        anonymousBody,
                        defaultNamespace,
                        useGlobalNamespaceFallback,
                        ownerHintName,
                        cancellationToken
                    );
                }

                replacementTypes[anonymousBody.Span] = dtoTemplate.PlaceholderToken;
                var buildResult = BuildProjectionTemplate(
                    anonymousBody,
                    dtoTemplate.PlaceholderToken,
                    dtoTemplate,
                    existingPropertyNames: null,
                    namedContext: true,
                    defaultNamespace: dtoTemplate.PreferredNamespace,
                    useGlobalNamespaceFallback: dtoTemplate.UseGlobalNamespaceFallback,
                    ownerHintName: ownerHintName,
                    cancellationToken: cancellationToken
                );
                MergeReplacementTypes(
                    replacementTypes,
                    buildResult.ReplacementTypes,
                    cancellationToken
                );
                RegisterDto(buildResult.DtoTemplate!, cancellationToken);
                typeTemplate = ApplyExpressionNullability(
                    dtoTemplate.PlaceholderToken,
                    targetExpressionType
                );
                return true;
            }

            if (selectorBody is ObjectCreationExpressionSyntax namedBody)
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

                var namedBodyType =
                    ResolveNamedType(
                        namedBody.Type,
                        _semanticModel,
                        defaultNamespace,
                        cancellationToken
                    )
                    ?? _semanticModel
                        .GetTypeInfo(namedBody.Type, cancellationToken)
                        .Type?.ToFullyQualifiedTypeName()
                    ?? namedBody.Type.ToString();
                typeTemplate = ApplyExpressionNullability(namedBodyType, targetExpressionType);
                return true;
            }

            typeTemplate = ApplyExpressionNullability(
                AnalyzeMemberType(
                    selectorBody,
                    memberName,
                    replacementTypes,
                    namedContext,
                    defaultNamespace,
                    useGlobalNamespaceFallback,
                    ownerHintName,
                    cancellationToken
                ),
                targetExpressionType
            );
            return true;
        }

        /// <summary>
        /// Creates generated named dto template.
        /// </summary>
        private GeneratedDtoTemplateModel CreateGeneratedNamedDtoTemplate(
            string dtoName,
            string defaultNamespace,
            bool useGlobalNamespaceFallback,
            string ownerHintName,
            SyntaxNode origin,
            INamedTypeSymbol? sourceType,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            var templateId = $"root:{defaultNamespace}||{dtoName}";
            return new GeneratedDtoTemplateModel
            {
                TemplateId = templateId,
                PlaceholderToken = CreateDtoPlaceholderToken(
                    templateId,
                    _generatorOptions,
                    cancellationToken
                ),
                Kind = GeneratedDtoTemplateKind.RootExplicit,
                PreferredNamespace = defaultNamespace,
                UseGlobalNamespaceFallback = useGlobalNamespaceFallback,
                Name = dtoName,
                ShapeHash = string.Empty,
                AccessibilityKeyword = IsPubliclyAccessible(sourceType) ? "public" : "internal",
                DeclaredIsRecord = null,
                IsRoot = true,
                IsAutoGeneratedNested = false,
                Documentation = DocumentationExtractor.GetTypeDocumentation(
                    sourceType,
                    LinqraftCommentOutput.All,
                    cancellationToken
                ),
                OwnerHintName = ownerHintName,
                Origins = new[] { CreateOrigin(origin) },
                ShapeSignature = templateId,
                ContainingTypes = Array.Empty<ContainingTypeInfo>(),
                Properties = Array.Empty<GeneratedPropertyTemplateModel>(),
            };
        }

        /// <summary>
        /// Gets projection source type.
        /// </summary>
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
                    when namedType.OriginalDefinition.SpecialType
                        == SpecialType.System_Nullable_T => namedType.TypeArguments[0]
                    as INamedTypeSymbol,
                INamedTypeSymbol namedType => namedType,
                _ => null,
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
            cancellationToken = ResolveCancellationToken(cancellationToken);
            // The packaged runfile path can leave the selector parameter identifier weakly bound even
            // though the intercepted SelectExpr signature already told us the source type. Reusing
            // that known source type keeps member-access recovery anchored to the original selector.
            if (
                expression is IdentifierNameSyntax identifierName
                && _selectorParameterType is not null
                && string.Equals(
                    identifierName.Identifier.ValueText,
                    _selectorParameterName,
                    StringComparison.Ordinal
                )
            )
            {
                return _selectorParameterType;
            }

            // dotnet run <script> + packaged analyzers can surface ErrorType for simple scalar member
            // accesses such as l.Id or l.Data.TestRepoInfo.CommonName. Probe progressively richer
            // semantic APIs before falling back to object so explicit DTO members keep their types.
            var typeInfo = _semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = GetResolvedType(typeInfo.Type) ?? GetResolvedType(typeInfo.ConvertedType);
            if (type is not null)
            {
                return type;
            }

            type = GetResolvedType(
                _semanticModel.GetOperation(expression, cancellationToken)?.Type
            );
            if (type is not null)
            {
                return type;
            }

            return _semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
            {
                IAliasSymbol alias when alias.Target is ITypeSymbol aliasType => GetResolvedType(
                    aliasType
                ),
                IEventSymbol eventSymbol => GetResolvedType(eventSymbol.Type),
                IFieldSymbol fieldSymbol => GetResolvedType(fieldSymbol.Type),
                ILocalSymbol localSymbol => GetResolvedType(localSymbol.Type),
                IMethodSymbol methodSymbol => GetResolvedType(methodSymbol.ReturnType),
                IParameterSymbol parameterSymbol => GetResolvedType(parameterSymbol.Type),
                IPropertySymbol propertySymbol => GetResolvedType(propertySymbol.Type),
                _ when expression is ConditionalAccessExpressionSyntax conditionalAccess =>
                    ResolveConditionalAccessType(conditionalAccess, cancellationToken),
                _ => ResolveMemberAccessType(expression, cancellationToken),
            };
        }

        private static ITypeSymbol? GetResolvedType(ITypeSymbol? type)
        {
            return type is null or IErrorTypeSymbol ? null : type;
        }

        private ITypeSymbol? ResolveMemberAccessType(
            ExpressionSyntax expression,
            CancellationToken cancellationToken
        )
        {
            if (expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return null;
            }

            // Even when the full member access is weakly bound in the packaged script scenario,
            // Roslyn can still sometimes resolve the terminal member name or let us walk the known
            // receiver type. Try both before giving up on the member's declared type.
            var memberSymbol = _semanticModel
                .GetSymbolInfo(memberAccess.Name, cancellationToken)
                .Symbol;
            var memberType = GetMemberType(memberSymbol);
            if (memberType is not null)
            {
                return memberType;
            }

            var receiverType = GetExpressionType(memberAccess.Expression, cancellationToken);
            if (receiverType is null)
            {
                return null;
            }

            var memberName = memberAccess.Name.Identifier.ValueText;
            return ResolveMemberTypeFromReceiver(
                receiverType,
                memberName,
                argumentCount: null,
                cancellationToken
            );
        }

        private string? TryResolveConditionalAccessTypeName(
            ConditionalAccessExpressionSyntax conditionalAccess,
            CancellationToken cancellationToken
        )
        {
            cancellationToken = ResolveCancellationToken(cancellationToken);
            var resolvedType = ResolveConditionalAccessType(conditionalAccess, cancellationToken);
            return resolvedType is null ? null : FormatConditionalAccessTypeName(resolvedType);
        }

        private ITypeSymbol? ResolveConditionalAccessType(
            ConditionalAccessExpressionSyntax conditionalAccess,
            CancellationToken cancellationToken
        )
        {
            var typeInfo = _semanticModel.GetTypeInfo(conditionalAccess, cancellationToken);
            var resolvedType =
                GetResolvedType(typeInfo.Type) ?? GetResolvedType(typeInfo.ConvertedType);
            if (resolvedType is not null)
            {
                return resolvedType;
            }

            return conditionalAccess.WhenNotNull switch
            {
                ConditionalAccessExpressionSyntax nestedConditional => ResolveConditionalAccessType(
                    nestedConditional,
                    cancellationToken
                ),
                MemberBindingExpressionSyntax memberBinding => ResolveConditionalAccessMemberType(
                    conditionalAccess.Expression,
                    memberBinding.Name.Identifier.ValueText,
                    argumentCount: null,
                    cancellationToken
                ),
                InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberBindingExpressionSyntax memberBinding =>
                    ResolveConditionalAccessMemberType(
                        conditionalAccess.Expression,
                        memberBinding.Name.Identifier.ValueText,
                        invocation.ArgumentList.Arguments.Count,
                        cancellationToken
                    ),
                ExpressionSyntax whenNotNullExpression => GetExpressionType(
                    whenNotNullExpression,
                    cancellationToken
                ),
                _ => null,
            };
        }

        private ITypeSymbol? ResolveConditionalAccessMemberType(
            ExpressionSyntax receiverExpression,
            string memberName,
            int? argumentCount,
            CancellationToken cancellationToken
        )
        {
            var receiverType = GetExpressionType(receiverExpression, cancellationToken);
            if (receiverType is null)
            {
                return null;
            }

            return ResolveMemberTypeFromReceiver(
                receiverType,
                memberName,
                argumentCount,
                cancellationToken
            );
        }

        private static ITypeSymbol? ResolveMemberTypeFromReceiver(
            ITypeSymbol receiverType,
            string memberName,
            int? argumentCount,
            CancellationToken cancellationToken
        )
        {
            foreach (var candidate in EnumerateMemberCandidates(receiverType, memberName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (argumentCount is int expectedArgumentCount)
                {
                    if (
                        candidate is IMethodSymbol methodSymbol
                        && methodSymbol.Parameters.Length == expectedArgumentCount
                    )
                    {
                        return GetResolvedType(methodSymbol.ReturnType);
                    }

                    continue;
                }

                var candidateType = GetMemberType(candidate);
                if (candidateType is not null)
                {
                    return candidateType;
                }
            }

            return null;
        }

        private static IEnumerable<ISymbol> EnumerateMemberCandidates(
            ITypeSymbol receiverType,
            string memberName
        )
        {
            foreach (var candidate in receiverType.GetMembers(memberName))
            {
                yield return candidate;
            }

            if (receiverType is not INamedTypeSymbol namedType)
            {
                yield break;
            }

            for (var baseType = namedType.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                foreach (var candidate in baseType.GetMembers(memberName))
                {
                    yield return candidate;
                }
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                foreach (var candidate in interfaceType.GetMembers(memberName))
                {
                    yield return candidate;
                }
            }
        }

        private static string? FormatConditionalAccessTypeName(ITypeSymbol resolvedType)
        {
            if (resolvedType.IsAnonymousType || IsCollectionLikeType(resolvedType))
            {
                return null;
            }

            return
                resolvedType is INamedTypeSymbol namedType
                && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? namedType.ToFullyQualifiedTypeName()
                : MakeNullable(resolvedType.ToFullyQualifiedTypeName());
        }

        private static ITypeSymbol? GetMemberType(ISymbol? symbol)
        {
            return symbol switch
            {
                IEventSymbol eventSymbol => GetResolvedType(eventSymbol.Type),
                IFieldSymbol fieldSymbol => GetResolvedType(fieldSymbol.Type),
                IMethodSymbol methodSymbol when methodSymbol.Parameters.Length == 0 =>
                    GetResolvedType(methodSymbol.ReturnType),
                IPropertySymbol propertySymbol => GetResolvedType(propertySymbol.Type),
                _ => null,
            };
        }

        /// <summary>
        /// Creates projection properties.
        /// </summary>
        private EquatableArray<GeneratedPropertyTemplateModel> CreateProjectionProperties(
            INamedTypeSymbol sourceType,
            ISet<string> existingPropertyNames
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
                .Select(property => new GeneratedPropertyTemplateModel
                {
                    Name = property.Name,
                    TypeTemplate = property.Type.ToFullyQualifiedTypeName(),
                    FallbackTypeTemplate = property.Type.ToFullyQualifiedTypeName(),
                    Documentation = null,
                    IsSuppressed = existingPropertyNames.Contains(property.Name),
                })
                .ToArray();
        }

        /// <summary>
        /// Determines whether the type is a projection leaf type.
        /// </summary>
        private static bool IsProjectionLeafType(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_String
                || type.TypeKind == TypeKind.Enum
                || type.IsValueType;
        }

        /// <summary>
        /// Builds generated dto shape signature.
        /// </summary>
        private static string BuildGeneratedDtoShapeSignature(
            string templateId,
            IEnumerable<GeneratedPropertyTemplateModel> properties
        )
        {
            return $"{templateId}|{string.Join(";", properties.Select(property => $"{property.Name}:{property.TypeTemplate}:{property.FallbackTypeTemplate}:{property.IsSuppressed}"))}";
        }

        /// <summary>
        /// Creates projection dto name.
        /// </summary>
        private static string CreateProjectionDtoName(string name)
        {
            return name.EndsWith("Dto", StringComparison.Ordinal) ? name : $"{name}Dto";
        }

        /// <summary>
        /// Merges replacement types.
        /// </summary>
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

        /// <summary>
        /// Creates nested dto template.
        /// </summary>
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

        /// <summary>
        /// Creates anonymous object signature.
        /// </summary>
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
                _generatorOptions,
                _projectionHelperParameterName,
                _projectionHelperParameterTypeName
            );
            return emitter.Emit(syntax, cancellationToken);
        }

        /// <summary>
        /// Attempts to get collection projection.
        /// </summary>
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
                    IsSelectExprInvocation(
                        invocation,
                        _semanticModel,
                        _generatorOptions,
                        cancellationToken
                    )
                    && genericArgs.Count >= 2
                    && body is AnonymousObjectCreationExpressionSyntax,
                NestedResultTypeSyntax = genericArgs.Count >= 2 ? genericArgs[1] : null,
                SourceTypeSyntax = genericArgs.Count >= 1 ? genericArgs[0] : null,
            };
            return true;
        }

        /// <summary>
        /// Determines whether the expression qualifies for collection nullability removal.
        /// </summary>
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

        /// <summary>
        /// Builds projected result type.
        /// </summary>
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

        /// <summary>
        /// Merges dto template.
        /// </summary>
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

        /// <summary>
        /// Gets projection members.
        /// </summary>
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

        /// <summary>
        /// Builds collection type name.
        /// </summary>
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

        /// <summary>
        /// Applies expression nullability.
        /// </summary>
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

        /// <summary>
        /// Determines whether the type is a collection like type.
        /// </summary>
        private static bool IsCollectionLikeType(ITypeSymbol? type)
        {
            return type is IArrayTypeSymbol || SymbolNameHelper.IsEnumerable(type);
        }

        /// <summary>
        /// Determines whether the expression is a collection like result expression.
        /// </summary>
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

        /// <summary>
        /// Unwraps projected type name.
        /// </summary>
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

        /// <summary>
        /// Removes nullable annotation.
        /// </summary>
        private static string RemoveNullableAnnotation(string typeName)
        {
            return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName[..^1] : typeName;
        }

        /// <summary>
        /// Makes nullable.
        /// </summary>
        private static string MakeNullable(string typeName)
        {
            return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName : $"{typeName}?";
        }

        /// <summary>
        /// Attempts to get null guard branch.
        /// </summary>
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

        /// <summary>
        /// Merges template type.
        /// </summary>
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

        /// <summary>
        /// Merges origins.
        /// </summary>
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

        /// <summary>
        /// Attempts to get single generic argument.
        /// </summary>
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

        /// <summary>
        /// Gets nullable suffix.
        /// </summary>
        private static string GetNullableSuffix(NullableAnnotation nullableAnnotation)
        {
            return nullableAnnotation == NullableAnnotation.Annotated ? "?" : string.Empty;
        }
    }
}
