namespace Linqraft.Core.Pipeline;

/// <summary>
/// Base interface for all pipeline stages.
/// Each stage transforms input of type TInput to output of type TOutput.
/// </summary>
/// <typeparam name="TInput">The input type for this stage</typeparam>
/// <typeparam name="TOutput">The output type for this stage</typeparam>
internal interface IPipelineStage<TInput, TOutput>
{
    /// <summary>
    /// Processes the input and produces the output.
    /// </summary>
    /// <param name="input">The input to process</param>
    /// <returns>The processed output</returns>
    TOutput Process(TInput input);
}
