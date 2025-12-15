using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core;
using Linqraft.Core.Pipeline;
using Linqraft.Core.Pipeline.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Linqraft;

internal class SelectExprGroups
{
    public required List<SelectExprLocations> Exprs { get; set; }

    public List<GenerateDtoClassInfo> DtoClasses { get; set; } = [];

    public required LinqraftConfiguration Configuration { get; set; }

    public required string TargetNamespace
    {
        get
        {
            if (IsGlobalNamespace)
            {
                return Configuration.GlobalNamespace;
            }
            return targetNamespace;
        }
        set => targetNamespace = value.Trim();
    }
    private string targetNamespace = "";

    public required string TargetFileName { get; set; }

    // Determine if the target namespace is global (empty or compiler-generated)
    // Note: Compiler-generated namespaces contain '<' (e.g., for top-level statements)
    // TODO: In the future, use INamespaceSymbol.IsGlobalNamespace for more accurate detection
    private bool IsGlobalNamespace =>
        string.IsNullOrEmpty(targetNamespace) || targetNamespace.Contains("<");

    public string GetUniqueId()
    {
        var fileNamespace = IsGlobalNamespace ? "Global" : TargetNamespace;
        var targetNsReplaced = fileNamespace.Replace('.', '_');
        var filenameReplaced = TargetFileName.Replace('.', '_');
        return $"{targetNsReplaced}_{filenameReplaced}";
    }

    // Generate source code for expressions and co-located DTOs
    // This method uses the pipeline architecture for code generation
    public virtual void GenerateCodeWithoutDtos(SourceProductionContext context)
    {
        try
        {
            var selectExprMethods = new List<string>();
            var staticFields = new List<string>();
            var mappingMethods = new List<(SelectExprInfo Info, string Code)>();

            foreach (var expr in Exprs)
            {
                var info = expr.Info;

                // For mapping methods, generate extension method without interceptor
                if (info.MappingMethodName != null && expr.Location == null)
                {
                    // Use pipeline-based generation when available
                    var mappingMethod = GenerateMappingMethodWithPipeline(info);
                    if (!string.IsNullOrEmpty(mappingMethod))
                    {
                        mappingMethods.Add((info, mappingMethod));
                    }
                }
                else if (expr.Location != null)
                {
                    // For regular SelectExpr, generate interceptor
                    var exprMethods = info.GenerateSelectExprCodes(expr.Location);
                    selectExprMethods.AddRange(exprMethods);
                }

                var fields = info.GenerateStaticFields();
                if (fields != null)
                {
                    staticFields.Add(fields);
                }
            }

            // Use pipeline's DtoCodeBuilder for DTO generation
            var dtoCode =
                DtoClasses.Count > 0
                    ? BuildDtoCodeWithPipeline(DtoClasses)
                    : string.Empty;

            // Generate interceptor-based expression methods
            if (
                selectExprMethods.Count > 0
                || staticFields.Count > 0
                || !string.IsNullOrEmpty(dtoCode)
            )
            {
                var sourceCode = GenerateSourceCodeSnippets.BuildExprCodeSnippetsWithHeaders(
                    selectExprMethods,
                    staticFields,
                    dtoCode
                );
                var uniqueId = GetUniqueId();
                context.AddSource($"GeneratedExpression_{uniqueId}.g.cs", sourceCode);
            }

            // Separate mapping methods into regular (static partial class) and LinqraftMappingDeclare patterns
            var regularMappingMethods = mappingMethods
                .Where(m => string.IsNullOrEmpty(m.Info.MappingDeclareClassNameHash))
                .ToList();
            var declareMappingMethods = mappingMethods
                .Where(m => !string.IsNullOrEmpty(m.Info.MappingDeclareClassNameHash))
                .ToList();

            // Generate regular mapping methods grouped by containing class
            var regularMethodsByClass = regularMappingMethods
                .GroupBy(m =>
                    m.Info.MappingContainingClass?.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    ) ?? ""
                )
                .Where(g => !string.IsNullOrEmpty(g.Key));

            foreach (var classGroup in regularMethodsByClass)
            {
                var firstInfo = classGroup.First().Info;
                var containingClass = firstInfo.MappingContainingClass;
                if (containingClass == null)
                    continue;

                var sourceCode = GenerateSourceCodeSnippets.BuildMappingClassCode(
                    containingClass,
                    classGroup.Select(m => m.Code).ToList()
                );
                var className = containingClass.Name.Replace("<", "_").Replace(">", "_");
                var hash = HashUtility.GenerateSha256Hash(containingClass.ToDisplayString());
                context.AddSource($"GeneratedMapping_{className}_{hash}.g.cs", sourceCode);
            }

            // Generate LinqraftMappingDeclare mapping methods with hash suffix
            var declareMethodsByClass = declareMappingMethods
                .GroupBy(m =>
                    m.Info.MappingContainingClass?.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    ) ?? ""
                )
                .Where(g => !string.IsNullOrEmpty(g.Key));

            foreach (var classGroup in declareMethodsByClass)
            {
                var firstInfo = classGroup.First().Info;
                var containingClass = firstInfo.MappingContainingClass;
                var classNameHash = firstInfo.MappingDeclareClassNameHash;
                if (containingClass == null || string.IsNullOrEmpty(classNameHash))
                    continue;

                // Generate class name with hash suffix
                var baseClassName = containingClass.Name;
                var customClassName = $"{baseClassName}_{classNameHash}";

                var sourceCode = GenerateSourceCodeSnippets.BuildMappingClassCode(
                    containingClass,
                    classGroup.Select(m => m.Code).ToList(),
                    customClassName
                );
                var hash = HashUtility.GenerateSha256Hash(containingClass.ToDisplayString());
                context.AddSource($"GeneratedMapping_{customClassName}_{hash}.g.cs", sourceCode);
            }
        }
        catch (Exception ex)
        {
            // Output error information for debugging
            var errorMessage = $"""
                /*
                 * Source Generator Error: {ex.Message}
                 * Stack Trace: {ex.StackTrace}
                 */
                """;
            var hash = HashUtility.GenerateRandomIdentifier();
            context.AddSource($"GeneratorError_{hash}.g.cs", errorMessage);
        }
    }

    /// <summary>
    /// Generates mapping method code using the pipeline architecture.
    /// Falls back to GenerateSourceCodeSnippets for backward compatibility.
    /// </summary>
    private string GenerateMappingMethodWithPipeline(SelectExprInfo info)
    {
        // Use the existing GenerateSourceCodeSnippets for now
        // The pipeline-based generation can be enabled later
        return GenerateSourceCodeSnippets.GenerateMappingMethod(info);
    }

    /// <summary>
    /// Builds DTO code using the pipeline's DtoCodeBuilder.
    /// </summary>
    private string BuildDtoCodeWithPipeline(List<GenerateDtoClassInfo> dtoClasses)
    {
        // Use the DtoCodeBuilder through the static method for now
        // This provides a clean abstraction while maintaining compatibility
        return GenerateSourceCodeSnippets.BuildDtoCodeSnippetsGroupedByNamespace(
            dtoClasses,
            Configuration
        );
    }
}

internal class SelectExprLocations
{
    public required SelectExprInfo Info { get; init; }
    public required InterceptableLocation? Location { get; init; }
}
