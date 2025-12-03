using System.Text;
using Linqraft.Core;
using Linqraft.Core.Formatting;
using Linqraft.Playground.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Playground.Services;

/// <summary>
/// Service for Linqraft code generation using Roslyn
/// This uses the actual Linqraft.Core library for semantic analysis and code generation.
/// Uses SharedCompilationService for consistent compilation across services.
/// </summary>
public class CodeGenerationService(SharedCompilationService sharedCompilation)
{
    /// <summary>
    /// If true, drops internal attributes from generated DTO classes for cleaner display in the playground.
    /// </summary>
    public bool IsDropAttributes { get; set; } = true;

    /// <summary>
    /// Generates Linqraft output based on multiple source files
    /// </summary>
    public GeneratedOutput GenerateOutput(
        IEnumerable<ProjectFile> files,
        LinqraftConfiguration? config = null
    )
    {
        config ??= new LinqraftConfiguration { CommentOutput = CommentOutputMode.None };

        try
        {
            var filesList = files.ToList();
            if (filesList.Count == 0)
            {
                return new GeneratedOutput
                {
                    QueryExpression = "// No source files provided",
                    DtoClass = "// No DTO class generated",
                };
            }

            // Create shared compilation with all source files
            sharedCompilation.CreateCompilation(filesList);

            // Find SelectExpr invocations from all user syntax trees
            var allSelectExprInfos = new List<SelectExprInfo>();
            foreach (var syntaxTree in sharedCompilation.GetUserSyntaxTrees())
            {
                var semanticModel = sharedCompilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot();
                var infos = FindSelectExprInvocations(root, semanticModel);
                allSelectExprInfos.AddRange(infos);
            }

            if (allSelectExprInfos.Count == 0)
            {
                return new GeneratedOutput
                {
                    QueryExpression = "// No SelectExpr call found in the code",
                    DtoClass = "// No DTO class generated",
                };
            }

            // Generate code for each SelectExpr
            var queryExpressionBuilder = new StringBuilder();
            var dtoClassBuilder = new StringBuilder();

            foreach (var info in allSelectExprInfos)
            {
                try
                {
                    info.Configuration = config;
                    var targetNamespace = info.GetNamespaceString();
                    var semanticModel = sharedCompilation.GetSemanticModel(
                        info.Invocation.SyntaxTree
                    );
                    var location = semanticModel.GetInterceptableLocation(info.Invocation);
                    var selectExprCodes = info.GenerateSelectExprCodes(location!);
                    var dtoClasses = info.GenerateDtoClasses()
                        .GroupBy(c => c.FullName)
                        .Select(g => g.First())
                        .ToList();

                    queryExpressionBuilder.AppendLine(
                        GenerateSourceCodeSnippets.BuildExprCodeSnippets(selectExprCodes)
                    );
                    dtoClassBuilder.AppendLine(
                        GenerateSourceCodeSnippets.BuildDtoCodeSnippetsGroupedByNamespace(
                            dtoClasses,
                            config
                        )
                    );
                }
                catch (Exception ex)
                {
                    queryExpressionBuilder.AppendLine(
                        $"// Error processing SelectExpr: {ex.Message}"
                    );
                }
            }

            var expressionCode = queryExpressionBuilder.ToString().TrimEnd();
            var dtoCode = dtoClassBuilder.ToString().TrimEnd();

            // Filter out internal attributes from the DTO output for cleaner display
            dtoCode = FilterInternalAttributes(dtoCode);

            // Add generated code to the shared compilation for accurate highlighting
            sharedCompilation.AddGeneratedCode(expressionCode, dtoCode);

            return new GeneratedOutput { QueryExpression = expressionCode, DtoClass = dtoCode };
        }
        catch (Exception ex)
        {
            return new GeneratedOutput { ErrorMessage = $"Error generating output: {ex.Message}" };
        }
    }

    /// <summary>
    /// Filters out internal attributes from the generated DTO code for cleaner playground display.
    /// </summary>
    private string FilterInternalAttributes(string dtoCode)
    {
        var lines = dtoCode.Split('\n');
        var filteredLines = lines.Where(line =>
        {
            var trimmed = line.Trim();
            // Skip lines containing attributes
            return IsDropAttributes && !(trimmed.StartsWith('[') && trimmed.EndsWith(']'));
        });
        return string.Join("\n", filteredLines);
    }

    private static List<SelectExprInfo> FindSelectExprInvocations(
        SyntaxNode root,
        SemanticModel semanticModel
    )
    {
        var results = new List<SelectExprInfo>();

        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            if (!IsSelectExprInvocation(invocation))
                continue;

            var info = GetSelectExprInfo(invocation, semanticModel);
            if (info != null)
            {
                results.Add(info);
            }
        }

        return results;
    }

    private static bool IsSelectExprInvocation(InvocationExpressionSyntax invocation)
    {
        var expression = invocation.Expression;
        return SelectExprHelper.IsSelectExprInvocationSyntax(expression);
    }

    private static SelectExprInfo? GetSelectExprInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        // Get lambda expression from arguments
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var lambdaArg = invocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is not LambdaExpressionSyntax lambda)
            return null;

        // Extract lambda parameter name
        var lambdaParamName = GetLambdaParameterName(lambda);

        // Extract capture argument info (if present)
        var (captureArgExpr, captureType) = GetCaptureInfo(invocation, semanticModel);

        var body = lambda.Body;

        // Check for different SelectExpr patterns
        if (body is ObjectCreationExpressionSyntax objCreation)
        {
            return GetNamedSelectExprInfo(
                invocation,
                objCreation,
                lambdaParamName,
                semanticModel,
                captureArgExpr,
                captureType
            );
        }

        if (
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name is GenericNameSyntax genericName
            && genericName.TypeArgumentList.Arguments.Count >= 2
            && body is AnonymousObjectCreationExpressionSyntax anonSyntax
        )
        {
            return GetExplicitDtoSelectExprInfo(
                invocation,
                anonSyntax,
                genericName,
                lambdaParamName,
                semanticModel,
                captureArgExpr,
                captureType
            );
        }

        if (body is AnonymousObjectCreationExpressionSyntax anon)
        {
            return GetAnonymousSelectExprInfo(
                invocation,
                anon,
                lambdaParamName,
                semanticModel,
                captureArgExpr,
                captureType
            );
        }

        return null;
    }

    private static string GetLambdaParameterName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren
                when paren.ParameterList.Parameters.Count > 0 => paren
                .ParameterList
                .Parameters[0]
                .Identifier
                .Text,
            _ => "x",
        };
    }

    private static (ExpressionSyntax? captureArgExpr, ITypeSymbol? captureType) GetCaptureInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        ExpressionSyntax? captureArgExpr = null;
        ITypeSymbol? captureType = null;
        if (invocation.ArgumentList.Arguments.Count == 2)
        {
            captureArgExpr = invocation.ArgumentList.Arguments[1].Expression;
            var typeInfo = semanticModel.GetTypeInfo(captureArgExpr);
            captureType = typeInfo.Type ?? typeInfo.ConvertedType;
        }
        return (captureArgExpr, captureType);
    }

    private static SelectExprInfoAnonymous? GetAnonymousSelectExprInfo(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        string lambdaParameterName,
        SemanticModel semanticModel,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        var namespaceDecl = invocation
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var callerNamespace = namespaceDecl?.Name.ToString() ?? "";

        return new SelectExprInfoAnonymous
        {
            SourceType = sourceType,
            AnonymousObject = anonymousObj,
            SemanticModel = semanticModel,
            Invocation = invocation,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = callerNamespace,
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }

    private static SelectExprInfoNamed? GetNamedSelectExprInfo(
        InvocationExpressionSyntax invocation,
        ObjectCreationExpressionSyntax obj,
        string lambdaParameterName,
        SemanticModel semanticModel,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        var namespaceDecl = invocation
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var callerNamespace = namespaceDecl?.Name.ToString() ?? "";

        return new SelectExprInfoNamed
        {
            SourceType = sourceType,
            ObjectCreation = obj,
            SemanticModel = semanticModel,
            Invocation = invocation,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = callerNamespace,
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }

    private static SelectExprInfoExplicitDto? GetExplicitDtoSelectExprInfo(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        GenericNameSyntax genericName,
        string lambdaParameterName,
        SemanticModel semanticModel,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        var typeArguments = genericName.TypeArgumentList.Arguments;
        if (typeArguments.Count < 2)
            return null;

        var tResultType = semanticModel.GetTypeInfo(typeArguments[1]).Type;
        if (tResultType is null)
            return null;

        var explicitDtoName = tResultType.Name;

        var parentClasses = new List<string>();
        var currentContaining = tResultType.ContainingType;
        while (currentContaining is not null)
        {
            parentClasses.Insert(0, currentContaining.Name);
            currentContaining = currentContaining.ContainingType;
        }

        var namespaceDecl = invocation
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var targetNamespace = namespaceDecl?.Name.ToString() ?? "";

        return new SelectExprInfoExplicitDto
        {
            SourceType = sourceType,
            AnonymousObject = anonymousObj,
            SemanticModel = semanticModel,
            Invocation = invocation,
            ExplicitDtoName = explicitDtoName,
            TargetNamespace = targetNamespace,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = targetNamespace,
            ParentClasses = parentClasses,
            TResultType = tResultType,
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }
}
