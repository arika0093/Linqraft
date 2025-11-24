using System.Text.Json;
using Linqraft.MinimumSample;

// exec
var results = SampleData
    .GetOrdersFromOtherSource()
    .AsQueryable()
    .SelectExpr(s => new
    {
        Id = s.Id,
        CustomerName = s.Customer?.Name,
        CustomerCountry = s.Customer?.Address?.Country?.Name,
        CustomerCity = s.Customer?.Address?.City?.Name,
        Items = s
            .OrderItems.Select(oi => new { ProductName = oi.Product?.Name, Quantity = oi.Quantity })
            .ToList(),
    })
    .ToList();

var resultJson = JsonSerializer.Serialize(
    results,
    new JsonSerializerOptions { WriteIndented = true }
);
Console.WriteLine(resultJson);
