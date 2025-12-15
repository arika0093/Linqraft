using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.Pipeline.Analysis;
using Linqraft.Core.Pipeline.Parsing;
using Linqraft.Core.Pipeline.Transformation;
using Linqraft.Core.Pipeline.Generation;

namespace Linqraft.Core.Pipeline;

/// <summary>
/// The main orchestrator for the code generation pipeline.
/// Coordinates parsing, analysis, transformation, and generation stages.
/// </summary>
internal class CodeGenerationPipeline
{
    private readonly SemanticModel _semanticModel;
    private readonly LinqraftConfiguration _configuration;
    private PropertyAssignmentGenerator? _propertyAssignmentGenerator;
    private NullCheckGenerator? _nullCheckGenerator;

    /// <summary>
    /// Creates a new code generation pipeline.
    /// </summary>
    /// <param name="semanticModel">The semantic model for type resolution</param>
    /// <param name="configuration">The Linqraft configuration</param>
    public CodeGenerationPipeline(SemanticModel semanticModel, LinqraftConfiguration configuration)
    {
        _semanticModel = semanticModel;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the semantic model.
    /// </summary>
    public SemanticModel SemanticModel => _semanticModel;

    /// <summary>
    /// Gets the configuration.
    /// </summary>
    public LinqraftConfiguration Configuration => _configuration;

    /// <summary>
    /// Gets the property assignment generator.
    /// </summary>
    public PropertyAssignmentGenerator PropertyAssignmentGenerator
    {
        get
        {
            _propertyAssignmentGenerator ??= new PropertyAssignmentGenerator(_semanticModel, _configuration);
            return _propertyAssignmentGenerator;
        }
    }

    /// <summary>
    /// Gets the null check generator.
    /// </summary>
    public NullCheckGenerator NullCheckGenerator
    {
        get
        {
            _nullCheckGenerator ??= new NullCheckGenerator(_semanticModel, _configuration);
            return _nullCheckGenerator;
        }
    }

    /// <summary>
    /// Parses a lambda expression from an invocation.
    /// </summary>
    /// <param name="invocation">The invocation expression</param>
    /// <returns>The parsed syntax information</returns>
    public ParsedSyntax ParseLambda(InvocationExpressionSyntax invocation)
    {
        var context = new PipelineContext
        {
            TargetNode = invocation,
            SemanticModel = _semanticModel
        };

        var parser = new LambdaAnonymousTypeParser();
        return parser.Parse(context);
    }

    /// <summary>
    /// Analyzes the parsed syntax to extract type information.
    /// </summary>
    /// <param name="parsed">The parsed syntax</param>
    /// <returns>The analyzed syntax with type information</returns>
    public AnalyzedSyntax AnalyzeTypes(ParsedSyntax parsed)
    {
        var analyzer = new TypeAnalyzer(_semanticModel);
        return analyzer.Analyze(parsed);
    }

    /// <summary>
    /// Analyzes captured variables in a lambda expression.
    /// </summary>
    /// <param name="parsed">The parsed syntax</param>
    /// <returns>The analyzed syntax with capture information</returns>
    public AnalyzedSyntax AnalyzeCaptures(ParsedSyntax parsed)
    {
        var analyzer = new CaptureAnalyzer(_semanticModel);
        return analyzer.Analyze(parsed);
    }

    /// <summary>
    /// Transforms an expression using the transformation pipeline.
    /// </summary>
    /// <param name="expression">The expression to transform</param>
    /// <param name="expectedType">The expected type of the result</param>
    /// <returns>The transformed expression</returns>
    public ExpressionSyntax TransformExpression(ExpressionSyntax expression, ITypeSymbol expectedType)
    {
        var context = new TransformContext
        {
            Expression = expression,
            SemanticModel = _semanticModel,
            ExpectedType = expectedType
        };

        var pipeline = new TransformationPipeline(
            new NullConditionalTransformer(),
            new FullyQualifyingTransformer()
        );

        return pipeline.Transform(context);
    }

    /// <summary>
    /// Fully qualifies all references in an expression.
    /// </summary>
    /// <param name="expression">The expression to process</param>
    /// <param name="expectedType">The expected type</param>
    /// <returns>The expression with fully qualified names as a string</returns>
    public string FullyQualifyExpression(ExpressionSyntax expression, ITypeSymbol expectedType)
    {
        return PropertyAssignmentGenerator.FullyQualifyExpression(expression, expectedType);
    }

    /// <summary>
    /// Converts a null-conditional expression to an explicit null check.
    /// </summary>
    /// <param name="expression">The expression with null-conditional access</param>
    /// <param name="typeSymbol">The type of the expression result</param>
    /// <returns>The converted expression with explicit null check</returns>
    public string ConvertToExplicitNullCheck(ExpressionSyntax expression, ITypeSymbol typeSymbol)
    {
        return NullCheckGenerator.ConvertToExplicitNullCheck(expression, typeSymbol);
    }

    /// <summary>
    /// Checks if an expression needs null check conversion.
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <param name="isNullable">Whether the property is nullable</param>
    /// <param name="typeSymbol">The type symbol of the expression</param>
    /// <returns>True if the expression needs null check conversion</returns>
    public bool NeedsNullCheckConversion(ExpressionSyntax expression, bool isNullable, ITypeSymbol typeSymbol)
    {
        return NullCheckGenerator.NeedsNullCheckConversion(expression, isNullable, typeSymbol);
    }

    /// <summary>
    /// Gets the default value for a type symbol.
    /// </summary>
    /// <param name="typeSymbol">The type symbol</param>
    /// <returns>The default value as a string</returns>
    public string GetDefaultValueForType(ITypeSymbol typeSymbol)
    {
        return NullCheckGenerator.GetDefaultValueForType(typeSymbol);
    }

    /// <summary>
    /// Gets the source type from an invocation expression.
    /// </summary>
    /// <param name="invocation">The invocation expression</param>
    /// <returns>The source type, or null if not found</returns>
    public ITypeSymbol? GetSourceType(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var typeInfo = _semanticModel.GetTypeInfo(memberAccess.Expression);
            if (typeInfo.Type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                return namedType.TypeArguments.Length > 0 ? namedType.TypeArguments[0] : null;
            }
        }
        return null;
    }
}
