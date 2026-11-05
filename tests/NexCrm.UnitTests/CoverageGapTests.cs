using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexCrm.Application.Common;
using NexCrm.Application.Common.Querying;
using NexCrm.Application.Features.Auth;
using NexCrm.Application.Features.Contacts;
using NexCrm.Application.Features.Deals;
using NexCrm.Application.Features.Leads;
using NexCrm.Application.Features.Lookups;
using NexCrm.Application.Features.Tasks;
using NexCrm.Domain.Entities;
using NexCrm.Infrastructure.Services;

namespace NexCrm.UnitTests;

/// <summary>Behaviours the first pass left untested: create/update paths, lookups and every filter operator.</summary>
public sealed class LeadAndTaskSaveTests
{
    [Fact, Trait("AC", "LD-1")]
    public async Task Lead_CreateThenUpdate_RoundTripsEveryField()
    {
        using var db = new TestDb();
        var service = new LeadService(db.Context, FakeUser.Rep, db.Clock);

        var created = await service.CreateAsync(new("  Ayesha Malik ", "Kiehn LLC", "AYESHA@kiehn.test", " ", LeadSource.Referral, LeadStatus.New, 0, 0, db.Rep.Id), CancellationToken.None);
        var updated = await service.UpdateAsync(created.Id, new("Ayesha Malik", "Kiehn LLC", "ayesha@kiehn.test", "+92 300 1234567", LeadSource.Event, LeadStatus.Qualified, 100, 42_000, db.Manager.Id), CancellationToken.None);

        Assert.Equal(("Ayesha Malik", "ayesha@kiehn.test", (string?)null), (created.Name, created.Email, created.Phone));
        Assert.Equal((LeadStatus.Qualified, 100, 42_000m, db.Manager.Id, "Hira Test"), (updated.Status, updated.Score, updated.EstimatedValue, updated.OwnerId, updated.Owner));
        Assert.Equal(LeadSource.Event, (await service.GetAsync(created.Id, CancellationToken.None)).Source);
    }

    [Fact, Trait("AC", "TK-1")]
    public async Task Task_CreateWithContactAndDeal_ProjectsTheirNames()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        var contact = db.AddContact(company, "c@acme.test", "Omer", "Gill");
        var deal = db.AddDeal(company, 1_000, DealStage.Proposal);

        var saved = await new TaskService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(
            new("  Send the quote ", TaskType.Email, TaskPriority.High, TestDb.Now.AddDays(1), "  ", contact.Id, deal.Id, db.Rep.Id), CancellationToken.None);

        Assert.Equal(("Send the quote", "Omer Gill", "Acme deal", (string?)null), (saved.Subject, saved.Contact, saved.Deal, saved.Notes));
        Assert.False(saved.Overdue);
    }

    [Fact, Trait("AC", "TK-1")]
    public async Task Task_ValidContactButUnknownDeal_IsRejected()
    {
        using var db = new TestDb();
        var contact = db.AddContact(null, "c@acme.test");
        await Assert.ThrowsAsync<BadRequestException>(() => new TaskService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(
            new("Call", TaskType.Call, TaskPriority.Low, TestDb.Now, null, contact.Id, 404, db.Rep.Id), CancellationToken.None));
    }

    [Fact, Trait("AC", "TK-2")]
    public async Task Task_SetDoneOnUnknownId_IsNotFound()
    {
        using var db = new TestDb();
        await Assert.ThrowsAsync<NotFoundException>(() => new TaskService(db.Context, FakeUser.Rep, db.Clock).SetDoneAsync(404, true, CancellationToken.None));
    }

    [Fact, Trait("AC", "CO-4")]
    public async Task Get_UnknownId_IsNotFound()
    {
        using var db = new TestDb();
        await Assert.ThrowsAsync<NotFoundException>(() => new ContactService(db.Context, FakeUser.Rep, db.Clock).GetAsync(404, CancellationToken.None));
    }

    [Fact, Trait("AC", "CT-2")]
    public async Task Contact_WithExistingCompany_IsSaved()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        var saved = await new ContactService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(
            new("Ali", "Khan", "ali@acme.test", null, "CTO", ContactStage.Customer, company.Id, db.Rep.Id), CancellationToken.None);
        Assert.Equal(("Acme", "CTO", 0), (saved.Company, saved.JobTitle, saved.OpenTasks));
    }

    [Fact, Trait("AC", "AUTH-1")]
    public async Task Me_ActiveUser_ReturnsTheProfile()
    {
        using var db = new TestDb();
        var auth = TestDb.Auth(db);
        var me = await auth.MeAsync(db.Manager.Id, CancellationToken.None);
        Assert.Equal(("hira", "Manager", true), (me.Username, me.Role, me.CanDelete));
    }

    [Theory, Trait("AC", "CO-1")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" x ", "x")]
    public void Clean_TrimsAndNullsBlanks(string? input, string? expected) => Assert.Equal(expected, input.Clean());

    [Fact, Trait("AC", "DL-3")]
    public void Deal_Close_BackdatesTheOutcome()
    {
        var deal = new Deal();
        deal.Close(DealStage.Won, TestDb.Now.AddDays(-30));
        Assert.Equal((DealStage.Won, 100, (DateTime?)TestDb.Now.AddDays(-30)), (deal.Stage, deal.Probability, deal.ClosedAt));
    }

    [Fact, Trait("AC", "DL-3")]
    public void Deal_MoveToSameStage_ChangesNothing()
    {
        var deal = new Deal();
        deal.Forecast(35);
        deal.MoveTo(DealStage.Prospecting, TestDb.Now);
        Assert.Equal(35, deal.Probability);
    }
}

public sealed class LookupTests
{
    [Fact, Trait("AC", "LK-1")]
    public async Task Contacts_FilterByTermOrEmail_AndByCompany()
    {
        using var db = new TestDb();
        var acme = db.AddCompany("Acme");
        var zen = db.AddCompany("Zen");
        db.AddContact(acme, "omer@acme.test", "Omer", "Gill");
        db.AddContact(zen, "sara@zen.test", "Sara", "Abbasi");
        var lookups = new LookupService(db.Context);

        Assert.Equal("Omer Gill", (await lookups.ContactsAsync("mer G", null, CancellationToken.None)).Single().Label);
        Assert.Equal("Sara Abbasi", (await lookups.ContactsAsync("zen.test", null, CancellationToken.None)).Single().Label);
        Assert.Equal("Sara Abbasi", (await lookups.ContactsAsync("", zen.Id, CancellationToken.None)).Single().Label);
    }

    [Fact, Trait("AC", "LK-1")]
    public async Task NullTerm_MeansNoFilter()
    {
        using var db = new TestDb();
        db.AddCompany("Acme");
        Assert.Single(await new LookupService(db.Context).CompaniesAsync(null, CancellationToken.None));
    }

    [Fact, Trait("AC", "LK-1")]
    public async Task Deals_FilterByTitle_NewestFirst()
    {
        using var db = new TestDb();
        var acme = db.AddCompany("Acme");
        db.AddDeal(acme, 1, DealStage.Proposal, createdAt: TestDb.Now.AddDays(-2));
        db.AddDeal(db.AddCompany("Zen"), 1, DealStage.Proposal, createdAt: TestDb.Now.AddDays(-1));
        var lookups = new LookupService(db.Context);

        Assert.Equal(["Zen deal", "Acme deal"], (await lookups.DealsAsync("", CancellationToken.None)).Select(o => o.Label));
        Assert.Equal("Acme", (await lookups.DealsAsync("acme", CancellationToken.None)).Single().Hint);
    }
}

public sealed class FilterOperatorTests
{
    private sealed record Row(int Id, string? Name, int Count, decimal? Value, bool Active, DateTime When, DealStage Stage) : IHasId;

    private static readonly Row[] Rows =
    [
        new(1, "Alpha", 1, 10m, true, new DateTime(2026, 10, 1, 9, 0, 0), DealStage.Won),
        new(2, "Beta", 5, null, false, new DateTime(2026, 10, 15, 9, 0, 0), DealStage.Lost),
        new(3, "", 9, 30m, true, new DateTime(2026, 11, 2, 9, 0, 0), DealStage.Proposal),
        new(4, null, 12, 40m, false, new DateTime(2026, 11, 5, 9, 0, 0), DealStage.Won),
    ];

    private static int[] Ids(params FilterRule[] rules) =>
        QueryEngine.ApplyFilters(Rows.AsQueryable(), new TableQuery { Filters = [.. rules] }).Select(r => r.Id).OrderBy(i => i).ToArray();

    private static FilterRule R(string field, string op, string? v = null, string? v2 = null) => new() { Field = field, Operator = op, Value = v, Value2 = v2 };

    [Theory, Trait("AC", "GQ-3")]
    [InlineData("contains", "LP", new[] { 1 })]
    [InlineData("notContains", "a", new[] { 3, 4 })]
    [InlineData("equals", "beta", new[] { 2 })]
    [InlineData("notEquals", "beta", new[] { 1, 3, 4 })]
    [InlineData("startsWith", "al", new[] { 1 })]
    [InlineData("endsWith", "TA", new[] { 2 })]
    public void TextOperators(string op, string value, int[] expected) => Assert.Equal(expected, Ids(R("name", op, value)));

    [Fact, Trait("AC", "GQ-3")]
    public void EmptyOperators_CoverNullAndBlankText_AndNullableNumbers()
    {
        Assert.Equal([3, 4], Ids(R("name", "isEmpty")));
        Assert.Equal([1, 2], Ids(R("name", "isNotEmpty")));
        Assert.Equal([2], Ids(R("value", "isEmpty")));
        Assert.Equal([1, 3, 4], Ids(R("value", "isNotEmpty")));
        Assert.Empty(Ids(R("count", "isEmpty")));
        Assert.Equal([1, 2, 3, 4], Ids(R("count", "isNotEmpty")));
    }

    [Theory, Trait("AC", "GQ-3")]
    [InlineData("equals", "5", null, new[] { 2 })]
    [InlineData("notEquals", "5", null, new[] { 1, 3, 4 })]
    [InlineData("gt", "5", null, new[] { 3, 4 })]
    [InlineData("gte", "5", null, new[] { 2, 3, 4 })]
    [InlineData("lt", "5", null, new[] { 1 })]
    [InlineData("lte", "5", null, new[] { 1, 2 })]
    [InlineData("between", "2", "10", new[] { 2, 3 })]
    [InlineData("gt", "abc", null, new[] { 1, 2, 3, 4 })]
    [InlineData("unknownOp", "1", null, new[] { 1, 2, 3, 4 })]
    public void NumberOperators(string op, string value, string? value2, int[] expected) => Assert.Equal(expected, Ids(R("count", op, value, value2)));

    [Theory, Trait("AC", "GQ-3")]
    [InlineData("on", "2026-10-15", null, new[] { 2 })]
    [InlineData("notEquals", "2026-10-15", null, new[] { 1, 3, 4 })]
    [InlineData("before", "2026-10-15", null, new[] { 1 })]
    [InlineData("after", "2026-10-15", null, new[] { 3, 4 })]
    [InlineData("gte", "2026-10-15", null, new[] { 2, 3, 4 })]
    [InlineData("lte", "2026-10-15", null, new[] { 1, 2 })]
    [InlineData("between", "2026-10-02", "2026-11-02", new[] { 2, 3 })]
    [InlineData("on", "not a date", null, new[] { 1, 2, 3, 4 })]
    public void DateOperators(string op, string value, string? value2, int[] expected) => Assert.Equal(expected, Ids(R("when", op, value, value2)));

    [Fact, Trait("AC", "GQ-3")]
    public void EnumAndBoolOperators()
    {
        Assert.Equal([1, 4], Ids(R("stage", "equals", "won")));
        Assert.Equal([2, 3], Ids(R("stage", "notEquals", "Won")));
        Assert.Equal([1, 2, 4], Ids(R("stage", "equals", "Won,Lost")));
        Assert.Equal([1, 2, 3, 4], Ids(R("stage", "equals", "Bogus")));
        Assert.Equal([1, 3], Ids(R("active", "isTrue")));
        Assert.Equal([2, 4], Ids(R("active", "isFalse")));
        Assert.Equal([2, 4], Ids(R("active", "notEquals", "true")));
        Assert.Equal([1, 2, 3, 4], Ids(R("count", "isTrue")));
    }

    [Theory, Trait("AC", "GQ-3")]
    [InlineData("name", "alpha, beta", new[] { 1, 2 })]
    [InlineData("active", "true", new[] { 1, 3 })]
    [InlineData("active", "maybe", new[] { 1, 2, 3, 4 })]
    [InlineData("count", "9", new[] { 3 })]
    [InlineData("count", "=9", new[] { 3 })]
    [InlineData("count", "<=5", new[] { 1, 2 })]
    [InlineData("count", ">x", new[] { 1, 2, 3, 4 })]
    [InlineData("count", "..5", new[] { 1, 2 })]
    [InlineData("stage", "nope", new[] { 1, 2, 3, 4 })]
    [InlineData("when", "..2026-10-15", new[] { 1, 2 })]
    public void ColumnFilterSyntax(string field, string raw, int[] expected)
    {
        var ids = QueryEngine.ApplyFilters(Rows.AsQueryable(), new TableQuery { ColumnFilters = new() { [field] = raw } }).Select(r => r.Id).OrderBy(i => i);
        Assert.Equal(expected, ids);
    }

    [Fact, Trait("AC", "GQ-1")]
    public async Task InMemoryPaging_WorksWithoutEf_AndSumsNullableNumbers()
    {
        var page = await Rows.AsQueryable().ToPagedAsync(new TableQuery { PageSize = 2, SumFields = ["value", "name", "missing"] }, "count", true, CancellationToken.None);
        Assert.Equal([4, 3], page.Items.Select(r => r.Id));
        Assert.Equal(80m, page.Sums!["value"]);
        Assert.False(page.Sums.ContainsKey("name"));
    }

    [Fact, Trait("AC", "GQ-6")]
    public async Task ExportList_AppliesFiltersAndSort_AcrossAllRows()
    {
        using var db = new TestDb();
        var c = db.AddCompany("Acme");
        db.AddDeal(c, 1_000, DealStage.Won);
        db.AddDeal(c, 3_000, DealStage.Won);
        db.AddDeal(c, 2_000, DealStage.Lost);

        var rows = await new DealService(db.Context, FakeUser.Rep, db.Clock).Query()
            .ToExportListAsync(new TableQuery { PageSize = 1, ColumnFilters = new() { ["stage"] = "Won" } }, "amount", true, CancellationToken.None);

        Assert.Equal([3_000m, 1_000m], rows.Select(r => r.Amount));
        var inMemory = await Rows.AsQueryable().ToExportListAsync(new TableQuery(), "id", true);
        Assert.Equal([4, 3, 2, 1], inMemory.Select(r => r.Id));
    }
}

public sealed class CrudServiceContractTests
{
    /// <summary>The smallest possible feature: only the abstract members, so the base class defaults run.</summary>
    private sealed class BareCompanyService(IAppDbContext db, ICurrentUser user, IClock clock)
        : CrudService<Company, Application.Features.Companies.CompanyDto, Application.Features.Companies.SaveCompanyRequest>(db, user, clock)
    {
        protected override DbSet<Company> Set => Db.Companies;
        protected override string Noun => "Company";
        public override IQueryable<Application.Features.Companies.CompanyDto> Query() =>
            Db.Companies.Select(c => new Application.Features.Companies.CompanyDto { Id = c.Id, Name = c.Name });
        protected override int OwnerOf(Application.Features.Companies.SaveCompanyRequest r) => r.OwnerId;
        protected override void Apply(Company c, Application.Features.Companies.SaveCompanyRequest r) { c.Name = r.Name; c.Industry = r.Industry; c.City = r.City; c.Country = r.Country; }
    }

    [Fact, Trait("AC", "PERM-1")]
    public async Task DefaultHooks_AllowSaveAndArchive()
    {
        using var db = new TestDb();
        var service = new BareCompanyService(db.Context, FakeUser.Manager, db.Clock);

        var created = await service.CreateAsync(new("Bare", "Retail", null, null, "Lahore", "Pakistan", 1, 0, db.Rep.Id), CancellationToken.None);
        await service.ArchiveAsync(created.Id, CancellationToken.None);

        Assert.Empty(await service.Query().ToListAsync());
    }

    [Fact, Trait("AC", "CO-2"), Trait("Finding", "F-04")]
    public async Task UniqueIndexViolation_BecomesAConflict_WithTheDefaultMessage()
    {
        // The bare service has no duplicate pre-check, like two requests that both passed it at the same moment.
        using var db = new TestDb();
        db.AddCompany("Bare");
        var service = new BareCompanyService(db.Context, FakeUser.Rep, db.Clock);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new("BARE ", "Retail", null, null, "Lahore", "Pakistan", 1, 0, db.Rep.Id), CancellationToken.None));

        Assert.Equal("This company already exists.", ex.Message);
        Assert.Equal(1, await db.NewContext().Companies.CountAsync());
    }

    [Fact, Trait("AC", "CO-4")]
    public async Task UnknownLead_IsReportedByName()
    {
        using var db = new TestDb();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => new LeadService(db.Context, FakeUser.Rep, db.Clock).GetAsync(404, CancellationToken.None));
        Assert.Equal("Lead not found.", ex.Message);
    }
}
