using System.Text;

namespace Linqraft.Core;

/// <summary>
/// Helper class for generating pre-built Expression Tree code
/// This generates static cached expression fields that are initialized once and reused,
/// avoiding the overhead of building expression trees at runtime for IQueryable operations.
/// </summary>
public static class ExpressionTreeBuilder
{
    /// <summary>
    /// Generates a static cached expression tree field declaration
    /// </summary>
    /// <param name="sourceTypeFullName">The fully qualified source type name</param>
    /// <param name="resultTypeFullName">The fully qualified result type name (DTO type)</param>
    /// <param name="uniqueId">A unique identifier for this expression to avoid collisions</param>
    /// <returns>A tuple of (fieldDeclaration, fieldName)</returns>
    public static (string FieldDeclaration, string FieldName) GenerateExpressionTreeField(
        string sourceTypeFullName,
        string resultTypeFullName,
        string uniqueId
    )
    {
        // Generate a unique field name based on the hash
        var hash = HashUtility.GenerateSha256Hash(uniqueId).Substring(0, 8);
        var fieldName = $"_cachedExpression_{hash}";

        // Generate the static field declaration - stores the compiled expression tree
        var fieldDeclaration = $"    private static global::System.Linq.Expressions.Expression<global::System.Func<{sourceTypeFullName}, {resultTypeFullName}>>? {fieldName};";
        
        return (fieldDeclaration, fieldName);
    }

    /// <summary>
    /// Generates the expression tree initialization code for anonymous types.
    /// This creates a lambda expression once and caches it, then uses it with IQueryable.
    /// </summary>
    /// <param name="lambdaBody">The lambda body code (the "new { ... }" part)</param>
    /// <param name="lambdaParameterName">The lambda parameter name</param>
    /// <param name="fieldName">The field name to initialize</param>
    /// <returns>The initialization code</returns>
    public static string GenerateAnonymousExpressionTreeInitialization(
        string lambdaBody,
        string lambdaParameterName,
        string fieldName
    )
    {
        var sb = new StringBuilder();
        
        // Check if field is already initialized
        sb.AppendLine($"    if ({fieldName} == null)");
        sb.AppendLine("    {");
        
        // Create the expression tree from the lambda
        // The C# compiler will automatically convert the lambda to an Expression<Func<TIn, TResult>>
        // when we explicitly type it
        sb.AppendLine($"        global::System.Linq.Expressions.Expression<global::System.Func<TIn, TResult>> expr = {lambdaParameterName} => {lambdaBody};");
        sb.AppendLine($"        {fieldName} = expr;");
        
        sb.AppendLine("    }");
        
        return sb.ToString();
    }

    /// <summary>
    /// Generates the expression tree initialization code for named types.
    /// This creates a lambda expression once and caches it, then uses it with IQueryable.
    /// </summary>
    /// <param name="lambdaBody">The lambda body code (the "new ClassName { ... }" part)</param>
    /// <param name="sourceTypeFullName">The fully qualified source type name</param>
    /// <param name="resultTypeFullName">The fully qualified result type name</param>
    /// <param name="lambdaParameterName">The lambda parameter name</param>
    /// <param name="fieldName">The field name to initialize</param>
    /// <returns>The initialization code</returns>
    public static string GenerateNamedExpressionTreeInitialization(
        string lambdaBody,
        string sourceTypeFullName,
        string resultTypeFullName,
        string lambdaParameterName,
        string fieldName
    )
    {
        var sb = new StringBuilder();
        
        // Check if field is already initialized
        sb.AppendLine($"    if ({fieldName} == null)");
        sb.AppendLine("    {");
        
        // Create the expression tree from the lambda
        // The C# compiler will automatically convert the lambda to an Expression<Func<TIn, TResult>>
        // when we explicitly type it
        sb.AppendLine($"        global::System.Linq.Expressions.Expression<global::System.Func<{sourceTypeFullName}, {resultTypeFullName}>> expr = {lambdaParameterName} => {lambdaBody};");
        sb.AppendLine($"        {fieldName} = expr;");
        
        sb.AppendLine("    }");
        
        return sb.ToString();
    }
}
