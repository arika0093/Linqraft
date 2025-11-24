namespace Linqraft.MinimumSample;

public static class SampleData
{
    public static List<Order> GetOrdersFromOtherSource()
    {
        return new List<Order>
        {
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
                OrderItems = new List<OrderItem>
                {
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
                },
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
                OrderItems = new List<OrderItem>
                {
                    new OrderItem
                    {
                        Product = new Product { Name = "Smartphone" },
                        Quantity = 1,
                    },
                },
            },
        };
    }
}
