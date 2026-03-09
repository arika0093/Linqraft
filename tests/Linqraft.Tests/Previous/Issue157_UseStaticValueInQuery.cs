using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class Issue157_UseStaticValueInQuery
{
    /// <summary>
    /// Test for issue #157: Using enum/static values inside SelectExpr query
    /// </summary>
    [Fact]
    public void CanUseEnumValueInSelectQuery()
    {
        PersonWithChildren[] people = [];
        var result = people
            .AsQueryable()
            .SelectExpr<PersonWithChildren, Issue157TestDto>(s => new
            {
                FilteredData = s
                    .Children.Where(c => c.EnumValue == SampleValues.A)
                    .Where(c => c.SomeValue > ReferenceClass.ConstValue)
                    .Select(c => new { c.EnumValue, c.AnotherValue })
                    .FirstOrDefault(c => c.AnotherValue != ReferenceClass.StaticReadonlyValue),
            })
            .ToList();
    }

    public class PersonWithChildren
    {
        public required int Id { get; set; }
        public required string? Name { get; set; }
        public required List<Child> Children { get; set; }
    }

    public class Child
    {
        public required SampleValues EnumValue { get; set; }
        public required int SomeValue { get; set; }
        public required string? AnotherValue { get; set; }
    }

    public enum SampleValues
    {
        A = 1,
        B = 2,
    }

    public class ReferenceClass
    {
        public const int ConstValue = 10;
        public static readonly string StaticReadonlyValue = "Test";
    }
}
