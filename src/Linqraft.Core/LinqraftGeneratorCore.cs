using Linqraft.Core.Configuration;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core;

public abstract class LinqraftGeneratorCore<TOptions> : IIncrementalGenerator
    where TOptions : LinqraftGeneratorOptionsCore, new()
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        Linqraft.SourceGenerator.LinqraftGeneratorPipeline.Initialize(context, new TOptions());
    }
}
