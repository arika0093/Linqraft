using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

        var selectExprInvocations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) =>
                node is InvocationExpressionSyntax invocation
                && IsPotentialSelectExprInvocation(invocation),
            static (syntaxContext, _) => (InvocationExpressionSyntax)syntaxContext.Node
        );

        var mappingClasses = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Linqraft.LinqraftMappingGenerateAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (context, _) => (ClassDeclarationSyntax)context.TargetNode
        );

        var mappingMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Linqraft.LinqraftMappingGenerateAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (context, _) => (MethodDeclarationSyntax)context.TargetNode
        );

        var configuration = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => LinqraftConfiguration.Parse(provider.GlobalOptions)
        );

        var pipeline = context
            .CompilationProvider.Combine(configuration)
            .Combine(selectExprInvocations.Collect())
            .Combine(mappingClasses.Collect())
            .Combine(mappingMethods.Collect());

        context.RegisterSourceOutput(
            pipeline,
            static (output, data) =>
            {
                var (
                    (((compilation, configuration), selectExprs), mappingClassDeclarations),
                    mappingMethodDeclarations
                ) = data;
                var analyzer = new ProjectionAnalyzer(
                    compilation,
                    configuration,
                    output,
                    output.CancellationToken
                );

                analyzer.AnalyzeSelectInvocations(selectExprs);
                analyzer.AnalyzeMappingClasses(mappingClassDeclarations);
                analyzer.AnalyzeMappingMethods(mappingMethodDeclarations);

                var dtosByOwner = analyzer
                    .GeneratedDtos.GroupBy(dto => dto.OwnerHintName, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyCollection<GeneratedDtoModel>)group.ToList(),
                        StringComparer.Ordinal
                    );
                var emittedOwners = new HashSet<string>(StringComparer.Ordinal);

                foreach (var request in analyzer.ProjectionRequests)
                {
                    var semanticModel = compilation.GetSemanticModel(request.Invocation.SyntaxTree);
                    emittedOwners.Add(request.HintName);
                    dtosByOwner.TryGetValue(request.HintName, out var ownedDtos);
                    output.AddSource(
                        $"{request.HintName}.g.cs",
                        SourceWriters.WriteProjectionUnit(
                            request,
                            ownedDtos ?? Array.Empty<GeneratedDtoModel>(),
                            semanticModel,
                            configuration
                        )
                    );
                }

                foreach (var mapping in analyzer.MappingRequests)
                {
                    var semanticModel = compilation.GetSemanticModel(mapping.SourceNode.SyntaxTree);
                    emittedOwners.Add(mapping.HintName);
                    dtosByOwner.TryGetValue(mapping.HintName, out var ownedDtos);
                    output.AddSource(
                        $"{mapping.HintName}.g.cs",
                        SourceWriters.WriteMappingUnit(
                            mapping,
                            ownedDtos ?? Array.Empty<GeneratedDtoModel>(),
                            semanticModel,
                            configuration
                        )
                    );
                }

                foreach (
                    var dto in analyzer.GeneratedDtos.Where(dto =>
                        !emittedOwners.Contains(dto.OwnerHintName)
                    )
                )
                {
                    output.AddSource(
                        $"{SymbolNameHelper.SanitizeHintName(dto.Key)}.g.cs",
                        SourceWriters.WriteDtoUnit(dto, configuration)
                    );
                }
            }
        );
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
