using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.AnalyzerHelpers;
using Linqraft.Core.Pipeline.Parsing;

namespace Linqraft.Core.Pipeline.Analysis;

/// <summary>
/// Analyzer for detecting and extracting captured variables in lambda expressions.
/// Identifies local variables, parameters, and fields that need to be captured.
/// </summary>
internal class CaptureAnalyzer : ISemanticAnalyzer
{
    private readonly SemanticModel _semanticModel;

    public CaptureAnalyzer(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
    }

    /// <inheritdoc/>
    public AnalyzedSyntax Analyze(Parsing.ParsedSyntax parsed)
    {
        var capturedVariables = new HashSet<string>();

        if (parsed.LambdaBody != null && parsed.LambdaParameterName != null)
        {
            var lambda = LambdaParsingHelper.FindLambdaInArguments(parsed.OriginalNode);
            if (lambda != null)
            {
                var lambdaParameters = ImmutableHashSet.Create(parsed.LambdaParameterName);
                capturedVariables = CaptureHelper.FindSimpleVariablesToCapture(
                    lambda,
                    lambdaParameters,
                    _semanticModel
                );
            }
        }

        return new AnalyzedSyntax
        {
            ParsedSyntax = parsed,
            CapturedVariables = capturedVariables
        };
    }
}
