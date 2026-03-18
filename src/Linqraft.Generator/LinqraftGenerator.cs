using Linqraft.Core;
using Microsoft.CodeAnalysis;

namespace Linqraft.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class LinqraftGenerator : LinqraftGeneratorCore<LinqraftGeneratorOptions>;
