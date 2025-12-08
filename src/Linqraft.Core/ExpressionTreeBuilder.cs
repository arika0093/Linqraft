using System;
using System.Text;

namespace Linqraft.Core;

/// <summary>
/// Helper class for generating pre-built Expression Tree code
/// This generates static cached expression fields with direct initialization,
/// avoiding the overhead of building expression trees at runtime for IQueryable operations.
/// </summary>
public static class ExpressionTreeBuilder
{
    /// <summary>
    /// Generates a static cached expression tree field with direct initialization
    /// </summary>
    /// <param name="sourceTypeFullName">The fully qualified source type name</param>
    /// <param name="resultTypeFullName">The fully qualified result type name (DTO type)</param>
    /// <param name="lambdaParameterName">The lambda parameter name</param>
    /// <param name="lambdaBody">The lambda body code</param>
    /// <param name="uniqueId">A unique identifier for this expression to avoid collisions</param>
    /// <returns>A tuple of (fieldDeclaration, fieldName)</returns>
    public static (string FieldDeclaration, string FieldName) GenerateExpressionTreeField(
        string sourceTypeFullName,
        string resultTypeFullName,
        string lambdaParameterName,
        string lambdaBody,
        string uniqueId
    )
    {
        // Generate a unique field name based on the hash
        var hash = HashUtility.GenerateSha256Hash(uniqueId).Substring(0, 8);
        var fieldName = $"_cachedExpression_{hash}";

        // Build the expression tree field with direct initialization
        var sb = new StringBuilder();
        
        // The lambda body may contain newlines, so we need to properly handle multi-line expressions
        var lines = lambdaBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines.Length == 1)
        {
            // Single line lambda body
            sb.AppendLine($"    private static global::System.Linq.Expressions.Expression<global::System.Func<{sourceTypeFullName}, {resultTypeFullName}>> {fieldName} = {lambdaParameterName} => {lambdaBody};");
        }
        else
        {
            // Multi-line lambda body - need to format it properly
            sb.AppendLine($"    private static global::System.Linq.Expressions.Expression<global::System.Func<{sourceTypeFullName}, {resultTypeFullName}>> {fieldName} = {lambdaParameterName} =>");
            // Indent the lambda body by 2 levels (8 spaces) from the start of the line
            var indentedBody = Formatting.CodeFormatter.IndentCode(lambdaBody.TrimEnd(), Formatting.CodeFormatter.IndentSize * 2);
            sb.Append(indentedBody + ";");
        }
        
        return (sb.ToString(), fieldName);
    }
}
