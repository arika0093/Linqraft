using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core;
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

    // Generate source code without DTOs (for global DTO generation)
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
                    var mappingMethod = GenerateSourceCodeSnippets.GenerateMappingMethod(info);
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

            var dtoCode = DtoClasses.Count > 0
                ? GenerateSourceCodeSnippets.BuildDtoCodeSnippetsGroupedByNamespace(
                    DtoClasses,
                    Configuration
                )
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

            // Generate mapping methods grouped by containing class
            var mappingMethodsByClass = mappingMethods
                .GroupBy(m =>
                    m.Info.MappingContainingClass?.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    ) ?? ""
                )
                .Where(g => !string.IsNullOrEmpty(g.Key));

            foreach (var classGroup in mappingMethodsByClass)
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
}

internal class SelectExprLocations
{
    public required SelectExprInfo Info { get; init; }
    public required InterceptableLocation? Location { get; init; }
}
