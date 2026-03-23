using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using Linqraft.Core.Generation;
using static Linqraft.Core.Generation.CodeTemplateContents;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Generates projection support extension class.
/// </summary>
internal abstract partial class ProjectionSupportExtensionClassGenerator
{
    // Operation-specific generators only contribute the names and overload families unique to each projection shape.
    /// <summary>
    /// Generates select expr support extension class.
    /// </summary>
    private sealed class SelectExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        /// <summary>
        /// Gets class name.
        /// </summary>
        protected override string GetClassName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectExprClassName;

        /// <summary>
        /// Gets method name.
        /// </summary>
        protected override string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectExprMethodName;

        /// <summary>
        /// Gets class summary.
        /// </summary>
        protected override string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions) =>
            $"Provides the {generatorOptions.SelectExprMethodName} entry points that {generatorOptions.GeneratorDisplayName} intercepts ahead of time.";

        /// <summary>
        /// Gets method signatures.
        /// </summary>
        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads(ProjectionOperationKind.Select, _ => "TResult");
    }

    /// <summary>
    /// Generates select many expr support extension class.
    /// </summary>
    private sealed class SelectManyExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        /// <summary>
        /// Gets class name.
        /// </summary>
        protected override string GetClassName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectManyExprClassName;

        /// <summary>
        /// Gets method name.
        /// </summary>
        protected override string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectManyExprMethodName;

        /// <summary>
        /// Gets class summary.
        /// </summary>
        protected override string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions) =>
            $"Provides the {generatorOptions.SelectManyExprMethodName} entry points that {generatorOptions.GeneratorDisplayName} intercepts ahead of time.";

        /// <summary>
        /// Gets method signatures.
        /// </summary>
        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads(
                ProjectionOperationKind.SelectMany,
                _ => "global::System.Collections.Generic.IEnumerable<TResult>",
                includeAnonymousObjectPattern: false
            );
    }

    /// <summary>
    /// Generates group by expr support extension class.
    /// </summary>
    private sealed class GroupByExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        /// <summary>
        /// Gets class name.
        /// </summary>
        protected override string GetClassName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.GroupByExprClassName;

        /// <summary>
        /// Gets method name.
        /// </summary>
        protected override string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.GroupByExprMethodName;

        /// <summary>
        /// Gets class summary.
        /// </summary>
        protected override string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions) =>
            $"Provides the {generatorOptions.GroupByExprMethodName} entry points that {generatorOptions.GeneratorDisplayName} intercepts ahead of time.";

        /// <summary>
        /// Gets method signatures.
        /// </summary>
        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads(
                ProjectionOperationKind.GroupBy,
                _ => "TResult",
                hasKeySelector: true,
                includeAnonymousObjectPattern: false
            );
    }
}
