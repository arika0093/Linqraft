using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// SelectExprInfo for named (predefined DTO) Select expressions
/// </summary>
public record SelectExprInfoNamed : SelectExprInfo
{
    /// <summary>
    /// The object creation expression for the named type
    /// </summary>
    public required ObjectCreationExpressionSyntax ObjectCreation { get; init; }

    /// <summary>
    /// Generates DTO classes (predefined types don't generate new classes)
    /// </summary>
    public override List<GenerateDtoClassInfo> GenerateDtoClasses() => [];

    /// <summary>
    /// Generates the DTO structure for unique ID generation
    /// </summary>
    public override DtoStructure GenerateDtoStructure()
    {
        return DtoStructure.AnalyzeNamedType(
            ObjectCreation,
            SemanticModel,
            SourceType,
            configuration: Configuration
        )!;
    }

    /// <summary>
    /// Gets the DTO class name (uses the source type name)
    /// </summary>
    public override string GetClassName(DtoStructure structure) => structure.SourceTypeName;

    /// <summary>
    /// Gets the parent DTO class name (fully qualified)
    /// </summary>
    public override string GetParentDtoClassName(DtoStructure structure) =>
        structure.SourceTypeFullName;

    /// <summary>
    /// Gets the parent DTO fully qualified name with global:: prefix
    /// Named types already have fully qualified names in SourceTypeFullName
    /// </summary>
    public override string GetParentDtoFullName(DtoStructure structure) =>
        structure.SourceTypeFullName;

    /// <summary>
    /// Gets the namespace where DTOs will be placed
    /// Named types use the DTO's own namespace
    /// </summary>
    public override string GetDtoNamespace() =>
        SourceType.ContainingNamespace?.ToDisplayString() ?? CallerNamespace;

    // Get expression type string (for documentation)
    public override string GetExprTypeString() => "predefined";

    /// <summary>
    /// Generates static field declarations for pre-built expressions (if enabled)
    /// </summary>
    public override string? GenerateStaticFields()
    {
        // Check if we should use pre-built expressions (only for IQueryable, not IEnumerable)
        var usePrebuildExpression =
            Configuration.UsePrebuildExpression && !IsEnumerableInvocation();

        // Don't generate fields if captures are used (they don't work well with closures)
        var hasCapture = CaptureArgumentExpression != null && CaptureArgumentType != null;

        if (!usePrebuildExpression || hasCapture)
        {
            return null;
        }

        var querySourceTypeFullName = SourceType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );
        var structure = GenerateDtoStructure();
        var dtoName = GetParentDtoClassName(structure);
        var id = GetUniqueId();

        // Build the lambda body
        var lambdaBodyBuilder = new StringBuilder();
        lambdaBodyBuilder.AppendLine($"new {dtoName}");
        lambdaBodyBuilder.AppendLine($"    {{");
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignment(prop, CodeFormatter.IndentSize * 2);
                return $"{CodeFormatter.Indent(2)}{prop.Name} = {assignment}";
            })
            .ToList();
        lambdaBodyBuilder.AppendLine(
            string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments)
        );
        lambdaBodyBuilder.Append("    }");

        var (fieldDecl, _) = ExpressionTreeBuilder.GenerateExpressionTreeField(
            querySourceTypeFullName,
            dtoName,
            LambdaParameterName,
            lambdaBodyBuilder.ToString(),
            id
        );

        return fieldDecl;
    }

    /// <summary>
    /// Generates the SelectExpr method code
    /// </summary>
    protected override string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    )
    {
        // Use SourceType for the query source (not the DTO return type)
        var querySourceTypeFullName = SourceType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );
        var returnTypePrefix = GetReturnTypePrefix();

        var sb = new StringBuilder();
        var id = GetUniqueId();

        // Check if we should use pre-built expressions (only for IQueryable, not IEnumerable)
        var usePrebuildExpression =
            Configuration.UsePrebuildExpression && !IsEnumerableInvocation();

        sb.AppendLine(GenerateMethodHeaderPart(dtoName, location));

        // Determine if we have capture parameters
        var hasCapture = CaptureArgumentExpression != null && CaptureArgumentType != null;

        if (usePrebuildExpression && !hasCapture)
        {
            // Get the field name (we need to generate the same hash as in GenerateStaticFields)
            var hash = HashUtility.GenerateSha256Hash(id).Substring(0, 8);
            var fieldName = $"_cachedExpression_{hash}";

            // Use the cached expression directly (no initialization needed, it's done in the field declaration)
            sb.AppendLine(
                $$"""
                public static {{returnTypePrefix}}<TResult> SelectExpr_{{id}}<TIn, TResult>(
                    this {{returnTypePrefix}}<TIn> query, Func<TIn, TResult> selector)
                {
                    return query.Provider.CreateQuery<TResult>(
                        Expression.Call(null, _methodInfo_{{id}}, query.Expression, _unaryExpression_{{id}}));
                }
                private static readonly UnaryExpression _unaryExpression_{{id}} = Expression.Quote({{fieldName}});
                private static readonly System.Reflection.MethodInfo _methodInfo_{{id}} = new Func<
                    IQueryable<{{querySourceTypeFullName}}>,
                    Expression<Func<{{querySourceTypeFullName}}, {{dtoName}}>>,
                    IQueryable<{{dtoName}}>>(Queryable.Select).Method;
                """
            );
            return sb.ToString();
        }

        if (hasCapture)
        {
            // Generate method with capture parameter that creates closure variables
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>("
            );
            sb.AppendLine(
                $"    this {returnTypePrefix}<TIn> query, Func<TIn, TResult> selector, object captureParam)"
            );
            sb.AppendLine("{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{querySourceTypeFullName}>;"
            );

            // For anonymous types, use dynamic to extract properties as closure variables
            var isAnonymousType =
                CaptureArgumentType != null && CaptureArgumentType.IsAnonymousType;
            if (isAnonymousType && CaptureArgumentType != null)
            {
                // For anonymous types, get the properties and create closure variables using dynamic
                var properties = CaptureArgumentType.GetMembers().OfType<IPropertySymbol>();
                sb.AppendLine($"    dynamic captureObj = captureParam;");
                foreach (var prop in properties)
                {
                    var propTypeName = prop.Type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    sb.AppendLine($"    {propTypeName} {prop.Name} = captureObj.{prop.Name};");
                }

                sb.Append(GenerateAnonymousCaptureMemberAccessAliases());
            }
            else
            {
                // For non-anonymous types, just cast it
                var captureTypeName =
                    CaptureArgumentType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    ?? "object";
                sb.AppendLine($"    var capture = ({captureTypeName})captureParam;");
            }

            // Note: Pre-built expressions don't work well with captures because the closure
            // variables would be captured at compile time, not at runtime. So we disable
            // pre-built expressions when captures are used.
            // Also, convert to IEnumerable to avoid expression tree compilation with dynamic
            sb.AppendLine(
                $"    var converted = matchedQuery.AsEnumerable().Select({LambdaParameterName} => new {dtoName}"
            );
        }
        else
        {
            // Generate method without capture parameter
            sb.AppendLine(
                $"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>("
            );
            sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, TResult> selector)");
            sb.AppendLine("{");
            sb.AppendLine(
                $"    var matchedQuery = query as object as {returnTypePrefix}<{querySourceTypeFullName}>;"
            );

            sb.AppendLine(
                $"    var converted = matchedQuery.Select({LambdaParameterName} => new {dtoName}"
            );
        }

        sb.AppendLine($"    {{");

        // Generate property assignments using GeneratePropertyAssignment to properly handle null-conditional operators
        var propertyAssignments = structure
            .Properties.Select(prop =>
            {
                var assignment = GeneratePropertyAssignment(prop, CodeFormatter.IndentSize * 2);
                return $"{CodeFormatter.Indent(2)}{prop.Name} = {assignment}";
            })
            .ToList();
        sb.AppendLine(string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments));

        sb.AppendLine($"    }});");

        // For methods with capture that use AsEnumerable, convert back to IQueryable
        if (hasCapture)
        {
            sb.AppendLine(
                $"    return converted.AsQueryable() as object as {returnTypePrefix}<TResult>;"
            );
        }
        else
        {
            sb.AppendLine($"    return converted as object as {returnTypePrefix}<TResult>;");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
