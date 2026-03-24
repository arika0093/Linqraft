#:package Linqraft@__LINQRAFT_PACKAGE_VERSION__
#:property PublishAot=false
// AOT is disabled here because this check only validates the packaged source generator output
// for the top-level runfile scenario behind #324/#329.
// The repro intentionally mirrors the issue report's partially initialized POCO setup, so
// remaining CS8618 noise is suppressed instead of normalizing every property initializer away.
#pragma warning disable CS8618
using System;
using System.Linq;

var dbContext = new DbContextMock();
var key = "test";
var conditionInfo = dbContext
    .TestRepos.Where(l => l.Key == key)
    .SelectExpr<TestRepo, AutoSettingConditionInfoDto>(l => new
    {
        l.Id,
        l.Key,
        Name = l.Data.TestRepoInfo.CommonName,
        Available = l.Data.SomeInfo != null,
    })
    .FirstOrDefault();
Console.WriteLine(conditionInfo);

public class DbContextMock
{
    public IQueryable<TestRepo> TestRepos =>
        new[]
        {
            new TestRepo
            {
                Id = 1,
                Key = "test",
                Data = new TestRepoData
                {
                    TestRepoInfo = new TestRepoInfo { CommonName = "common name" },
                    SomeInfo = new TestRepoSomeInfo { Id = 1 },
                },
            },
        }.AsQueryable();
}

public class TestRepo
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public TestRepoData Data { get; set; } = new();
}

public class TestRepoData
{
    public TestRepoInfo TestRepoInfo { get; set; } = new();
    public TestRepoSomeInfo? SomeInfo { get; set; }
}

public class TestRepoInfo
{
    public string CommonName { get; set; } = string.Empty;
}

public class TestRepoSomeInfo
{
    public int Id { get; set; }
}
