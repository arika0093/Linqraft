namespace Linqraft.ApiSample;

public static class SampleData
{
    public static List<Order> GetOrdersFromOtherSource()
    {
        return
        [
            new Order
            {
                Id = 1,
                Customer = new Customer
                {
                    Name = "Alice",
                    Address = new Address
                    {
                        Country = new Country { Name = "USA" },
                        City = new City { Name = "New York" },
                    },
                },
                OrderItems =
                [
                    new OrderItem
                    {
                        Product = new Product { Name = "Laptop" },
                        Quantity = 1,
                    },
                    new OrderItem
                    {
                        Product = new Product { Name = "Mouse" },
                        Quantity = 2,
                    },
                ],
            },
            new Order
            {
                Id = 2,
                Customer = new Customer
                {
                    Name = "Bob",
                    Address = new Address
                    {
                        Country = new Country { Name = "Canada" },
                        City = new City { Name = "Toronto" },
                    },
                },
                OrderItems =
                [
                    new OrderItem
                    {
                        Product = new Product { Name = "Smartphone" },
                        Quantity = 1,
                    },
                ],
            },
        ];
    }
}
