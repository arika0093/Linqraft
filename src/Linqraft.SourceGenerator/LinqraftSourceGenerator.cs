using System;
using System.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class LinqraftSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource(
                "Linqraft.Declarations.g.cs",
                SupportSourceEmitter.CreateSupportSource()
            )
        );

        var configuration = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => LinqraftConfiguration.Parse(provider.GlobalOptions)
        );

        var selectExprTemplates = context
            .SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    node is InvocationExpressionSyntax invocation
                    && IsPotentialSelectExprInvocation(invocation),
                static (syntaxContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeSelectInvocation(
                        syntaxContext,
                        cancellationToken
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var mappingClassTemplates = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Linqraft.LinqraftMappingGenerateAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeMappingClass(
                        attributeContext,
                        cancellationToken
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var mappingMethodTemplates = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Linqraft.LinqraftMappingGenerateAttribute",
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeMappingMethod(
                        attributeContext,
                        cancellationToken
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var projectionModels = selectExprTemplates.Combine(configuration).Select(
            static (data, _) => ProjectionModelFinalizer.FinalizeProjection(data.Left, data.Right)
        );
        var mappingClassModels = mappingClassTemplates.Combine(configuration).Select(
            static (data, _) => ProjectionModelFinalizer.FinalizeMapping(data.Left, data.Right)
        );
        var mappingMethodModels = mappingMethodTemplates.Combine(configuration).Select(
            static (data, _) => ProjectionModelFinalizer.FinalizeMapping(data.Left, data.Right)
        );

        var projectionSources = projectionModels.Select(
            static (model, _) => new GeneratedSourceFileModel
            {
                HintName = $"{model.Request.HintName}.g.cs",
                SourceText = SourceWriters.WriteProjectionUnit(model.Request),
            }
        );
        var mappingClassSources = mappingClassModels.Select(
            static (model, _) => new GeneratedSourceFileModel
            {
                HintName = $"{model.Request.HintName}.g.cs",
                SourceText = SourceWriters.WriteMappingUnit(model.Request),
            }
        );
        var mappingMethodSources = mappingMethodModels.Select(
            static (model, _) => new GeneratedSourceFileModel
            {
                HintName = $"{model.Request.HintName}.g.cs",
                SourceText = SourceWriters.WriteMappingUnit(model.Request),
            }
        );

        context.RegisterSourceOutput(
            projectionSources,
            static (output, source) => output.AddSource(source.HintName, source.SourceText)
        );
        context.RegisterSourceOutput(
            mappingClassSources,
            static (output, source) => output.AddSource(source.HintName, source.SourceText)
        );
        context.RegisterSourceOutput(
            mappingMethodSources,
            static (output, source) => output.AddSource(source.HintName, source.SourceText)
        );

        var dtoSources = projectionModels
            .SelectMany(static (model, _) => model.GeneratedDtos)
            .Collect()
            .Combine(mappingClassModels.SelectMany(static (model, _) => model.GeneratedDtos).Collect())
            .Combine(mappingMethodModels.SelectMany(static (model, _) => model.GeneratedDtos).Collect())
            .Combine(configuration)
            .Select(static (data, _) => BuildDtoSources(data));

        context.RegisterSourceOutput(
            dtoSources,
            static (output, sourceSet) =>
            {
                foreach (var source in sourceSet.Sources)
                {
                    output.AddSource(source.HintName, source.SourceText);
                }
            }
        );
    }

    private static GeneratedSourceSetModel BuildDtoSources(
        (((System.Collections.Immutable.ImmutableArray<GeneratedDtoModel> projectionDtos,
            System.Collections.Immutable.ImmutableArray<GeneratedDtoModel> mappingClassDtos),
            System.Collections.Immutable.ImmutableArray<GeneratedDtoModel> mappingMethodDtos),
            LinqraftConfiguration configuration) data
    )
    {
        var (((projectionDtos, mappingClassDtos), mappingMethodDtos), configuration) = data;
        var mergedDtos = ProjectionModelFinalizer.MergeDtos(
            projectionDtos.Concat(mappingClassDtos).Concat(mappingMethodDtos)
        );
        return new GeneratedSourceSetModel
        {
            Sources = mergedDtos
                .Select(dto => new GeneratedSourceFileModel
                {
                    HintName = $"{SymbolNameHelper.SanitizeHintName(dto.Key)}.g.cs",
                    SourceText = SourceWriters.WriteDtoUnit(dto, configuration),
                })
                .ToArray(),
        };
    }

    private static bool IsPotentialSelectExprInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name.Identifier.ValueText == "SelectExpr" => true,
            GenericNameSyntax genericName when genericName.Identifier.ValueText == "SelectExpr" =>
                true,
            _ => false,
        };
    }
}
