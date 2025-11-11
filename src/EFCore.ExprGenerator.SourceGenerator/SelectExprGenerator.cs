using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCore.ExprGenerator;

/// <summary>
/// Generator for SelectExpr method
/// </summary>
[Generator]
public class SelectExprGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Initialize the generator
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Debug: Verify that Source Generator is running
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("DebugInfo.g.cs", @"// Source Generator is running");
        });

        // Provider to detect SelectExpr method invocations
        var invocations = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsSelectExprInvocation(node),
                transform: static (ctx, _) => GetSelectExprInfo(ctx)
            )
            .Where(static info => info is not null)
            .Collect();

        // Code generation
        context.RegisterSourceOutput(
            invocations,
            (spc, infos) =>
            {
                // Remove duplicates based on unique ID
                var uniqueInfos = new Dictionary<string, SelectExprInfo>();
                foreach (var info in infos)
                {
                    if (info is null)
                        continue;
                    var uniqueId = GetUniqueIdForInfo(info);
                    if (!uniqueInfos.ContainsKey(uniqueId))
                    {
                        uniqueInfos[uniqueId] = info;
                    }
                }

                // Generate code for each unique info
                foreach (var info in uniqueInfos.Values)
                {
                    GenerateCode(spc, info);
                }
            }
        );
    }

    private static string GetUniqueIdForInfo(SelectExprInfo info)
    {
        // Analyze anonymous type structure
        var dtoStructure = AnalyzeAnonymousType(
            info.AnonymousObject,
            info.SemanticModel,
            info.SourceType
        );
        return GenerateUniqueId(dtoStructure);
    }

    private static bool IsSelectExprInvocation(SyntaxNode node)
    {
        // Detect InvocationExpression with method name "SelectExpr"
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        var expression = invocation.Expression;

        // MemberAccessExpression (e.g., query.SelectExpr)
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == "SelectExpr";
        }

        return false;
    }

    private static SelectExprInfo? GetSelectExprInfo(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Get lambda expression from arguments
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var lambdaArg = invocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is not LambdaExpressionSyntax lambda)
            return null;

        // Check if lambda body is an anonymous object initializer
        var body = lambda.Body;
        AnonymousObjectCreationExpressionSyntax? anonymousObj = body switch
        {
            AnonymousObjectCreationExpressionSyntax anon => anon,
            _ => null,
        };

        if (anonymousObj is null)
            return null;

        // Get target type from MemberAccessExpression
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Get type information
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        // Get T from IQueryable<T>
        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        return new SelectExprInfo(
            SourceType: sourceType,
            AnonymousObject: anonymousObj,
            SemanticModel: semanticModel,
            Invocation: invocation
        );
    }

    private static void GenerateCode(SourceProductionContext context, SelectExprInfo info)
    {
        try
        {
            // Analyze anonymous type structure
            var dtoStructure = AnalyzeAnonymousType(
                info.AnonymousObject,
                info.SemanticModel,
                info.SourceType
            );

            // Skip if properties are empty
            if (dtoStructure.Properties.Count == 0)
                return;

            // Generate unique ID (from hash of property structure)
            var uniqueId = GenerateUniqueId(dtoStructure);

            // Get namespace
            var namespaceSymbol = info.SourceType.ContainingNamespace;
            var namespaceName = namespaceSymbol?.ToDisplayString() ?? "Generated";

            // Generate DTO classes (including nested DTOs)
            var dtoClasses = new List<string>();
            var mainDtoName = GenerateDtoClasses(dtoStructure, uniqueId, dtoClasses, namespaceName);
            var mainDtoFullName = $"global::{namespaceName}.{mainDtoName}";

            // Generate SelectExpr method
            var selectExprMethod = GenerateSelectExprMethod(mainDtoFullName, dtoStructure);

            // Build final source code
            var sourceCode = BuildSourceCode(
                namespaceName,
                mainDtoName,
                dtoClasses,
                selectExprMethod
            );

            // Register with Source Generator
            context.AddSource($"GeneratedExpression_{uniqueId}.g.cs", sourceCode);
        }
        catch (Exception ex)
        {
            // Output error information for debugging
            var errorMessage =
                $@"// Source Generator Error: {ex.Message}
// Stack Trace: {ex.StackTrace}
";
            context.AddSource("GeneratorError.g.cs", errorMessage);
        }
    }

    private static string GenerateDtoClasses(
        DtoStructure structure,
        string uniqueId,
        List<string> dtoClasses,
        string namespaceName
    )
    {
        var dtoName = $"{structure.SourceTypeName}Dto_{uniqueId}";

        var sb = new StringBuilder();
        sb.AppendLine($"    public class {dtoName}");
        sb.AppendLine("    {");

        foreach (var prop in structure.Properties)
        {
            var propertyType = prop.TypeName;
            // If propertyType is a generic type, use only the base type
            if (propertyType.Contains("<"))
            {
                propertyType = propertyType[..propertyType.IndexOf("<")];
            }

            // For nested structures, recursively generate DTOs (add first)
            if (prop.NestedStructure is not null)
            {
                var nestedId = GenerateUniqueId(prop.NestedStructure);
                var nestedDtoName = GenerateDtoClasses(
                    prop.NestedStructure,
                    nestedId,
                    dtoClasses,
                    namespaceName
                );
                // Since propertyType already has a fully qualified name starting with global::,
                // add global:: to nestedDtoName as well
                var nestedDtoFullName = $"global::{namespaceName}.{nestedDtoName}";
                propertyType = $"{propertyType}<{nestedDtoFullName}>";
            }
            sb.AppendLine($"        public required {propertyType} {prop.Name} {{ get; set; }}");
        }

        sb.AppendLine("    }");

        // Add current DTO (nested DTOs are already added by recursive calls)
        dtoClasses.Add(sb.ToString());
        return dtoName;
    }

    private static string GenerateSelectExprMethod(string dtoName, DtoStructure structure)
    {
        var sourceTypeFullName = structure.SourceTypeFullName;
        var sb = new StringBuilder();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// generated method");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        public static IQueryable<{dtoName}> SelectExpr<TResult>(");
        sb.AppendLine($"            this IQueryable<{sourceTypeFullName}> query,");
        sb.AppendLine($"            Func<{sourceTypeFullName}, TResult> selector)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return query.Select(s => new {dtoName}");
        sb.AppendLine("            {");

        // Generate property assignments
        var propertyAssignments = new List<string>();
        foreach (var prop in structure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop);
            propertyAssignments.Add($"                {prop.Name} = {assignment}");
        }

        sb.AppendLine(string.Join(",\n", propertyAssignments));
        sb.AppendLine("            });");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    private static string GeneratePropertyAssignment(DtoProperty property)
    {
        var expression = property.OriginalExpression;

        // For nested Select (collection) case
        if (property.NestedStructure is not null)
        {
            var converted = ConvertNestedSelect(expression, property.NestedStructure);
            // Debug: Check if conversion was performed correctly
            if (converted == expression && expression.Contains("Select"))
            {
                // If conversion was not performed, leave the original expression as a comment
                return $"{converted} /* CONVERSION FAILED: {property.Name} */";
            }
            return converted;
        }

        // If nullable operator is used, convert to explicit null check
        if (property.IsNullable && expression.Contains("?."))
        {
            return ConvertNullableAccessToExplicitCheck(expression);
        }

        // Regular property access
        return expression;
    }

    private static string ConvertNestedSelect(string expression, DtoStructure nestedStructure)
    {
        // Example: s.Childs.Select(c => new { ... })
        // Extract parameter name (e.g., "c")
        // Consider the possibility of whitespace or generic type parameters after .Select
        var selectIndex = expression.IndexOf(".Select");
        if (selectIndex == -1)
            return expression;

        // Find '(' after Select (start of lambda)
        var lambdaStart = expression.IndexOf("(", selectIndex);
        if (lambdaStart == -1)
            return expression;

        var lambdaArrow = expression.IndexOf("=>", lambdaStart);
        if (lambdaArrow == -1 || lambdaArrow <= lambdaStart + 1)
            return expression;

        var paramName = expression.Substring(lambdaStart + 1, lambdaArrow - lambdaStart - 1).Trim();
        if (string.IsNullOrEmpty(paramName))
            paramName = "x"; // Default parameter name

        var baseExpression = expression[..selectIndex];
        var uniqueId = GenerateUniqueId(nestedStructure);
        var nestedDtoName = $"{nestedStructure.SourceTypeFullName}Dto_{uniqueId}";

        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop);
            // For nested Select, convert to inline to avoid multi-line
            assignment = assignment.Replace("\n", " ").Replace("\r", "");
            propertyAssignments.Add($"{prop.Name} = {assignment}");
        }

        var propertiesCode = string.Join(", ", propertyAssignments);
        return $"{baseExpression}.Select({paramName} => new {nestedDtoName} {{ {propertiesCode} }})";
    }

    private static string ConvertNullableAccessToExplicitCheck(string expression)
    {
        // Example: c.Child?.Id → c.Child != null ? c.Child.Id : null
        // Example: s.Child3?.Child?.Id → s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null

        if (!expression.Contains("?."))
            return expression;

        // Replace ?. with . to create the actual access path
        var accessPath = expression.Replace("?.", ".");

        // Find where ?. occurs and build null checks
        var checks = new List<string>();
        var parts = expression.Split(["?."], StringSplitOptions.None);

        if (parts.Length < 2)
            return expression;

        // All parts except the first require null checks
        var currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            checks.Add($"{currentPath} != null");

            // Get the first token (property name) of the next part
            var nextPart = parts[i];
            var dotIndex = nextPart.IndexOf('.');
            var propertyName = dotIndex > 0 ? nextPart[..dotIndex] : nextPart;

            currentPath = $"{currentPath}.{propertyName}";
        }

        if (checks.Count == 0)
            return expression;

        // Build null checks
        var nullCheckPart = string.Join(" && ", checks);

        return $"{nullCheckPart} ? {accessPath} : default";
    }

    private static string BuildSourceCode(
        string namespaceName,
        string mainDtoName,
        List<string> dtoClasses,
        string selectExprMethod
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable IDE0060");
        sb.AppendLine("#pragma warning disable CS8601");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine($"    internal static partial class GeneratedExpression_{mainDtoName}");
        sb.AppendLine("    {");
        sb.AppendLine(selectExprMethod);
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var dtoClass in dtoClasses)
        {
            sb.AppendLine(dtoClass);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static DtoStructure AnalyzeAnonymousType(
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        SemanticModel semanticModel,
        ITypeSymbol sourceType
    )
    {
        var properties = new List<DtoProperty>();

        foreach (var initializer in anonymousObj.Initializers)
        {
            string propertyName;
            var expression = initializer.Expression;

            // For explicit property names (e.g., Id = s.Id)
            if (initializer.NameEquals is not null)
            {
                propertyName = initializer.NameEquals.Name.Identifier.Text;
            }
            // For implicit property names (e.g., s.Id)
            else
            {
                // Get property name inferred from expression
                var name = GetImplicitPropertyName(expression);
                if (name is null)
                {
                    continue;
                }
                propertyName = name;
            }

            var property = AnalyzeExpression(propertyName, expression, semanticModel);
            if (property is not null)
                properties.Add(property);
        }

        return new DtoStructure(
            SourceTypeName: sourceType.Name,
            SourceTypeFullName: sourceType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            ),
            Properties: properties
        );
    }

    private static string? GetImplicitPropertyName(ExpressionSyntax expression)
    {
        // Get property name from member access (e.g., s.Id)
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }

        // Get property name from identifier (e.g., id)
        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }

        // Do not process other complex expressions
        return null;
    }

    private static DtoProperty? AnalyzeExpression(
        string propertyName,
        ExpressionSyntax expression,
        SemanticModel semanticModel
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type is null)
            return null;

        var propertyType = typeInfo.Type;
        var isNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated;

        // Check if nullable operator ?. is used
        var hasNullableAccess = HasNullableAccess(expression);

        // Detect nested Select (e.g., s.Childs.Select(...))
        DtoStructure? nestedStructure = null;
        if (expression is InvocationExpressionSyntax nestedInvocation)
        {
            // Check if it's a Select method invocation
            if (
                nestedInvocation.Expression is MemberAccessExpressionSyntax nestedMemberAccess
                && nestedMemberAccess.Name.Identifier.Text == "Select"
            )
            {
                // Analyze anonymous type in lambda expression
                if (nestedInvocation.ArgumentList.Arguments.Count > 0)
                {
                    var lambdaArg = nestedInvocation.ArgumentList.Arguments[0].Expression;
                    if (
                        lambdaArg is LambdaExpressionSyntax nestedLambda
                        && nestedLambda.Body
                            is AnonymousObjectCreationExpressionSyntax nestedAnonymous
                    )
                    {
                        // Get collection element type
                        var collectionType = semanticModel
                            .GetTypeInfo(nestedMemberAccess.Expression)
                            .Type;
                        if (
                            collectionType is INamedTypeSymbol namedCollectionType
                            && namedCollectionType.TypeArguments.Length > 0
                        )
                        {
                            var elementType = namedCollectionType.TypeArguments[0];
                            nestedStructure = AnalyzeAnonymousType(
                                nestedAnonymous,
                                semanticModel,
                                elementType
                            );
                        }
                    }
                }
            }
        }

        return new DtoProperty(
            Name: propertyName,
            TypeName: propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsNullable: isNullable || hasNullableAccess,
            OriginalExpression: expression.ToString(),
            NestedStructure: nestedStructure
        );
    }

    private static bool HasNullableAccess(ExpressionSyntax expression)
    {
        // Check if ?. operator is used
        return expression.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().Any();
    }

    private static string GenerateUniqueId(DtoStructure structure)
    {
        // Generate hash from property structure
        var signature = string.Join(
            "|",
            structure.Properties.Select(p => $"{p.Name}:{p.TypeName}:{p.IsNullable}")
        );

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signature));
        return BitConverter.ToString(hash).Replace("-", "")[..8]; // Use first 8 characters
    }

    private record SelectExprInfo(
        ITypeSymbol SourceType,
        AnonymousObjectCreationExpressionSyntax AnonymousObject,
        SemanticModel SemanticModel,
        InvocationExpressionSyntax Invocation
    );

    private record DtoStructure(
        string SourceTypeName,
        string SourceTypeFullName,
        List<DtoProperty> Properties
    );

    private record DtoProperty(
        string Name,
        string TypeName,
        bool IsNullable,
        string OriginalExpression,
        DtoStructure? NestedStructure
    );
}
