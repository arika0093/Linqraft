using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Generates source code output from pipeline-processed data.
/// This is the final stage of the pipeline that produces actual C# source code.
/// </summary>
internal class SourceCodeGenerator
{
    private readonly LinqraftConfiguration _configuration;

    /// <summary>
    /// Creates a new source code generator.
    /// </summary>
    public SourceCodeGenerator(LinqraftConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Builds a DTO class code string from a GenerateDtoClassInfo.
    /// </summary>
    public string BuildDtoClassCode(GenerateDtoClassInfo classInfo)
    {
        return classInfo.BuildCode(_configuration);
    }

    /// <summary>
    /// Builds DTO classes grouped by namespace.
    /// </summary>
    public string BuildDtoCodeSnippetsGroupedByNamespace(List<GenerateDtoClassInfo> dtoClassInfos)
    {
        return GenerateSourceCodeSnippets.BuildDtoCodeSnippetsGroupedByNamespace(
            dtoClassInfos,
            _configuration);
    }

    /// <summary>
    /// Builds expression code snippets with headers.
    /// </summary>
    public string BuildExprCodeSnippetsWithHeaders(
        List<string> expressions,
        List<string> staticFields,
        string? dtoCode = null)
    {
        return GenerateSourceCodeSnippets.BuildExprCodeSnippetsWithHeaders(
            expressions,
            staticFields,
            dtoCode);
    }

    /// <summary>
    /// Builds a global DTO code snippet for all DTOs.
    /// </summary>
    public string BuildGlobalDtoCodeSnippet(List<GenerateDtoClassInfo> allDtoClassInfos)
    {
        return GenerateSourceCodeSnippets.BuildGlobalDtoCodeSnippet(
            allDtoClassInfos,
            _configuration);
    }

    /// <summary>
    /// Builds mapping class code wrapper.
    /// </summary>
    public string BuildMappingClassCode(
        INamedTypeSymbol containingClass,
        List<string> methods)
    {
        return GenerateSourceCodeSnippets.BuildMappingClassCode(containingClass, methods);
    }

    /// <summary>
    /// Builds mapping class code with custom class name.
    /// </summary>
    public string BuildMappingClassCode(
        INamedTypeSymbol containingClass,
        List<string> methods,
        string customClassName)
    {
        return GenerateSourceCodeSnippets.BuildMappingClassCode(
            containingClass,
            methods,
            customClassName);
    }

    /// <summary>
    /// Generates a mapping method from SelectExprInfo.
    /// </summary>
    public string GenerateMappingMethod(SelectExprInfo info)
    {
        return GenerateSourceCodeSnippets.GenerateMappingMethod(info);
    }

    /// <summary>
    /// Gets the DTO code attributes.
    /// </summary>
    public static string BuildDtoCodeAttributes()
    {
        return GenerateSourceCodeSnippets.BuildDtoCodeAttributes();
    }
}
