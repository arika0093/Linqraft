using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.AnonymousTypeToDtoAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class AnonymousTypeToDtoAnalyzerTests
{
    [Fact]
    public async Task AnonymousType_InVariableDeclaration_ReportsDiagnostic()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        var result = {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InReturnStatement_ReportsDiagnostic()
    {
        var test =
            @"
class Test
{
    object Method()
    {
        return {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InAssignment_ReportsDiagnostic()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        object result;
        result = {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InMethodArgument_ReportsDiagnostic()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        Process({|#0:new { Id = 1, Name = ""Test"" }|});
    }

    void Process(object obj) { }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InLambda_ReportsDiagnostic()
    {
        var test =
            @"
using System;
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = list.Select(x => {|#0:new { Value = x }|});
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_Empty_NoDiagnostic()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        var result = new { };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AnonymousType_WithMultipleProperties_ReportsDiagnostic()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        var result = {|#0:new { Id = 1, Name = ""Test"", Value = 42.5, Active = true }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InConditional_ReportsDiagnostic()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        var condition = true;
        var result = condition ? {|#0:new { Id = 1 }|} : {|#1:new { Id = 2 }|};
    }
}";

        var expected1 = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);
        var expected2 = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task AnonymousType_InArrayInitializer_ReportsDiagnostic()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        var result = new[] { {|#0:new { Id = 1 }|}, {|#1:new { Id = 2 }|} };
    }
}";

        var expected1 = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);
        var expected2 = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task AnonymousType_WithNullableAccess_ReportsDiagnostic()
    {
        var test =
            @"
class Child
{
    public string Name { get; set; }
    public int Value { get; set; }
}

class Parent
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Test
{
    void Method()
    {
        var parent = new Parent { Id = 1 };
        var result = {|#0:new { parent.Id, ChildName = parent.Child?.Name, ChildValue = parent.Child?.Value }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithNestedNullableAccess_ReportsDiagnostic()
    {
        var test =
            @"
class GrandChild
{
    public string Details { get; set; }
}

class Child
{
    public string Name { get; set; }
    public GrandChild? GrandChild { get; set; }
}

class Parent
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Test
{
    void Method()
    {
        var parent = new Parent { Id = 1 };
        var result = {|#0:new
        {
            parent.Id,
            ChildName = parent.Child?.Name,
            GrandChildDetails = parent.Child?.GrandChild?.Details
        }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithCollectionProperty_ReportsDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;

class Item
{
    public string Name { get; set; }
    public int Value { get; set; }
}

class Container
{
    public int Id { get; set; }
    public List<Item> Items { get; set; }
}

class Test
{
    void Method()
    {
        var container = new Container { Id = 1 };
        var result = {|#0:new { container.Id, container.Items }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithNestedSelect_ReportsDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Linq;

class Item
{
    public string Name { get; set; }
    public int Value { get; set; }
}

class Container
{
    public int Id { get; set; }
    public List<Item> Items { get; set; }
}

class Test
{
    void Method()
    {
        var container = new Container { Id = 1, Items = new List<Item>() };
        var result = {|#0:new
        {
            container.Id,
            ItemNames = container.Items.Select(i => i.Name).ToList(),
            ItemValues = container.Items.Select(i => i.Value).ToList()
        }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithNestedAnonymousTypeInSelect_ReportsMultipleDiagnostics()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Linq;

class Item
{
    public string Name { get; set; }
    public int Value { get; set; }
}

class Container
{
    public int Id { get; set; }
    public List<Item> Items { get; set; }
}

class Test
{
    void Method()
    {
        var container = new Container { Id = 1, Items = new List<Item>() };
        var result = {|#0:new
        {
            container.Id,
            Items = container.Items.Select(i => {|#1:new { i.Name, i.Value }|}).ToList()
        }|};
    }
}";

        var expected1 = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);
        var expected2 = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task AnonymousType_WithComplexTypes_ReportsDiagnostic()
    {
        var test =
            @"
using System;

class Test
{
    void Method()
    {
        var now = DateTime.Now;
        var guid = Guid.NewGuid();
        var result = {|#0:new
        {
            Id = 1,
            CreatedAt = now,
            UniqueId = guid,
            Description = ""Test"",
            IsActive = true,
            Score = 95.5,
            Count = 100L
        }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithNullableValueTypes_ReportsDiagnostic()
    {
        var test =
            @"
class Entity
{
    public int Id { get; set; }
    public int? NullableInt { get; set; }
    public double? NullableDouble { get; set; }
    public bool? NullableBool { get; set; }
}

class Test
{
    void Method()
    {
        var entity = new Entity { Id = 1 };
        var result = {|#0:new
        {
            entity.Id,
            entity.NullableInt,
            entity.NullableDouble,
            entity.NullableBool
        }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InLinqQuery_ReportsDiagnostic()
    {
        var test =
            @"
using System.Collections.Generic;
using System.Linq;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
}

class Test
{
    void Method()
    {
        var entities = new List<Entity>();
        var result = entities
            .Where(e => e.IsActive)
            .Select(e => {|#0:new { e.Id, e.Name }|})
            .ToList();
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithByteArray_ReportsDiagnostic()
    {
        var test =
            @"
class Entity
{
    public int Id { get; set; }
    public byte[] Data { get; set; }
}

class Test
{
    void Method()
    {
        var entity = new Entity { Id = 1, Data = new byte[] { 1, 2, 3 } };
        var result = {|#0:new { entity.Id, entity.Data }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithExpressionCalculations_ReportsDiagnostic()
    {
        var test =
            @"
class Entity
{
    public int Price { get; set; }
    public int Quantity { get; set; }
    public double DiscountRate { get; set; }
}

class Test
{
    void Method()
    {
        var entity = new Entity { Price = 100, Quantity = 5, DiscountRate = 0.1 };
        var result = {|#0:new
        {
            entity.Price,
            entity.Quantity,
            Total = entity.Price * entity.Quantity,
            Discount = entity.Price * entity.Quantity * entity.DiscountRate,
            FinalPrice = entity.Price * entity.Quantity * (1 - entity.DiscountRate)
        }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_WithStringInterpolation_ReportsDiagnostic()
    {
        var test =
            @"
class Person
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
}

class Test
{
    void Method()
    {
        var person = new Person { FirstName = ""John"", LastName = ""Doe"", Age = 30 };
        var result = {|#0:new
        {
            person.FirstName,
            person.LastName,
            FullName = $""{person.FirstName} {person.LastName}"",
            Description = $""{person.FirstName} is {person.Age} years old""
        }|};
    }
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
