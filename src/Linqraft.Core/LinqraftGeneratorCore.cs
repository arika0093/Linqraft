using Linqraft.Core.Configuration;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core;

/// <summary>
/// Provides the shared incremental-generator entry point for Linqraft packages.
/// </summary>
public abstract class LinqraftGeneratorCore<TOptions> : IIncrementalGenerator
    where TOptions : LinqraftGeneratorOptionsCore, new()
{
    /// <summary>
    /// Initializes the Linqraft incremental generator pipeline.
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        Linqraft.SourceGenerator.LinqraftGeneratorPipeline.Initialize(context, new TOptions());
    }
}
