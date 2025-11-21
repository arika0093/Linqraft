using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.ApiControllerProducesResponseTypeAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class ApiControllerProducesResponseTypeAnalyzerTests
{
    private static async Task RunTestAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<
            ApiControllerProducesResponseTypeAnalyzer,
            DefaultVerifier
        >
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90.AddPackages([
                new PackageIdentity("Microsoft.AspNetCore.Mvc.Core", "2.2.5"),
            ]),
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task ApiController_WithSelectExprAndNoProducesResponseType_ReportsDiagnostic()
    {
        var test =
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

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunTestAsync(test, expected);
    }

    [Fact]
    public async Task ApiController_WithProducesResponseType_NoDiagnostic()
    {
        var test =
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
    public IActionResult SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id });
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        await RunTestAsync(test);
    }

    [Fact]
    public async Task NonApiController_WithSelectExpr_NoDiagnostic()
    {
        var test =
            @"
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

public class SampleService
{
    public object SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id });
        return result;
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        await RunTestAsync(test);
    }

    [Fact]
    public async Task ApiController_WithTypedActionResult_NoDiagnostic()
    {
        var test =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

public class Sample
{
    public int Id { get; set; }
}

public class SampleDto
{
    public int Id { get; set; }
}

[ApiController]
public class SampleController : ControllerBase
{
    [HttpGet]
    public ActionResult<List<SampleDto>> SampleGet()
    {
        var query = new List<Sample>().AsQueryable();
        var result = query.SelectExpr<Sample, SampleDto>(x => new { x.Id });
        return result.ToList();
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        await RunTestAsync(test);
    }

    [Fact]
    public async Task ApiController_WithoutSelectExpr_NoDiagnostic()
    {
        var test =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

[ApiController]
public class SampleController : ControllerBase
{
    [HttpGet]
    public IActionResult SampleGet()
    {
        var result = new List<Sample>();
        return Ok(result);
    }
}";

        await RunTestAsync(test);
    }

    [Fact]
    public async Task ApiController_WithSelectExprWithoutTypeArgs_NoDiagnostic()
    {
        var test =
            @"
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

class Sample
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
        var result = query.SelectExpr(x => new { x.Id });
        return Ok(result);
    }
}

" + TestSourceCodes.SelectExprWithExpressionNotImplemented;

        await RunTestAsync(test);
    }

    [Fact]
    public async Task ApiController_ExpressionBodied_ReportsDiagnostic()
    {
        var test =
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

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunTestAsync(test, expected);
    }

    [Fact]
    public async Task ApiController_MultipleSelectExprs_ReportsDiagnostic()
    {
        var test =
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

class OtherDto
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
        // First SelectExpr should trigger the diagnostic
        var result1 = {|#0:query.SelectExpr<Sample, SampleDto>(x => new { x.Id })|};
        var result2 = query.SelectExpr<Sample, OtherDto>(x => new { x.Id });
        return Ok(result1);
    }
}

" + TestSourceCodes.SelectExprWithExpressionObject;

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunTestAsync(test, expected);
    }

    [Fact]
    public async Task ApiController_WithSelectExpr_FirstOrDefault_ReportsDiagnostic()
    {
        var test =
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

        var expected = new DiagnosticResult(
            ApiControllerProducesResponseTypeAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunTestAsync(test, expected);
    }
}
