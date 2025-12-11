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
            var mappingMethods = new List<(SelectExprInfo Info, string Code)>();

            foreach (var expr in Exprs)
            {
                var info = expr.Info;
                
                // For mapping methods, generate extension method without interceptor
                if (info.MappingMethodName != null && expr.Location == null)
                {
                    var mappingMethod = GenerateMappingMethod(info);
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

            // Generate interceptor-based expression methods
            if (selectExprMethods.Count > 0 || staticFields.Count > 0)
            {
                var sourceCode = GenerateSourceCodeSnippets.BuildExprCodeSnippetsWithHeaders(
                    selectExprMethods,
                    staticFields
                );
                var uniqueId = GetUniqueId();
                context.AddSource($"GeneratedExpression_{uniqueId}.g.cs", sourceCode);
            }

            // Generate mapping methods grouped by containing class
            var mappingMethodsByClass = mappingMethods
                .GroupBy(m => m.Info.MappingContainingClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "")
                .Where(g => !string.IsNullOrEmpty(g.Key));

            foreach (var classGroup in mappingMethodsByClass)
            {
                var firstInfo = classGroup.First().Info;
                var containingClass = firstInfo.MappingContainingClass;
                if (containingClass == null)
                    continue;

                var sourceCode = GenerateMappingClassCode(containingClass, classGroup.Select(m => m.Code).ToList());
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

    // Generate the containing class code for mapping methods
    private static string GenerateMappingClassCode(INamedTypeSymbol containingClass, List<string> methods)
    {
        var sb = new System.Text.StringBuilder();
        
        // Add header
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file is auto-generated by Linqraft");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        // Add using directives
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();

        // Get namespace
        var namespaceName = containingClass.ContainingNamespace?.ToDisplayString();
        
        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        // Generate the partial class
        var indent = !string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>" ? "    " : "";
        var accessibility = containingClass.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "internal"
        };

        sb.AppendLine($"{indent}{accessibility} static partial class {containingClass.Name}");
        sb.AppendLine($"{indent}{{");

        // Add all methods
        foreach (var method in methods)
        {
            var indentedMethod = method.Replace("\n", $"\n{indent}    ");
            sb.AppendLine($"{indent}    {indentedMethod}");
        }

        sb.AppendLine($"{indent}}}");

        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    // Generate extension method for mapping methods (without interceptor)
    private static string GenerateMappingMethod(SelectExprInfo info)
    {
        if (info.MappingMethodName == null || info.MappingContainingClass == null)
            return "";

        // Analyze the DTO structure
        var dtoStructure = info.GenerateDtoStructure();

        if (dtoStructure == null || dtoStructure.Properties.Count == 0)
            return "";

        // Get the method name from the mapping info
        var methodName = info.MappingMethodName;
        var containingClass = info.MappingContainingClass;
        
        // Get the DTO class name
        var dtoClassName = info.GetParentDtoClassName(dtoStructure);

        // Get return type prefix (IQueryable or IEnumerable)
        var returnTypePrefix = info.GetReturnTypePrefix();

        var sourceTypeFullName = dtoStructure.SourceTypeFullName;

        // Build the extension method
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// Extension method generated by LinqraftMappingGenerate attribute");
        sb.AppendLine($"/// </summary>");
        
        // Generate method signature as an extension method
        sb.AppendLine($"internal static {returnTypePrefix}<{dtoClassName}> {methodName}(");
        sb.AppendLine($"    this {returnTypePrefix}<{sourceTypeFullName}> source)");
        sb.AppendLine($"{{");
        sb.AppendLine($"    return source.Select({info.LambdaParameterName} => new {dtoClassName}");
        sb.AppendLine($"    {{");

        // Generate property assignments
        foreach (var prop in dtoStructure.Properties)
        {
            var assignment = info.GeneratePropertyAssignment(prop, 8);
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
