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
