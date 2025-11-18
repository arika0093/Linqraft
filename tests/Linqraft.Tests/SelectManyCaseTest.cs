using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class SelectManyCaseTest
{
    private readonly List<Author> AuthorData =
    [
        new Author
        {
            Id = 1,
            Name = "Author1",
            Books =
            [
                new Book
                {
                    Title = "Book1-1",
                    Chapters =
                    [
                        new Chapter { Title = "Chapter1-1-1", PageCount = 10 },
                        new Chapter { Title = "Chapter1-1-2", PageCount = 15 },
                    ],
                },
                new Book
                {
                    Title = "Book1-2",
                    Chapters =
                    [
                        new Chapter { Title = "Chapter1-2-1", PageCount = 20 },
                    ],
                },
            ],
        },
        new Author
        {
            Id = 2,
            Name = "Author2",
            Books =
            [
                new Book
                {
                    Title = "Book2-1",
                    Chapters =
                    [
                        new Chapter { Title = "Chapter2-1-1", PageCount = 25 },
                        new Chapter { Title = "Chapter2-1-2", PageCount = 30 },
                        new Chapter { Title = "Chapter2-1-3", PageCount = 35 },
                    ],
                },
            ],
        },
    ];

    [Fact]
    public void SelectMany_Basic_Anonymous()
    {
        // Basic SelectMany usage: flatten all chapters from all books
        var converted = AuthorData
            .AsQueryable()
            .SelectExpr(a => new
            {
                a.Name,
                AllChapterTitles = a.Books.SelectMany(b => b.Chapters).Select(c => c.Title).ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        var first = converted[0];
        first.Name.ShouldBe("Author1");
        first.AllChapterTitles.Count.ShouldBe(3);
        first.AllChapterTitles.ShouldBe(["Chapter1-1-1", "Chapter1-1-2", "Chapter1-2-1"]);

        var second = converted[1];
        second.Name.ShouldBe("Author2");
        second.AllChapterTitles.Count.ShouldBe(3);
        second.AllChapterTitles.ShouldBe(["Chapter2-1-1", "Chapter2-1-2", "Chapter2-1-3"]);
    }

    [Fact]
    public void SelectMany_WithProjection_Anonymous()
    {
        // SelectMany with projection to anonymous type
        var converted = AuthorData
            .AsQueryable()
            .SelectExpr(a => new
            {
                a.Name,
                AllChapters = a
                    .Books.SelectMany(b => b.Chapters)
                    .Select(c => new { c.Title, c.PageCount })
                    .ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        var first = converted[0];
        first.Name.ShouldBe("Author1");
        first.AllChapters.Count.ShouldBe(3);
        first.AllChapters[0].Title.ShouldBe("Chapter1-1-1");
        first.AllChapters[0].PageCount.ShouldBe(10);
        first.AllChapters[1].Title.ShouldBe("Chapter1-1-2");
        first.AllChapters[1].PageCount.ShouldBe(15);
    }

    [Fact]
    public void SelectMany_WithProjection_ExplicitDto()
    {
        // SelectMany with explicit DTO
        var converted = AuthorData
            .AsQueryable()
            .SelectExpr<Author, AuthorWithChaptersDto>(a => new
            {
                a.Name,
                AllChapters = a
                    .Books.SelectMany(b => b.Chapters)
                    .Select(c => new { c.Title, c.PageCount })
                    .ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        var first = converted[0];
        first.Name.ShouldBe("Author1");
        first.AllChapters.Count.ShouldBe(3);
        first.AllChapters[0].Title.ShouldBe("Chapter1-1-1");
        first.AllChapters[0].PageCount.ShouldBe(10);
    }

    [Fact]
    public void SelectMany_DirectProjection()
    {
        // SelectMany without chained Select
        var converted = AuthorData
            .AsQueryable()
            .SelectExpr(a => new
            {
                a.Name,
                AllChapters = a.Books.SelectMany(b => b.Chapters).ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        var first = converted[0];
        first.Name.ShouldBe("Author1");
        first.AllChapters.Count.ShouldBe(3);
        first.AllChapters[0].Title.ShouldBe("Chapter1-1-1");
        first.AllChapters[0].PageCount.ShouldBe(10);
    }

    [Fact]
    public void SelectMany_WithNullableAccess()
    {
        // SelectMany with null-conditional operator
        var testData = new List<Author>
        {
            new Author
            {
                Id = 1,
                Name = "Author1",
                Books =
                [
                    new Book
                    {
                        Title = "Book1",
                        Chapters = [new Chapter { Title = "Chapter1", PageCount = 10 }],
                    },
                ],
            },
            new Author { Id = 2, Name = "Author2", Books = null, },
        };

        var converted = testData
            .AsQueryable()
            .SelectExpr(a => new
            {
                a.Name,
                AllChapterTitles = a
                    .Books?.SelectMany(b => b.Chapters)
                    .Select(c => c.Title)
                    .ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        var first = converted[0];
        first.Name.ShouldBe("Author1");
        first.AllChapterTitles.ShouldNotBeNull();
        first.AllChapterTitles!.Count.ShouldBe(1);
        first.AllChapterTitles[0].ShouldBe("Chapter1");

        var second = converted[1];
        second.Name.ShouldBe("Author2");
        second.AllChapterTitles.ShouldBeNull();
    }



    [Fact]
    public void SelectMany_NestedWithinSelect()
    {
        // SelectMany nested within a Select
        var data = new List<Publisher>
        {
            new Publisher
            {
                Name = "Publisher1",
                Authors =
                [
                    new Author
                    {
                        Id = 1,
                        Name = "Author1",
                        Books =
                        [
                            new Book
                            {
                                Title = "Book1",
                                Chapters =
                                [
                                    new Chapter { Title = "Chapter1", PageCount = 10 },
                                ],
                            },
                        ],
                    },
                ],
            },
        };

        var converted = data
            .AsQueryable()
            .SelectExpr(p => new
            {
                p.Name,
                AllAuthors = p
                    .Authors.Select(a => new
                    {
                        a.Name,
                        AllChapters = a.Books.SelectMany(b => b.Chapters).Select(c => c.Title).ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(1);
        var first = converted[0];
        first.Name.ShouldBe("Publisher1");
        first.AllAuthors.Count.ShouldBe(1);
        first.AllAuthors[0].Name.ShouldBe("Author1");
        first.AllAuthors[0].AllChapters.Count.ShouldBe(1);
        first.AllAuthors[0].AllChapters[0].ShouldBe("Chapter1");
    }
}

internal class Publisher
{
    public required string Name { get; set; }
    public List<Author> Authors { get; set; } = [];
}

internal class Author
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<Book>? Books { get; set; }
}

internal class Book
{
    public required string Title { get; set; }
    public List<Chapter> Chapters { get; set; } = [];
}

internal class Chapter
{
    public required string Title { get; set; }
    public int PageCount { get; set; }
}

internal class ChapterInfo
{
    public required string Title { get; set; }
    public int PageCount { get; set; }
}
