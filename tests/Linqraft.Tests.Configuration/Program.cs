using System.Linq;

var data = new[]
{
    new ConfigOrder
    {
        Id = 1,
        Items = [new ConfigOrderItem { Name = "One" }],
    },
}.AsQueryable();

var result = data
    .SelectExpr<ConfigOrder, ConfigOrderDto>(order => new
    {
        order.Id,
        Items = order.Items.Select(item => new { item.Name }),
    })
    .ToList();

Console.WriteLine(result.Count);

public sealed class ConfigOrder
{
    public int Id { get; set; }

    public List<ConfigOrderItem> Items { get; set; } = [];
}

public sealed class ConfigOrderItem
{
    public string Name { get; set; } = string.Empty;
}
