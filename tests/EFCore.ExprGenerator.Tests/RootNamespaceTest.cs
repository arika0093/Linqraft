using System;
using System.Collections.Generic;
using System.Linq;
using EFCore.ExprGenerator;

public class RootNamespaceTest
{
    [Fact]
    public void Test_GlobalNamespaceHandling()
    {
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<RootSampleClass, Test>(s => new { s.Id })
            .ToList();
    }

    private List<RootSampleClass> SampleData =
    [
        new RootSampleClass { Id = 1, Name = "Alice" },
        new RootSampleClass { Id = 2, Name = "Bob" },
        new RootSampleClass { Id = 3, Name = "Charlie" },
    ];

    internal class RootSampleClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
