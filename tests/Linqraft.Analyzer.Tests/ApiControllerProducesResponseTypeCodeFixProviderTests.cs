using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class ApiControllerProducesResponseTypeCodeFixProviderTests
{
    private static async Task RunCodeFixTestAsync(string before, string after)
    {
        var test = new CSharpCodeFixTest<
            ApiControllerProducesResponseTypeAnalyzer,
            ApiControllerProducesResponseTypeCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90.AddPackages([
                new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"),
            ]),
        };

        await test.RunAsync();
    }

    [Fact(Skip = "Investigating attribute detection issue in test framework")]
    public async Task CodeFix_AddsProducesResponseTypeAttribute()
    {
        var before =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class SampleDto
{
    public int Id { get; set; }
}

[ApiController]
public class SampleController : ControllerBase
{
    [HttpGet]
    public IActionResult {|#0:SampleGet|}()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id });
        return Ok(result);
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
        => throw new System.NotImplementedException();
}";

        var after =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class SampleDto
{
    public int Id { get; set; }
}

[ApiController]
public class SampleController : ControllerBase
{
    [ProducesResponseType(typeof(List<SampleDto>), 200)]
    [HttpGet]
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id });
        return Ok(result);
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
        => throw new System.NotImplementedException();
}";

        await RunCodeFixTestAsync(before, after);
    }

    [Fact(Skip = "Investigating attribute detection issue in test framework")]
    public async Task CodeFix_AddsProducesResponseTypeAttribute_WithExpressionBody()
    {
        var before =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class SampleDto
{
    public int Id { get; set; }
}

[ApiController]
public class SampleController : ControllerBase
{
    [HttpGet]
    public IActionResult {|#0:SampleGet|}() =>
        Ok(new List<Sample>().AsQueryable().SelectExpr<Sample, SampleDto>(x => new { x.Id }).ToList());
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
        => throw new System.NotImplementedException();
}";

        var after =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class SampleDto
{
    public int Id { get; set; }
}

[ApiController]
public class SampleController : ControllerBase
{
    [ProducesResponseType(typeof(List<SampleDto>), 200)]
    [HttpGet]
    public IActionResult SampleGet() =>
        Ok(new List<Sample>().AsQueryable().SelectExpr<Sample, SampleDto>(x => new { x.Id }).ToList());
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
        => throw new System.NotImplementedException();
}";

        await RunCodeFixTestAsync(before, after);
    }

    [Fact(Skip = "Investigating attribute detection issue in test framework")]
    public async Task CodeFix_AddsProducesResponseTypeAttribute_WithComplexDtoName()
    {
        var before =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Order
{
    public int Id { get; set; }
}

class OrderDetailDto
{
    public int Id { get; set; }
}

[ApiController]
public class OrderController : ControllerBase
{
    [HttpGet]
    public IActionResult {|#0:GetOrderDetails|}()
    {
        var query = new List<Order>().AsQueryable();
        var result = query.SelectExpr<Order, OrderDetailDto>(x => new { x.Id });
        return Ok(result);
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
        => throw new System.NotImplementedException();
}";

        var after =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Order
{
    public int Id { get; set; }
}

class OrderDetailDto
{
    public int Id { get; set; }
}

[ApiController]
public class OrderController : ControllerBase
{
    [ProducesResponseType(typeof(List<OrderDetailDto>), 200)]
    [HttpGet]
    public IActionResult GetOrderDetails()
    {
        var query = new List<Order>().AsQueryable();
        var result = query.SelectExpr<Order, OrderDetailDto>(x => new { x.Id });
        return Ok(result);
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
        => throw new System.NotImplementedException();
}";

        await RunCodeFixTestAsync(before, after);
    }
}
