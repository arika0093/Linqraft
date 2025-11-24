namespace Linqraft.MinimumSample;

public class Order
{
    public int Id { get; set; }
    public Customer? Customer { get; set; }
    public List<OrderItem> OrderItems { get; set; } = [];
}

public class Customer
{
    public string Name { get; set; } = "";
    public Address? Address { get; set; }
}

public class Address
{
    public Country? Country { get; set; }
    public City? City { get; set; }
}

public class Country
{
    public string Name { get; set; } = "";
}

public class City
{
    public string Name { get; set; } = "";
}

public class OrderItem
{
    public Product? Product { get; set; }
    public int Quantity { get; set; }
}

public class Product
{
    public string Name { get; set; } = "";
}
