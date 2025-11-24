using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

public class ApiControllerProducesResponseTypeCodeFixProviderTests
{
    private static async Task RunCodeFixTestAsync(
        string before,
        string after,
        DiagnosticResult expected
    )
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
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }

    [Fact]
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
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = {|#0:query.SelectExpr<Sample, SampleDto>(x => new { x.Id })|};
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

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
    [HttpGet]
    [ProducesResponseType(typeof(IQueryable<SampleDto>), 200)]
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id });
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(before, after, expected);
    }

    [Fact]
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
    public IActionResult SampleGet() =>
        Ok({|#0:new List<Sample>().AsQueryable().SelectExpr<Sample, SampleDto>(x => new { x.Id })|}.ToList());
}

" + TestSourceCodes.SelectExprWithExpressionObject;

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
    [HttpGet]
    [ProducesResponseType(typeof(List<SampleDto>), 200)]
    public IActionResult SampleGet() =>
        Ok(new List<Sample>().AsQueryable().SelectExpr<Sample, SampleDto>(x => new { x.Id }).ToList());
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(before, after, expected);
    }

    [Fact]
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
    public IActionResult GetOrderDetails()
    {
        var query = new List<Order>().AsQueryable();
        var result = {|#0:query.SelectExpr<Order, OrderDetailDto>(x => new { x.Id })|};
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

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
    [HttpGet]
    [ProducesResponseType(typeof(IQueryable<OrderDetailDto>), 200)]
    public IActionResult GetOrderDetails()
    {
        var query = new List<Order>().AsQueryable();
        var result = query.SelectExpr<Order, OrderDetailDto>(x => new { x.Id });
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(before, after, expected);
    }

    [Fact]
    public async Task CodeFix_AddsProducesResponseTypeAttribute_SingleResult_FirstOrDefault()
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
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = {|#0:query.SelectExpr<Sample, SampleDto>(x => new { x.Id })|}.FirstOrDefault();
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

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
    [HttpGet]
    [ProducesResponseType(typeof(SampleDto), 200)]
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id }).FirstOrDefault();
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(before, after, expected);
    }

    [Fact]
    public async Task CodeFix_AddsProducesResponseTypeAttribute_Collection_ToArray()
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
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = {|#0:query.SelectExpr<Sample, SampleDto>(x => new { x.Id })|}.ToArray();
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

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
    [HttpGet]
    [ProducesResponseType(typeof(SampleDto[]), 200)]
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id }).ToArray();
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(before, after, expected);
    }
}
