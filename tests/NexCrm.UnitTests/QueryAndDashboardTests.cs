using System.Text;
using ClosedXML.Excel;
using NexCrm.Application.Common;
using NexCrm.Application.Common.Querying;
using NexCrm.Application.Features.Companies;
using NexCrm.Application.Features.Dashboard;
using NexCrm.Application.Features.Deals;
using NexCrm.Domain.Entities;
using NexCrm.Infrastructure.Services;

namespace NexCrm.UnitTests;

public sealed class QueryEngineTests
{
    private static async Task<PagedResult<DealDto>> Page(TestDb db, TableQuery query) =>
        await new DealService(db.Context, FakeUser.Rep, db.Clock).Query().ToPagedAsync(query, "createdAt", true, CancellationToken.None);

    private static TestDb Seeded()
    {
        var db = new TestDb();
        var acme = db.AddCompany("Acme Ltd");
        var zen = db.AddCompany("Zen 100% Co");
        db.AddDeal(acme, 1_000, DealStage.Won);
        db.AddDeal(acme, 9_000, DealStage.Won);
        db.AddDeal(acme, 4_000, DealStage.Proposal);
        db.AddDeal(zen, 20_000, DealStage.Negotiation);
        return db;
    }

    [Theory, Trait("AC", "GQ-1")]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 10_000, 1, 500)]
    [InlineData(99, 2, 2, 2)]
    public async Task Paging_IsClamped(int page, int size, int expectedPage, int expectedSize)
    {
        using var db = Seeded();
        var result = await Page(db, new TableQuery { Page = page, PageSize = size });
        Assert.Equal((expectedPage, expectedSize), (result.Page, result.PageSize));
    }

    [Fact, Trait("AC", "GQ-2")]
    public async Task UnknownSortOrFilterFields_AreIgnored()
    {
        using var db = Seeded();
        var result = await Page(db, new TableQuery
        {
            SortBy = "passwordHash; DROP TABLE Deals",
            ColumnFilters = new() { ["ownerPassword"] = "x" },
            Filters = [new FilterRule { Field = "nope", Operator = "equals", Value = "1" }],
        });
        Assert.Equal(4, result.Total);
    }

    [Theory, Trait("AC", "GQ-3")]
    [InlineData("amount", ">4000", 2)]
    [InlineData("amount", ">=4000", 3)]
    [InlineData("amount", "<4000", 1)]
    [InlineData("amount", "1000..9000", 3)]
    [InlineData("stage", "Won", 2)]
    [InlineData("stage", "Won,Negotiation", 3)]
    [InlineData("company", "zen", 1)]
    [InlineData("createdAt", "2026-11-05", 4)]
    [InlineData("createdAt", "2026-11-06..", 0)]
    public async Task ColumnFilters_FollowTheirSyntax(string field, string value, int expected)
    {
        using var db = Seeded();
        var result = await Page(db, new TableQuery { ColumnFilters = new() { [field] = value } });
        Assert.Equal(expected, result.Total);
    }

    [Fact, Trait("AC", "GQ-4")]
    public async Task AdvancedRules_CombineWithAndOr_AndSumsCoverAllFilteredRows()
    {
        using var db = Seeded();
        FilterRule[] rules = [new() { Field = "stage", Operator = "equals", Value = "Won" }, new() { Field = "amount", Operator = "gt", Value = "5000" }];

        var and = await Page(db, new TableQuery { Filters = [.. rules], Logic = "and", PageSize = 1, SumFields = ["amount"] });
        var or = await Page(db, new TableQuery { Filters = [.. rules], Logic = "or", PageSize = 1, SumFields = ["amount"] });

        Assert.Equal((1, 9_000m), (and.Total, and.Sums!["amount"]));
        Assert.Equal((3, 30_000m), (or.Total, or.Sums!["amount"]));
        Assert.Single(or.Items);
    }

    [Theory, Trait("AC", "GQ-5")]
    [InlineData("acme", 3)]
    [InlineData("ACME", 3)]
    [InlineData("100%", 1)]
    [InlineData("%", 1)]
    [InlineData("_", 0)]
    public async Task GlobalSearch_IsCaseInsensitive_AndTreatsWildcardsLiterally(string term, int expected)
    {
        using var db = Seeded();
        Assert.Equal(expected, (await Page(db, new TableQuery { Search = term })).Total);
    }

    [Fact, Trait("AC", "GQ-2")]
    public async Task Sort_ByMoneyColumn_RunsInSql()
    {
        using var db = Seeded();
        var result = await Page(db, new TableQuery { SortBy = "amount", SortDesc = false });
        Assert.Equal([1_000m, 4_000m, 9_000m, 20_000m], result.Items.Select(d => d.Amount));
    }
}

public sealed class ExportServiceTests
{
    private static readonly CompanyDto[] Rows =
    [
        new() { Id = 1, Name = "=HYPERLINK(\"http://evil\")", City = "Karachi, PK", AnnualRevenue = 1234.5m, CreatedAt = TestDb.Now },
        new() { Id = 2, Name = "Plain \"Quoted\" Co", City = "Lahore", AnnualRevenue = 0 },
    ];

    private static ExportRequest Request(string format, params string[] fields) =>
        new() { Format = format, FileName = "../../etc/passwd", Columns = fields.Select(f => new ExportColumn { Field = f, Header = f.ToUpperInvariant() }).ToList() };

    [Fact, Trait("AC", "GQ-6")]
    public void Csv_EscapesFormulasQuotesAndCommas_InVisibleColumnOrder()
    {
        var file = new ExportService().Export(Rows, Request("csv", "name", "city"), "companies");
        var text = Encoding.UTF8.GetString(file.Content).TrimStart('﻿');
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("NAME,CITY", lines[0]);
        Assert.Equal("\"'=HYPERLINK(\"\"http://evil\"\")\",\"Karachi, PK\"", lines[1]);
        Assert.Equal("\"Plain \"\"Quoted\"\" Co\",Lahore", lines[2]);
        Assert.Equal("text/csv", file.ContentType);
        Assert.DoesNotContain("/", file.FileName);
        Assert.StartsWith("etcpasswd-", file.FileName);
    }

    [Fact, Trait("AC", "GQ-6")]
    public void Xlsx_WritesTypedCells()
    {
        var file = new ExportService().Export(Rows, Request("xlsx", "name", "annualRevenue"), "companies");

        using var book = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = book.Worksheets.First();
        Assert.Equal("NAME", sheet.Cell(1, 1).GetString());
        Assert.Equal(1234.5, sheet.Cell(2, 2).GetDouble());
        Assert.EndsWith(".xlsx", file.FileName);
    }

    [Fact, Trait("AC", "GQ-6")]
    public void Export_WithNoKnownColumns_IsRejected() =>
        Assert.Throws<BadRequestException>(() => new ExportService().Export(Rows, Request("csv", "passwordHash"), "companies"));
}

public sealed class DashboardServiceTests
{
    [Fact, Trait("AC", "DB-2")]
    public async Task EmptyDatabase_GivesZeros_WithoutDividingByZero()
    {
        using var db = new TestDb();
        var dash = await new DashboardService(db.Context, db.Clock).GetAsync(CancellationToken.None);

        Assert.Equal(0, dash.Kpis.WinRate);
        Assert.Equal(0, dash.Kpis.AvgDealSize);
        Assert.Equal(0, dash.Kpis.OpenPipeline);
        Assert.Empty(dash.Pipeline);
        Assert.Empty(dash.DueTasks);
    }

    [Fact, Trait("AC", "DB-1")]
    public async Task Kpis_MatchAKnownDataSet()
    {
        using var db = new TestDb();
        var c = db.AddCompany("Acme");
        db.AddDeal(c, 10_000, DealStage.Proposal);                                 // open, 50%
        db.AddDeal(c, 20_000, DealStage.Negotiation);                              // open, 75%
        db.AddDeal(c, 6_000, DealStage.Won, closedAt: new DateTime(2026, 11, 2));   // this month
        db.AddDeal(c, 4_000, DealStage.Won, closedAt: new DateTime(2026, 10, 15));  // last month
        db.AddDeal(c, 9_000, DealStage.Lost, closedAt: new DateTime(2026, 10, 20));
        db.AddDeal(c, 99_000, DealStage.Won, closedAt: new DateTime(2026, 6, 1));   // outside the 90 days
        db.AddLead(createdAt: new DateTime(2026, 11, 1));
        db.AddLead(email: "b@x.test", createdAt: new DateTime(2026, 10, 3));
        db.AddTask(TestDb.Now.AddHours(-2));          // overdue, and due today
        db.AddTask(TestDb.Now.Date.AddHours(17));      // due later today
        db.AddTask(TestDb.Now.AddHours(-5), done: true);

        var k = (await new DashboardService(db.Context, db.Clock).GetAsync(CancellationToken.None)).Kpis;

        Assert.Equal(30_000m, k.OpenPipeline);
        Assert.Equal(20_000m, k.WeightedForecast);
        Assert.Equal((6_000m, 4_000m, 1), (k.WonThisMonth, k.WonLastMonth, k.DealsWonThisMonth));
        Assert.Equal(66.7m, k.WinRate);
        Assert.Equal(5_000m, k.AvgDealSize);
        Assert.Equal((1, 1), (k.NewLeadsThisMonth, k.NewLeadsLastMonth));
        Assert.Equal((1, 2), (k.OverdueTasks, k.TasksDueToday));
    }

    [Fact, Trait("AC", "DB-3")]
    public async Task Trend_HasTwelveMonths_OldestFirst_WithZerosForGaps()
    {
        using var db = new TestDb();
        db.AddDeal(db.AddCompany("Acme"), 5_000, DealStage.Won, closedAt: new DateTime(2026, 3, 10));

        var trend = (await new DashboardService(db.Context, db.Clock).GetAsync(CancellationToken.None)).Trend;

        Assert.Equal(12, trend.Count);
        Assert.Equal(("Dec 25", "Nov 26"), (trend[0].Month, trend[^1].Month));
        Assert.Equal(5_000m, trend.Single(p => p.Month == "Mar 26").Won);
        Assert.Equal(11, trend.Count(p => p.Won == 0));
    }

    [Fact, Trait("AC", "DB-4")]
    public async Task DueTasks_CoverTwoWeeksBackToThreeDaysAhead_SoonestFirst_MaxEight()
    {
        using var db = new TestDb();
        db.AddTask(TestDb.Now.AddDays(-15));
        for (var i = 0; i < 10; i++) db.AddTask(TestDb.Now.Date.AddDays(-13 + i));
        db.AddTask(TestDb.Now.Date.AddDays(4));

        var due = (await new DashboardService(db.Context, db.Clock).GetAsync(CancellationToken.None)).DueTasks;

        Assert.Equal(8, due.Count);
        Assert.Equal(TestDb.Now.Date.AddDays(-13), due[0].DueAt);
        Assert.Equal(due.OrderBy(t => t.DueAt), due);
    }
}
