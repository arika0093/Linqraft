
using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public partial class Issue33_SealedPatternTest
{
    [Fact]
    public void SealedPattern_DisposableDto_ShouldWork()
    {
        var order = new List<Order>
        {
            new() { Id = 1 },
        };
        var converted = order
            .AsQueryable()
            .SelectExpr<Order, OrderDtoDisposable>(e => new
            {
                e.Id,
            })
            .ToList();
    }
    
    internal class Order
    {
        public int Id { get; set; }
    }

    internal sealed partial class OrderDtoDisposable : IDisposable
    {
        public void Dispose() {}
    }
}
