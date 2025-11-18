using Linqraft;
using Linqraft.ApiSample;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers();

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "v1");
    options.RoutePrefix = "";
});

// sample usage of SelectExpr in an Minimal API endpoint
app.MapGet(
    "/api/minimal/get-orders",
    () =>
    {
        return SampleData
            .GetOrdersFromOtherSource()
            .AsQueryable()
            .SelectExpr<Order, OrderDtoMinimal>(s => new
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
);

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
