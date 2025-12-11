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

    // Generate source code without DTOs (for global DTO generation)
    public virtual void GenerateCodeWithoutDtos(SourceProductionContext context)
    {
        try
        {
            var selectExprMethods = new List<string>();
            var staticFields = new List<string>();

            foreach (var expr in Exprs)
            {
                var info = expr.Info;
                
                // For mapping methods, generate extension method without interceptor
                if (info.MappingMethodName != null && expr.Location == null)
                {
                    var mappingMethod = GenerateMappingMethod(info);
                    if (!string.IsNullOrEmpty(mappingMethod))
                    {
                        selectExprMethods.Add(mappingMethod);
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

            // Generate only expression methods without DTOs
            var sourceCode = GenerateSourceCodeSnippets.BuildExprCodeSnippetsWithHeaders(
                selectExprMethods,
                staticFields
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

    // Generate extension method for mapping methods (without interceptor)
    private static string GenerateMappingMethod(SelectExprInfo info)
    {
        if (info.MappingMethodName == null || info.MappingContainingClass == null)
            return "";

        // Analyze the DTO structure
        var dtoStructure = info.GetType()
            .GetMethod("GenerateDtoStructure", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(info, null) as DtoStructure;

        if (dtoStructure == null || dtoStructure.Properties.Count == 0)
            return "";

        // Get the method name from the mapping info
        var methodName = info.MappingMethodName;
        var containingClass = info.MappingContainingClass;
        
        // Generate the extension method using reflection to call GetParentDtoClassName
        var getParentDtoClassName = info.GetType().GetMethod("GetParentDtoClassName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dtoClassName = getParentDtoClassName?.Invoke(info, new object[] { dtoStructure }) as string ?? "";

        // Get return type prefix (IQueryable or IEnumerable)
        var returnTypePrefix = info.GetType()
            .GetMethod("GetReturnTypePrefix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(info, null) as string ?? "IQueryable";

        var sourceTypeFullName = dtoStructure.SourceTypeFullName;

        // Build the extension method
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// Extension method generated by LinqraftMappingGenerate attribute");
        sb.AppendLine($"/// </summary>");
        
        // Generate method signature as an extension method in a partial class
        sb.AppendLine($"internal static partial {returnTypePrefix}<{dtoClassName}> {methodName}(");
        sb.AppendLine($"    this {returnTypePrefix}<{sourceTypeFullName}> source)");
        sb.AppendLine($"{{");
        sb.AppendLine($"    return source.Select({info.LambdaParameterName} => new {dtoClassName}");
        sb.AppendLine($"    {{");

        // Generate property assignments using reflection
        var generatePropertyAssignment = info.GetType().GetMethod("GeneratePropertyAssignment",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        foreach (var prop in dtoStructure.Properties)
        {
            var assignment = generatePropertyAssignment?.Invoke(info, new object[] { prop, 8 }) as string ?? "";
            sb.AppendLine($"        {prop.Name} = {assignment},");
        }

        sb.AppendLine($"    }});");
        sb.AppendLine($"}}");
        sb.AppendLine();

        return sb.ToString();
    }
}

internal class SelectExprLocations
{
    public required SelectExprInfo Info { get; init; }
    public required InterceptableLocation? Location { get; init; }
}
