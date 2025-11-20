using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Linqraft.ApiSample;

[Route("api/controller/")]
[ApiController]
public partial class OrderController : ControllerBase
{
    [HttpGet]
    [Route("get-orders/explicit")]
    public ActionResult<List<OrderDto>> GetOrdersAsync()
    {
        return SampleData
            .GetOrdersFromOtherSource()
            .AsQueryable()
            .SelectExpr<Order, OrderDto>(s => new
            {
                Id = s.Id,
                CustomerName = s.Customer?.Name,
                CustomerCountry = s.Customer?.Address?.Country?.Name,
                CustomerCity = s.Customer?.Address?.City?.Name,
                Items = s.OrderItems.Select(oi => new
                {
                    ProductName = oi.Product?.Name,
                    Quantity = oi.Quantity,
                }),
            })
            .ToList();
    }

    [HttpGet]
    [Route("get-orders/anonymous")]
    public IActionResult GetOrdersAnonymousAsync()
    {
        var results = SampleData
            .GetOrdersFromOtherSource()
            .AsQueryable()
            .SelectExpr<Order, ResultsDto_ZW8RN8AA>(s => new
            {
                Id = s.Id,
                CustomerName = s.Customer?.Name,
                CustomerCountry = s.Customer?.Address?.Country?.Name,
                CustomerCity = s.Customer?.Address?.City?.Name,
                Items = s.OrderItems.Select(oi => new
                {
                    ProductName = oi.Product?.Name,
                    Quantity = oi.Quantity,
                }),
            })
            .ToList();
        return Ok(results);
    }
}
