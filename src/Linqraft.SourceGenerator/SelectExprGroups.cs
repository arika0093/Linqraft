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

    // Generate source code
    public virtual void GenerateCode(SourceProductionContext context)
    {
        try
        {
            var dtoClassInfos = new List<GenerateDtoClassInfo>();
            var selectExprMethods = new List<string>();

            foreach (var expr in Exprs)
            {
                var info = expr.Info;
                var classInfos = info.GenerateDtoClasses();
                var exprMethods = info.GenerateSelectExprCodes(expr.Location);
                dtoClassInfos.AddRange(classInfos);
                selectExprMethods.AddRange(exprMethods);
            }

            // drop duplicate DTO classes based on full name
            var dtoClassesDistinct = dtoClassInfos
                .GroupBy(c => c.FullName)
                .Select(g => g.First())
                .ToList();

            // Build final source code using the new method that groups DTOs by namespace
            var sourceCode = GenerateSourceCodeSnippets.BuildCodeSnippetAll(
                selectExprMethods,
                dtoClassesDistinct,
                Configuration
            );

            // Register with Source Generator
            var uniqueId = GetUniqueId();
            context.AddSource($"GeneratedExpression_{uniqueId}.g.cs", sourceCode);
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
    public required InterceptableLocation Location { get; init; }
}
