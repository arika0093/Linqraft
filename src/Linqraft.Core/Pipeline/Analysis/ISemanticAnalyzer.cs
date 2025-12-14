namespace Linqraft.Core.Pipeline.Analysis;

/// <summary>
/// Interface for semantic analyzers that add semantic information to parsed syntax.
/// Used in the Semantic Analysis phase of the pipeline.
/// </summary>
internal interface ISemanticAnalyzer
{
    /// <summary>
    /// Analyzes the parsed syntax and adds semantic information.
    /// </summary>
    /// <param name="parsed">The parsed syntax to analyze</param>
    /// <returns>The analyzed result with semantic information</returns>
    AnalyzedSyntax Analyze(Parsing.ParsedSyntax parsed);
}
