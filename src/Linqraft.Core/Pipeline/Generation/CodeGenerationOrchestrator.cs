using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Orchestrates the full code generation pipeline from SelectExprInfo to source output.
/// This is the main entry point for converting analyzed SelectExpr invocations to generated code.
/// </summary>
internal class CodeGenerationOrchestrator
{
    private readonly LinqraftConfiguration _configuration;

    /// <summary>
    /// Creates a new code generation orchestrator.
    /// </summary>
    public CodeGenerationOrchestrator(LinqraftConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Generates all source code from SelectExprInfo instances.
    /// </summary>
    public GeneratedCodeResult GenerateAll(
        IEnumerable<SelectExprInfo> selectExprInfos,
        IEnumerable<SelectExprInfo> mappingSelectExprInfos)
    {
        var result = new GeneratedCodeResult();

        // Combine all infos
        var allInfos = selectExprInfos.Concat(mappingSelectExprInfos).ToList();

        // Assign configuration to each info
        foreach (var info in allInfos)
        {
            info.Configuration = _configuration;
        }

        // Group by namespace and filename
        var groups = GroupInfos(allInfos);

        // Track DTOs for global deduplication
        var hashNamespaceDtoClassInfos = new List<GenerateDtoClassInfo>();
        var emittedDtoFullNames = new HashSet<string>();

        foreach (var group in groups)
        {
            var groupDtos = new List<GenerateDtoClassInfo>();

            foreach (var info in group.Infos)
            {
                var classInfos = info.GenerateDtoClasses();
                foreach (var classInfo in classInfos)
                {
                    // DTOs in hash-named namespaces can stay in the shared file
                    if (IsHashNamespaceDto(classInfo.Namespace))
                    {
                        hashNamespaceDtoClassInfos.Add(classInfo);
                        continue;
                    }

                    // Deduplicate by FullName
                    if (emittedDtoFullNames.Add(classInfo.FullName))
                    {
                        groupDtos.Add(classInfo);
                    }
                }
            }

            group.DtoClasses = groupDtos;
        }

        result.Groups = groups;
        result.GlobalDtoClassInfos = hashNamespaceDtoClassInfos;

        return result;
    }

    /// <summary>
    /// Groups SelectExprInfo by namespace, filename, and mapping class.
    /// </summary>
    private List<CodeGenerationGroup> GroupInfos(List<SelectExprInfo> allInfos)
    {
        return allInfos
            .GroupBy(info => new
            {
                Namespace = info.GetNamespaceString(),
                FileName = info.GetFileNameString() ?? "",
                MappingClass = info.MappingContainingClass?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat) ?? ""
            })
            .Select(g => new CodeGenerationGroup
            {
                TargetNamespace = g.Key.Namespace,
                TargetFileName = g.Key.FileName,
                MappingClass = g.Key.MappingClass,
                Infos = g.ToList(),
                Configuration = _configuration
            })
            .ToList();
    }

    /// <summary>
    /// Checks if a namespace is a hash-generated namespace.
    /// </summary>
    private static bool IsHashNamespaceDto(string? namespaceName)
    {
        if (namespaceName is not { Length: > 0 } ns)
        {
            return false;
        }

        // Check if the final segment starts with the hash namespace prefix
        var lastDotIndex = ns.LastIndexOf('.');
        var finalSegment = lastDotIndex >= 0 ? ns[(lastDotIndex + 1)..] : ns;
        return finalSegment.StartsWith(PipelineConstants.HashNamespacePrefix);
    }
}

/// <summary>
/// Represents a group of SelectExprInfo for code generation.
/// </summary>
internal class CodeGenerationGroup
{
    /// <summary>
    /// The target namespace for generated code.
    /// </summary>
    public required string TargetNamespace { get; set; }

    /// <summary>
    /// The target filename for generated code.
    /// </summary>
    public required string TargetFileName { get; set; }

    /// <summary>
    /// The mapping class full name (empty if not a mapping method).
    /// </summary>
    public required string MappingClass { get; set; }

    /// <summary>
    /// The SelectExprInfo instances in this group.
    /// </summary>
    public required List<SelectExprInfo> Infos { get; set; }

    /// <summary>
    /// The DTO classes generated for this group.
    /// </summary>
    public List<GenerateDtoClassInfo> DtoClasses { get; set; } = [];

    /// <summary>
    /// The configuration.
    /// </summary>
    public required LinqraftConfiguration Configuration { get; set; }

    /// <summary>
    /// Gets a unique identifier for this group.
    /// </summary>
    public string GetUniqueId()
    {
        var isGlobalNamespace = string.IsNullOrEmpty(TargetNamespace) || TargetNamespace.Contains("<");
        var fileNamespace = isGlobalNamespace ? Configuration.GlobalNamespace : TargetNamespace;
        var targetNsReplaced = fileNamespace.Replace('.', '_');
        var filenameReplaced = TargetFileName.Replace('.', '_');
        return $"{targetNsReplaced}_{filenameReplaced}";
    }
}

/// <summary>
/// Result of the code generation orchestration.
/// </summary>
internal class GeneratedCodeResult
{
    /// <summary>
    /// The code generation groups.
    /// </summary>
    public List<CodeGenerationGroup> Groups { get; set; } = [];

    /// <summary>
    /// Global DTO class infos (hash-namespaced).
    /// </summary>
    public List<GenerateDtoClassInfo> GlobalDtoClassInfos { get; set; } = [];
}
