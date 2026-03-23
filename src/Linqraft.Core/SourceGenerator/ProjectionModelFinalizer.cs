using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Linqraft.Core.Collections;
using Linqraft.Core.Configuration;

namespace Linqraft.SourceGenerator;

internal static class ProjectionModelFinalizer
{
    public static ProjectionGenerationModel FinalizeProjection(
        ProjectionSourceTemplateModel template,
        LinqraftConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        var dtoReplacements = BuildDtoReplacements(
            template.GeneratedDtos,
            configuration,
            cancellationToken
        );
        var replacements = dtoReplacements.ToDictionary(
            replacement => replacement.PlaceholderToken,
            replacement => replacement.Dto.FullyQualifiedName,
            StringComparer.Ordinal
        );
        var requestTemplate = template.Request;
        var resultTypeName = ProjectionBodyEmitter.ReplaceTokens(
            requestTemplate.ResultTypeTemplate,
            replacements,
            cancellationToken
        );

        return new ProjectionGenerationModel
        {
            Request = new ProjectionRequest
            {
                HintName = requestTemplate.HintName,
                MethodName = requestTemplate.MethodName,
                Origin = requestTemplate.Origin,
                OperationKind = requestTemplate.OperationKind,
                ReceiverKind = requestTemplate.ReceiverKind,
                UsesFluentQuerySyntax = requestTemplate.UsesFluentQuerySyntax,
                Pattern = requestTemplate.Pattern,
                SourceTypeName = requestTemplate.SourceTypeName,
                ResultTypeName = resultTypeName,
                SelectorParameterName = requestTemplate.SelectorParameterName,
                UsesProjectionHelperParameter = requestTemplate.UsesProjectionHelperParameter,
                ProjectionHelperParameterName = requestTemplate.ProjectionHelperParameterName,
                ProjectionHelperParameterTypeName =
                    requestTemplate.ProjectionHelperParameterTypeName,
                KeySelectorParameterName = requestTemplate.KeySelectorParameterName,
                KeySelectorBodyText = requestTemplate.KeySelectorBodyTemplate is null
                    ? null
                    : ProjectionBodyEmitter.ReplaceTokens(
                        requestTemplate.KeySelectorBodyTemplate,
                        replacements,
                        cancellationToken
                    ),
                InnerJoinFilterBodyText = requestTemplate.InnerJoinFilterBodyTemplate is null
                    ? null
                    : ProjectionBodyEmitter.ReplaceTokens(
                        requestTemplate.InnerJoinFilterBodyTemplate,
                        replacements,
                        cancellationToken
                    ),
                UseObjectSelectorSignature = requestTemplate.UseObjectSelectorSignature,
                CanUsePrebuiltExpression =
                    requestTemplate.CanUsePrebuiltExpressionWhenConfigured
                    && configuration.UsePrebuildExpression,
                InterceptableLocationVersion = requestTemplate.InterceptableLocationVersion,
                InterceptableLocationData = requestTemplate.InterceptableLocationData,
                Captures = requestTemplate.Captures,
                CaptureTransportKind = requestTemplate.CaptureTransportKind,
                CaptureTransportTypeName = requestTemplate.CaptureTransportTypeName,
                ProjectionBodyText = requestTemplate.ProjectionBodyTemplate
                    is { } projectionBodyTemplate
                    ? ProjectionBodyEmitter.ReplaceTokens(
                        projectionBodyTemplate,
                        replacements,
                        cancellationToken
                    )
                    : ProjectionBodyEmitter.BuildProjectionBody(
                        requestTemplate.Projection!,
                        requestTemplate.Pattern,
                        resultTypeName,
                        configuration.ArrayNullabilityRemoval,
                        replacements,
                        cancellationToken
                    ),
            },
            GeneratedDtos = dtoReplacements.Select(replacement => replacement.Dto).ToArray(),
        };
    }

    public static ObjectGenerationModel FinalizeObjectGeneration(
        ObjectGenerationSourceTemplateModel template,
        LinqraftConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        var dtoReplacements = BuildDtoReplacements(
            template.GeneratedDtos,
            configuration,
            cancellationToken
        );
        var replacements = dtoReplacements.ToDictionary(
            replacement => replacement.PlaceholderToken,
            replacement => replacement.Dto.FullyQualifiedName,
            StringComparer.Ordinal
        );
        return new ObjectGenerationModel
        {
            Request = new ObjectGenerationRequest
            {
                HintName = template.Request.HintName,
                MethodName = template.Request.MethodName,
                Origin = template.Request.Origin,
                ResultTypeName = ProjectionBodyEmitter.ReplaceTokens(
                    template.Request.ResultTypeTemplate,
                    replacements,
                    cancellationToken
                ),
                ProjectionBodyText = ProjectionBodyEmitter.BuildProjectionBody(
                    template.Request.Projection,
                    ProjectionPattern.PredefinedDto,
                    ProjectionBodyEmitter.ReplaceTokens(
                        template.Request.ResultTypeTemplate,
                        replacements,
                        cancellationToken
                    ),
                    configuration.ArrayNullabilityRemoval,
                    replacements,
                    cancellationToken
                ),
                Captures = template.Request.Captures,
                CaptureTransportKind = template.Request.CaptureTransportKind,
                CaptureTransportTypeName = template.Request.CaptureTransportTypeName,
                InterceptableLocationVersion = template.Request.InterceptableLocationVersion,
                InterceptableLocationData = template.Request.InterceptableLocationData,
            },
            GeneratedDtos = dtoReplacements.Select(replacement => replacement.Dto).ToArray(),
        };
    }

    public static MappingGenerationModel FinalizeMapping(
        MappingSourceTemplateModel template,
        LinqraftConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        var dtoReplacements = BuildDtoReplacements(
            template.GeneratedDtos,
            configuration,
            cancellationToken
        );
        var replacements = dtoReplacements.ToDictionary(
            replacement => replacement.PlaceholderToken,
            replacement => replacement.Dto.FullyQualifiedName,
            StringComparer.Ordinal
        );
        var requestTemplate = template.Request;
        var resultTypeName = ProjectionBodyEmitter.ReplaceTokens(
            requestTemplate.ResultTypeTemplate,
            replacements,
            cancellationToken
        );

        return new MappingGenerationModel
        {
            Request = new MappingRequest
            {
                HintName = requestTemplate.HintName,
                Origin = requestTemplate.Origin,
                Namespace = requestTemplate.Namespace,
                ContainingTypeName = requestTemplate.ContainingTypeName,
                AccessibilityKeyword = requestTemplate.AccessibilityKeyword,
                MethodAccessibilityKeyword = requestTemplate.MethodAccessibilityKeyword,
                MethodName = requestTemplate.MethodName,
                ReceiverKind = requestTemplate.ReceiverKind,
                SourceTypeName = requestTemplate.SourceTypeName,
                ResultTypeName = resultTypeName,
                SelectorParameterName = requestTemplate.SelectorParameterName,
                Captures = requestTemplate.Captures,
                CanUsePrebuiltExpression =
                    requestTemplate.CanUsePrebuiltExpressionWhenConfigured
                    && configuration.UsePrebuildExpression,
                ProjectionBodyText = ProjectionBodyEmitter.BuildProjectionBody(
                    requestTemplate.Projection,
                    ProjectionPattern.PredefinedDto,
                    resultTypeName,
                    configuration.ArrayNullabilityRemoval,
                    replacements,
                    cancellationToken
                ),
                InnerJoinFilterBodyText = requestTemplate.InnerJoinFilterBodyTemplate is null
                    ? null
                    : ProjectionBodyEmitter.ReplaceTokens(
                        requestTemplate.InnerJoinFilterBodyTemplate,
                        replacements,
                        cancellationToken
                    ),
            },
            GeneratedDtos = dtoReplacements.Select(replacement => replacement.Dto).ToArray(),
        };
    }

    public static EquatableArray<GeneratedDtoEmissionModel> MergeDtosForEmission(
        IEnumerable<GeneratedDtoModel> dtoModels,
        CancellationToken cancellationToken = default
    )
    {
        var mergedDtos = new Dictionary<string, GeneratedDtoModel>(StringComparer.Ordinal);
        var ownerHintNamesByDtoKey = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal
        );
        foreach (var dto in dtoModels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mergedDtos.TryGetValue(dto.Key, out var existing))
            {
                mergedDtos[dto.Key] = MergeDtoShape(existing, dto, cancellationToken);
            }
            else
            {
                mergedDtos.Add(dto.Key, dto);
            }

            if (!ownerHintNamesByDtoKey.TryGetValue(dto.Key, out var ownerHintNames))
            {
                ownerHintNames = new HashSet<string>(StringComparer.Ordinal);
                ownerHintNamesByDtoKey.Add(dto.Key, ownerHintNames);
            }

            ownerHintNames.Add(dto.OwnerHintName);
        }

        return mergedDtos
            .Values.OrderByDescending(dto => dto.IsRoot)
            .ThenBy(dto => dto.Namespace, StringComparer.Ordinal)
            .ThenBy(dto => dto.FullyQualifiedName, StringComparer.Ordinal)
            .Select(dto => new GeneratedDtoEmissionModel
            {
                Dto = dto,
                OwnerHintNames = ownerHintNamesByDtoKey[dto.Key]
                    .OrderBy(ownerHintName => ownerHintName, StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();
    }

    private static IReadOnlyList<GeneratedDtoReplacementModel> BuildDtoReplacements(
        EquatableArray<GeneratedDtoTemplateModel> templates,
        LinqraftConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        return templates
            .OrderByDescending(template => template.IsRoot)
            .ThenBy(template => template.TemplateId, StringComparer.Ordinal)
            .Select(template => new GeneratedDtoReplacementModel
            {
                PlaceholderToken = template.PlaceholderToken,
                Dto = FinalizeDto(template, templates, configuration, cancellationToken),
            })
            .ToArray();
    }

    private static GeneratedDtoModel FinalizeDto(
        GeneratedDtoTemplateModel template,
        EquatableArray<GeneratedDtoTemplateModel> allTemplates,
        LinqraftConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        var replacements = allTemplates.ToDictionary(
            candidate => candidate.PlaceholderToken,
            candidate => ResolveFullyQualifiedName(candidate, configuration),
            StringComparer.Ordinal
        );
        var properties = template
            .Properties.Select(property => new GeneratedPropertyModel
            {
                Name = property.Name,
                TypeName = ProjectionBodyEmitter.ResolveTypeTemplate(
                    property,
                    configuration.ArrayNullabilityRemoval,
                    replacements,
                    cancellationToken
                ),
                Documentation = property.Documentation,
                IsSuppressed = property.IsSuppressed,
            })
            .ToArray();
        var fullyQualifiedName = ResolveFullyQualifiedName(template, configuration);
        return new GeneratedDtoModel
        {
            Key = fullyQualifiedName,
            Namespace = ResolveNamespace(template, configuration),
            Name = ResolveTypeName(template, configuration),
            FullyQualifiedName = fullyQualifiedName,
            AccessibilityKeyword = template.AccessibilityKeyword,
            IsRecord = template.DeclaredIsRecord ?? configuration.RecordGenerate,
            IsRoot = template.IsRoot,
            IsAutoGeneratedNested = template.IsAutoGeneratedNested,
            Documentation = template.Documentation,
            OwnerHintName = template.OwnerHintName,
            Origins = template.Origins,
            ShapeSignature =
                $"{fullyQualifiedName}|{string.Join(";", properties.Select(property => $"{property.Name}:{property.TypeName}:{property.IsSuppressed}"))}",
            ContainingTypes = template.ContainingTypes,
            Properties = properties,
        };
    }

    private static string ResolveFullyQualifiedName(
        GeneratedDtoTemplateModel template,
        LinqraftConfiguration configuration
    )
    {
        var namespaceName = ResolveNamespace(template, configuration);
        var typeName = ResolveTypeName(template, configuration);
        var containingTypePrefix =
            template.ContainingTypes.Length == 0
                ? string.Empty
                : $"{string.Join(".", template.ContainingTypes.Select(type => type.Name))}.";
        return string.IsNullOrWhiteSpace(namespaceName)
            ? $"global::{containingTypePrefix}{typeName}"
            : $"global::{namespaceName}.{containingTypePrefix}{typeName}";
    }

    private static string ResolveNamespace(
        GeneratedDtoTemplateModel template,
        LinqraftConfiguration configuration
    )
    {
        string baseNamespace;
        if (string.IsNullOrWhiteSpace(template.PreferredNamespace))
        {
            baseNamespace = template.UseGlobalNamespaceFallback
                ? configuration.GlobalNamespace
                : string.Empty;
        }
        else
        {
            baseNamespace = template.PreferredNamespace;
        }

        if (template.Kind == GeneratedDtoTemplateKind.NestedAuto)
        {
            if (!configuration.NestedDtoUseHashNamespace)
            {
                return baseNamespace;
            }

            return string.IsNullOrWhiteSpace(baseNamespace)
                ? $"{configuration.GeneratorOptions.NestedDtoNamespacePrefix}_{template.ShapeHash}"
                : $"{baseNamespace}.{configuration.GeneratorOptions.NestedDtoNamespacePrefix}_{template.ShapeHash}";
        }

        return baseNamespace;
    }

    private static string ResolveTypeName(
        GeneratedDtoTemplateModel template,
        LinqraftConfiguration configuration
    )
    {
        return
            template.Kind == GeneratedDtoTemplateKind.NestedAuto
            && !configuration.NestedDtoUseHashNamespace
            ? $"{template.Name}_{template.ShapeHash}"
            : template.Name;
    }

    private static GeneratedDtoModel MergeDtoShape(
        GeneratedDtoModel existing,
        GeneratedDtoModel incoming,
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

            merged[property.Name] = new GeneratedPropertyModel
            {
                Name = property.Name,
                TypeName = MergePropertyTypeName(current.TypeName, property.TypeName),
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
                $"{existing.FullyQualifiedName}|{string.Join(";", mergedProperties.Select(property => $"{property.Name}:{property.TypeName}:{property.IsSuppressed}"))}",
        };
    }

    private static string MergePropertyTypeName(string existingTypeName, string incomingTypeName)
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

    private sealed record GeneratedDtoReplacementModel
    {
        public required string PlaceholderToken { get; init; }

        public required GeneratedDtoModel Dto { get; init; }
    }
}
