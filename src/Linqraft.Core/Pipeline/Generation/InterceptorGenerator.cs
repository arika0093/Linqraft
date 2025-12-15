using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Generates interceptor code for SelectExpr method calls.
/// This is part of the Generation phase of the pipeline.
/// </summary>
internal class InterceptorGenerator
{
    private readonly SemanticModel _semanticModel;
    private readonly LinqraftConfiguration _configuration;

    /// <summary>
    /// Creates a new interceptor generator.
    /// </summary>
    public InterceptorGenerator(SemanticModel semanticModel, LinqraftConfiguration configuration)
    {
        _semanticModel = semanticModel;
        _configuration = configuration;
    }

    /// <summary>
    /// Generates the interceptor method code for a SelectExpr invocation.
    /// </summary>
    public string GenerateInterceptorMethod(
        InterceptableLocation location,
        string dtoClassName,
        string sourceTypeFullName,
        string lambdaParameterName,
        string returnTypePrefix,
        List<PropertyAssignment> propertyAssignments)
    {
        var sb = new StringBuilder();

        // Generate the interceptor attribute
        sb.AppendLine(GetInterceptorAttribute(location));

        // Generate method signature
        sb.AppendLine($"internal static {returnTypePrefix}<{dtoClassName}> Generated_{GetInterceptorMethodName(location)}(");
        sb.AppendLine($"    this {returnTypePrefix}<{sourceTypeFullName}> source,");
        sb.AppendLine($"    System.Func<{sourceTypeFullName}, object> selector)");
        sb.AppendLine("{");
        sb.AppendLine($"    return source.Select({lambdaParameterName} => new {dtoClassName}");
        sb.AppendLine("    {");

        // Generate property assignments
        foreach (var assignment in propertyAssignments)
        {
            sb.AppendLine($"        {assignment.PropertyName} = {assignment.Expression},");
        }

        sb.AppendLine("    });");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the interceptor attribute for a location.
    /// </summary>
    private static string GetInterceptorAttribute(InterceptableLocation location)
    {
        return $"[global::System.Runtime.CompilerServices.InterceptsLocation({location.Version}, \"{location.Data}\")]";
    }

    /// <summary>
    /// Gets a unique method name from the interceptor location.
    /// </summary>
    private static string GetInterceptorMethodName(InterceptableLocation location)
    {
        // Generate a unique name based on version and data hash
        var hash = HashUtility.GenerateSha256Hash($"{location.Version}_{location.Data}");
        return $"Interceptor_{hash}";
    }
}

/// <summary>
/// Represents a property assignment in generated code.
/// </summary>
internal record PropertyAssignment
{
    /// <summary>
    /// The property name.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// The expression to assign to the property.
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    /// The type of the property.
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// Whether the property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }
}
