using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

public class TernaryNullCheckToConditionalCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_SimpleCase_InsideSelectExpr()
    {
        var test =
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            {{TestSourceCodes.SelectExprWithFuncInLinqraftNamespace}}

            class Sample
            {
                public Nest? Nest { get; set; }
            }

            class Nest
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            class Test
            {
                void Method()
                {
                    var data = new List<Sample>();
                    var result = data.AsQueryable().SelectExpr(s => new
                    {
                        NestedData = {|#0:s.Nest != null
                            ? new {
                                Id = s.Nest.Id,
                                Name = s.Nest.Name
                            }
                            : null|}
                    });
                }
            }
            """;

        var fixedCode =
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            {{TestSourceCodes.SelectExprWithFuncInLinqraftNamespace}}

            class Sample
            {
                public Nest? Nest { get; set; }
            }

            class Nest
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            class Test
            {
                void Method()
                {
                    var data = new List<Sample>();
                    var result = data.AsQueryable().SelectExpr(s => new
                    {
                        NestedData = new
                        {
                            Id = s.Nest?.Id,
                            Name = s.Nest?.Name
                        }
                    });
                }
            }
            """;

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_NestedNullChecks_InsideSelectExpr()
    {
        var test =
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            {{TestSourceCodes.SelectExprWithFuncInLinqraftNamespace}}

            class Sample
            {
                public Nest? Nest { get; set; }
            }

            class Nest
            {
                public int Id { get; set; }
                public Child? Child { get; set; }
            }

            class Child
            {
                public string Name { get; set; }
            }

            class Test
            {
                void Method()
                {
                    var data = new List<Sample>();
                    var result = data.AsQueryable().SelectExpr(s => new
                    {
                        NestedData = {|#0:s.Nest != null && s.Nest.Child != null
                            ? new {
                                Id = s.Nest.Id,
                                ChildName = s.Nest.Child.Name
                            }
                            : null|}
                    });
                }
            }
            """;

        var fixedCode =
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            {{TestSourceCodes.SelectExprWithFuncInLinqraftNamespace}}

            class Sample
            {
                public Nest? Nest { get; set; }
            }

            class Nest
            {
                public int Id { get; set; }
                public Child? Child { get; set; }
            }

            class Child
            {
                public string Name { get; set; }
            }

            class Test
            {
                void Method()
                {
                    var data = new List<Sample>();
                    var result = data.AsQueryable().SelectExpr(s => new
                    {
                        NestedData = new
                        {
                            Id = s.Nest?.Id,
                            ChildName = s.Nest?.Child?.Name
                        }
                    });
                }
            }
            """;

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_InvertedCondition_InsideSelectExpr()
    {
        // Test the inverted case: condition ? null : new{}
        var test =
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            {{TestSourceCodes.SelectExprWithFuncInLinqraftNamespace}}

            class Parent
            {
                public Child? Child { get; set; }
            }

            class Child
            {
                public string Name { get; set; }
            }

            class ChildDto
            {
                public string Name { get; set; }
            }

            class Test
            {
                void Method()
                {
                    var data = new List<Parent>();
                    var result = data.AsQueryable().SelectExpr(p => new
                    {
                        ChildInfo = {|#0:p.Child == null 
                            ? null 
                            : new ChildDto {
                                Name = p.Child.Name
                            }|}
                    });
                }
            }
            """;

        var fixedCode =
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            {{TestSourceCodes.SelectExprWithFuncInLinqraftNamespace}}

            class Parent
            {
                public Child? Child { get; set; }
            }

            class Child
            {
                public string Name { get; set; }
            }

            class ChildDto
            {
                public string Name { get; set; }
            }

            class Test
            {
                void Method()
                {
                    var data = new List<Parent>();
                    var result = data.AsQueryable().SelectExpr(p => new
                    {
                        ChildInfo = new ChildDto
                        {
                            Name = p.Child?.Name
                        }
                    });
                }
            }
            """;

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource
    )
    {
        var test = new CSharpCodeFixTest<
            TernaryNullCheckToConditionalAnalyzer,
            TernaryNullCheckToConditionalCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
