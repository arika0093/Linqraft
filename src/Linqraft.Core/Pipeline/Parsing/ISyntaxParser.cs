namespace Linqraft.Core.Pipeline.Parsing;

/// <summary>
/// Interface for syntax parsers that extract structured information from syntax nodes.
/// Used in the Parsing phase of the pipeline.
/// </summary>
internal interface ISyntaxParser
{
    /// <summary>
    /// Parses the target node in the context and extracts structured syntax information.
    /// </summary>
    /// <param name="context">The pipeline context containing the target node</param>
    /// <returns>The parsed syntax information</returns>
    ParsedSyntax Parse(PipelineContext context);
}
