using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class ByteArrayCaseTest
{
    [Fact]
    public void ByteArray_WithNullableAccess_ShouldBeGenerated()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr<ByteArrayEntity, ByteArrayDto>(u => new
            {
                u.UserId,
                u.LoginPassword?.HashedPassword,
                u.LoginPassword?.Salt,
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.UserId.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        first.HashedPassword.ShouldBe(new byte[] { 1, 2, 3 });
        first.Salt.ShouldBe(new byte[] { 4, 5, 6 });

        var second = converted[1];
        second.UserId.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        second.HashedPassword.ShouldBeNull();
        second.Salt.ShouldBeNull();
    }

    [Fact]
    public void ByteArray_WithoutNullableAccess_ShouldBeGenerated()
    {
        var dataWithNonNull = new List<ByteArrayEntity>
        {
            new()
            {
                UserId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                LoginPassword = new() { HashedPassword = new byte[] { 1, 2, 3 }, Salt = new byte[] { 4, 5, 6 } }
            }
        };

        var converted = dataWithNonNull
            .AsQueryable()
            .SelectExpr<ByteArrayEntity, ByteArrayDto2>(u => new
            {
                u.UserId,
                u.LoginPassword.HashedPassword,
                u.LoginPassword.Salt,
            })
            .ToList();

        converted.Count.ShouldBe(1);
        var first = converted[0];
        first.UserId.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        first.HashedPassword.ShouldBe(new byte[] { 1, 2, 3 });
        first.Salt.ShouldBe(new byte[] { 4, 5, 6 });
    }

    private readonly List<ByteArrayEntity> TestData =
    [
        new()
        {
            UserId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            LoginPassword = new() { HashedPassword = new byte[] { 1, 2, 3 }, Salt = new byte[] { 4, 5, 6 } }
        },
        new()
        {
            UserId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            LoginPassword = null
        },
    ];
}

internal class ByteArrayEntity
{
    public Guid UserId { get; set; }
    public LoginPasswordEntity? LoginPassword { get; set; }
}

internal class LoginPasswordEntity
{
    public byte[] HashedPassword { get; set; } = [];
    public byte[] Salt { get; set; } = [];
}
