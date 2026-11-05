using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexCrm.Application.Common;
using NexCrm.Application.Features.Auth;
using NexCrm.Application.Features.Companies;
using NexCrm.Application.Features.Contacts;
using NexCrm.Application.Features.Deals;
using NexCrm.Application.Features.Leads;
using NexCrm.Application.Features.Lookups;
using NexCrm.Application.Features.Tasks;
using NexCrm.Domain.Common;
using NexCrm.Domain.Entities;
using NexCrm.Infrastructure.Services;

namespace NexCrm.UnitTests;

public sealed class AuthServiceTests
{
    private static AuthService Service(TestDb db) => TestDb.Auth(db);

    [Fact, Trait("AC", "AUTH-1")]
    public async Task Login_ValidCredentials_ReturnsTokenAndUser()
    {
        using var db = new TestDb();

        var result = await Service(db).LoginAsync(new LoginRequest("  SARA ", "sara"), "127.0.0.1", CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.Equal("sara", result.User.Username);
        Assert.Equal("Sales rep", result.User.Role);
        Assert.False(result.User.CanDelete);
    }

    [Fact, Trait("AC", "AUTH-1")]
    public async Task Login_PasswordWithSurroundingSpaces_IsNotTrimmed()
    {
        using var db = new TestDb();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).LoginAsync(new LoginRequest("sara", " sara "), "127.0.0.1", CancellationToken.None));
    }

    [Theory, Trait("AC", "AUTH-2")]
    [InlineData("sara", "wrong")]
    [InlineData("nobody", "sara")]
    public async Task Login_BadCredentials_FailWithTheSameMessage(string username, string password)
    {
        using var db = new TestDb();
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).LoginAsync(new LoginRequest(username, password), "127.0.0.1", CancellationToken.None));
        Assert.Equal("Wrong username or password.", ex.Message);
    }

    [Fact, Trait("AC", "AUTH-3")]
    public async Task Login_InactiveUser_IsRefused()
    {
        using var db = new TestDb();
        db.AddUser("gone", UserRole.SalesRep, active: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).LoginAsync(new LoginRequest("gone", "gone"), "127.0.0.1", CancellationToken.None));
    }

    [Fact, Trait("AC", "AUTH-3")]
    public async Task Me_InactiveUser_IsRefused()
    {
        using var db = new TestDb();
        var gone = db.AddUser("gone", UserRole.SalesRep, active: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).MeAsync(gone.Id, CancellationToken.None));
    }

    [Fact, Trait("AC", "AUTH-8")]
    public async Task Login_Success_RecordsLastLoginFromClock()
    {
        using var db = new TestDb();
        await Service(db).LoginAsync(new LoginRequest("hira", "hira"), "127.0.0.1", CancellationToken.None);
        Assert.Equal(TestDb.Now, (await db.NewContext().Users.SingleAsync(u => u.Username == "hira")).LastLoginAt);
    }

    [Fact, Trait("AC", "PERM-1")]
    public void Managers_AndAdmins_CanDelete_SalesRepsCannot()
    {
        Assert.True(AuthService.ToDto(new User { Role = UserRole.Admin }).CanDelete);
        Assert.True(AuthService.ToDto(new User { Role = UserRole.Manager }).CanDelete);
        Assert.False(AuthService.ToDto(new User { Role = UserRole.SalesRep }).CanDelete);
    }
}

public sealed class CompanyServiceTests
{
    private static SaveCompanyRequest Request(string name, int ownerId, string? website = "  ", string? phone = null) =>
        new(name, " Software ", website, phone, " Karachi ", "Pakistan", 10, 0, ownerId);

    [Fact, Trait("AC", "CO-1")]
    public async Task Create_Valid_TrimsTextAndNullsBlanks()
    {
        using var db = new TestDb();
        var service = new CompanyService(db.Context, FakeUser.Rep, db.Clock);

        var saved = await service.CreateAsync(Request("  Acme Ltd  ", db.Rep.Id), CancellationToken.None);

        Assert.Equal("Acme Ltd", saved.Name);
        Assert.Equal("Software", saved.Industry);
        Assert.Null(saved.Website);
        Assert.Equal(TestDb.Now, saved.CreatedAt);
    }

    [Theory, Trait("AC", "CO-2")]
    [InlineData("Acme Ltd")]
    [InlineData("acme ltd")]
    [InlineData("  ACME LTD ")]
    public async Task Create_DuplicateNameIgnoringCase_IsRefused(string name)
    {
        using var db = new TestDb();
        db.AddCompany("Acme Ltd");
        var service = new CompanyService(db.Context, FakeUser.Rep, db.Clock);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request(name, db.Rep.Id), CancellationToken.None));
    }

    [Fact, Trait("AC", "CO-2")]
    public async Task Update_KeepingItsOwnName_IsAllowed()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd");
        var service = new CompanyService(db.Context, FakeUser.Rep, db.Clock);

        var saved = await service.UpdateAsync(company.Id, Request("Acme Ltd", db.Rep.Id), CancellationToken.None);

        Assert.Equal(company.Id, saved.Id);
    }

    [Fact, Trait("AC", "X-5")]
    public async Task Update_StampsUpdatedAtFromClock()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd");
        db.Clock.Now = TestDb.Now.AddHours(3);

        await new CompanyService(db.Context, FakeUser.Rep, db.Clock).UpdateAsync(company.Id, Request("Acme Ltd", db.Rep.Id), CancellationToken.None);

        Assert.Equal(TestDb.Now.AddHours(3), (await db.NewContext().Companies.SingleAsync()).UpdatedAt);
    }

    [Fact, Trait("AC", "X-5")]
    public void SaveChanges_SyncPath_StampsCreatedAtLikeTheAsyncPath()
    {
        using var db = new TestDb();
        db.AddCompany("Acme Ltd");   // the helper saves synchronously
        Assert.Equal(TestDb.Now, db.NewContext().Companies.Single().CreatedAt);
    }

    [Fact, Trait("AC", "CO-4")]
    public async Task UpdateOrDelete_UnknownId_IsNotFound()
    {
        using var db = new TestDb();
        var service = new CompanyService(db.Context, FakeUser.Manager, db.Clock);

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(999, Request("X", db.Rep.Id), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ArchiveAsync(999, CancellationToken.None));
    }

    [Theory, Trait("AC", "PERM-2")]
    [InlineData(999)]
    [InlineData(-1)]
    public async Task Create_UnknownOwner_IsRejected(int ownerId)
    {
        using var db = new TestDb();
        var service = new CompanyService(db.Context, FakeUser.Rep, db.Clock);
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(Request("Acme", ownerId), CancellationToken.None));
    }

    [Fact, Trait("AC", "PERM-2")]
    public async Task Create_InactiveOwner_IsRejected()
    {
        using var db = new TestDb();
        var gone = db.AddUser("gone", UserRole.SalesRep, active: false);
        var service = new CompanyService(db.Context, FakeUser.Rep, db.Clock);
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(Request("Acme", gone.Id), CancellationToken.None));
    }

    [Fact, Trait("AC", "PERM-1")]
    public async Task Archive_BySalesRep_IsForbidden()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd");
        var service = new CompanyService(db.Context, FakeUser.Rep, db.Clock);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ArchiveAsync(company.Id, CancellationToken.None));
    }

    [Fact, Trait("AC", "CO-5")]
    public async Task Archive_WithOpenDeals_IsRefused_ButClosedDealsDoNotBlock()
    {
        using var db = new TestDb();
        var open = db.AddCompany("Open Co");
        db.AddDeal(open, 5_000, DealStage.Proposal);
        var closed = db.AddCompany("Closed Co");
        db.AddDeal(closed, 5_000, DealStage.Won);
        var service = new CompanyService(db.Context, FakeUser.Manager, db.Clock);

        await Assert.ThrowsAsync<ConflictException>(() => service.ArchiveAsync(open.Id, CancellationToken.None));
        await service.ArchiveAsync(closed.Id, CancellationToken.None);
    }

    [Fact, Trait("AC", "CO-5")]
    public async Task Archive_HidesFromGridsAndLookups_ButKeepsTheRow()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd");
        await new CompanyService(db.Context, FakeUser.Manager, db.Clock).ArchiveAsync(company.Id, CancellationToken.None);

        using var fresh = db.NewContext();
        Assert.Empty(await new CompanyService(fresh, FakeUser.Rep, db.Clock).Query().ToListAsync());
        Assert.Empty(await new LookupService(fresh).CompaniesAsync("Acme", CancellationToken.None));
        var row = await fresh.Companies.IgnoreQueryFilters().SingleAsync();
        Assert.True(row.IsDeleted);
        Assert.Equal(TestDb.Now, row.DeletedAt);
    }

    [Fact, Trait("AC", "CO-6")]
    public async Task Query_CountsContactsAndDeals_IgnoringArchived()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd");
        db.AddContact(company, "a@acme.test");
        db.AddContact(company, "b@acme.test").Archive(TestDb.Now);
        db.AddDeal(company, 1_000, DealStage.Won);
        db.AddDeal(company, 2_000, DealStage.Won).Archive(TestDb.Now);
        db.AddDeal(company, 3_000, DealStage.Negotiation);
        db.AddDeal(company, 4_000, DealStage.Lost);
        await db.Context.SaveChangesAsync();

        var row = await new CompanyService(db.NewContext(), FakeUser.Rep, db.Clock).Query().SingleAsync();

        Assert.Equal(1, row.Contacts);
        Assert.Equal(1, row.OpenDeals);
        Assert.Equal(1_000m, row.WonRevenue);
    }
}

public sealed class ContactServiceTests
{
    private static SaveContactRequest Request(string email, int ownerId, int? companyId = null, string first = "Zoë") =>
        new(first, "Ahmed", email, null, null, ContactStage.Lead, companyId, ownerId);

    [Fact, Trait("AC", "CT-1")]
    public async Task Create_StoresEmailLowerCase()
    {
        using var db = new TestDb();
        var saved = await new ContactService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(Request("  Zoe@Acme.TEST ", db.Rep.Id), CancellationToken.None);
        Assert.Equal("zoe@acme.test", saved.Email);
    }

    [Fact, Trait("AC", "CT-1")]
    public async Task Create_DuplicateEmailIgnoringCase_IsRefused()
    {
        using var db = new TestDb();
        db.AddContact(null, "zoe@acme.test");
        var service = new ContactService(db.Context, FakeUser.Rep, db.Clock);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Request("ZOE@acme.test", db.Rep.Id), CancellationToken.None));
    }

    [Fact, Trait("AC", "CT-4")]
    public async Task Update_KeepingItsOwnEmail_IsAllowed()
    {
        using var db = new TestDb();
        var contact = db.AddContact(null, "zoe@acme.test");
        var saved = await new ContactService(db.Context, FakeUser.Rep, db.Clock).UpdateAsync(contact.Id, Request("zoe@acme.test", db.Rep.Id), CancellationToken.None);
        Assert.Equal("Zoë Ahmed", saved.Name);
    }

    [Fact, Trait("AC", "CT-2")]
    public async Task Create_UnknownCompany_IsRejected()
    {
        using var db = new TestDb();
        var service = new ContactService(db.Context, FakeUser.Rep, db.Clock);
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(Request("z@acme.test", db.Rep.Id, companyId: 404), CancellationToken.None));
    }

    [Theory, Trait("AC", "CT-3")]
    [InlineData("محمد")]
    [InlineData("李")]
    public async Task Create_UnicodeName_RoundTrips(string first)
    {
        using var db = new TestDb();
        var saved = await new ContactService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(Request("u@acme.test", db.Rep.Id, first: first), CancellationToken.None);
        Assert.Equal(first, saved.FirstName);
    }
}

public sealed class LeadServiceTests
{
    [Fact, Trait("AC", "LD-3")]
    public async Task Convert_Qualified_OpensProspectingDealDueIn45Days()
    {
        using var db = new TestDb();
        var lead = db.AddLead();

        var result = await new LeadService(db.Context, FakeUser.Rep, db.Clock).ConvertAsync(lead.Id, CancellationToken.None);

        using var fresh = db.NewContext();
        var deal = await fresh.Deals.SingleAsync();
        var saved = await fresh.Leads.SingleAsync();
        Assert.Equal(DealStage.Prospecting, deal.Stage);
        Assert.Equal(12_500m, deal.Amount);
        Assert.Equal(TestDb.Now.Date.AddDays(45), deal.ExpectedCloseDate);
        Assert.Equal(LeadStatus.Converted, saved.Status);
        Assert.Equal(result.DealId, saved.ConvertedDealId);
        Assert.Equal(TestDb.Now, saved.ConvertedAt);
    }

    [Fact, Trait("AC", "LD-4")]
    public async Task Convert_NoMatches_CreatesCompanyAndContact()
    {
        using var db = new TestDb();
        var lead = db.AddLead(company: "Brand New Co", email: "first@new.test");

        var result = await new LeadService(db.Context, FakeUser.Rep, db.Clock).ConvertAsync(lead.Id, CancellationToken.None);

        using var fresh = db.NewContext();
        Assert.Equal("Brand New Co", (await fresh.Companies.SingleAsync(c => c.Id == result.CompanyId)).Name);
        var contact = await fresh.Contacts.SingleAsync(c => c.Id == result.ContactId);
        Assert.Equal(("Bilal", "Rana", ContactStage.Opportunity), (contact.FirstName, contact.LastName, contact.Stage));
    }

    [Fact, Trait("AC", "LD-4")]
    public async Task Convert_ReusesCompanyByNameIgnoringCase_AndContactByEmail()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd");
        var contact = db.AddContact(company, "lead@acme.test");
        var lead = db.AddLead(company: "ACME LTD");

        var result = await new LeadService(db.Context, FakeUser.Rep, db.Clock).ConvertAsync(lead.Id, CancellationToken.None);

        Assert.Equal(company.Id, result.CompanyId);
        Assert.Equal(contact.Id, result.ContactId);
        Assert.Equal(1, await db.NewContext().Companies.CountAsync());
    }

    [Theory, Trait("AC", "LD-5")]
    [InlineData(LeadStatus.Unqualified)]
    [InlineData(LeadStatus.Converted)]
    [InlineData(LeadStatus.New)]
    [InlineData(LeadStatus.Contacted)]
    public async Task Convert_AnythingButQualified_IsRefused(LeadStatus status)
    {
        using var db = new TestDb();
        var lead = db.AddLead(status);
        await Assert.ThrowsAsync<DomainException>(() => new LeadService(db.Context, FakeUser.Rep, db.Clock).ConvertAsync(lead.Id, CancellationToken.None));
        Assert.Equal(0, await db.NewContext().Deals.CountAsync());
    }

    [Fact, Trait("AC", "LD-5")]
    public async Task Update_ConvertedLead_IsReadOnly()
    {
        using var db = new TestDb();
        var lead = db.AddLead();
        var service = new LeadService(db.Context, FakeUser.Rep, db.Clock);
        await service.ConvertAsync(lead.Id, CancellationToken.None);

        var edit = new SaveLeadRequest("A B", "C", "a@c.test", null, LeadSource.Website, LeadStatus.Qualified, 50, 100, db.Rep.Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(lead.Id, edit, CancellationToken.None));
    }
}

public sealed class DealServiceTests
{
    private static SaveDealRequest Request(int companyId, int ownerId, DealStage stage = DealStage.Proposal, int probability = 40, int? contactId = null) =>
        new("Renewal", 9_000, stage, probability, TestDb.Now.AddDays(20), companyId, contactId, ownerId);

    [Fact, Trait("AC", "DL-4")]
    public async Task Create_OpenStage_KeepsRepForecast()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        var saved = await new DealService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(Request(company.Id, db.Rep.Id), CancellationToken.None);
        Assert.Equal((DealStage.Proposal, 40, 3_600m), (saved.Stage, saved.Probability, saved.Weighted));
    }

    [Fact, Trait("AC", "DL-3")]
    public async Task Create_AlreadyWon_StampsClosedAtAndIgnoresForecast()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        var saved = await new DealService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(Request(company.Id, db.Rep.Id, DealStage.Won, 30), CancellationToken.None);
        Assert.Equal((100, (DateTime?)TestDb.Now), (saved.Probability, saved.ClosedAt));
    }

    [Fact, Trait("AC", "DL-3")]
    public async Task Move_ToWonThenBack_ClosesAndReopens()
    {
        using var db = new TestDb();
        var deal = db.AddDeal(db.AddCompany("Acme"), 7_500, DealStage.Negotiation);
        var service = new DealService(db.Context, FakeUser.Rep, db.Clock);

        var won = await service.MoveAsync(deal.Id, DealStage.Won, CancellationToken.None);
        Assert.Equal((DealStage.Won, 100, (DateTime?)TestDb.Now), (won.Stage, won.Probability, won.ClosedAt));

        var reopened = await service.MoveAsync(deal.Id, DealStage.Proposal, CancellationToken.None);
        Assert.Equal((50, (DateTime?)null), (reopened.Probability, reopened.ClosedAt));
    }

    [Fact, Trait("AC", "DL-5")]
    public async Task Move_UnknownDeal_IsNotFound()
    {
        using var db = new TestDb();
        await Assert.ThrowsAsync<NotFoundException>(() => new DealService(db.Context, FakeUser.Rep, db.Clock).MoveAsync(404, DealStage.Won, CancellationToken.None));
    }

    [Fact, Trait("AC", "DL-7")]
    public async Task Create_UnknownCompanyOrContact_IsRejected()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        var service = new DealService(db.Context, FakeUser.Rep, db.Clock);

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(Request(404, db.Rep.Id), CancellationToken.None));
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(Request(company.Id, db.Rep.Id, contactId: 404), CancellationToken.None));
    }

    [Fact, Trait("AC", "DL-6")]
    public async Task Board_CountsAndTotalsEveryDeal_ButCapsCardsLargestFirst()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        for (var i = 1; i <= DealService.CardsPerColumn + 5; i++) db.AddDeal(company, i * 100, DealStage.Negotiation);

        var board = await new DealService(db.Context, FakeUser.Rep, db.Clock).BoardAsync(null, CancellationToken.None);
        var column = board.Single(c => c.Stage == DealStage.Negotiation);

        Assert.Equal(Enum.GetValues<DealStage>(), board.Select(c => c.Stage));
        Assert.Equal(45, column.Count);
        Assert.Equal(Enumerable.Range(1, 45).Sum(i => i * 100m), column.Total);
        Assert.Equal(DealService.CardsPerColumn, column.Cards.Count);
        Assert.Equal(4_500m, column.Cards[0].Amount);
    }

    [Fact, Trait("AC", "DL-6")]
    public async Task Board_ClosedColumnsOnlyShowTheLast90Days()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        db.AddDeal(company, 1_000, DealStage.Won, closedAt: TestDb.Now.AddDays(-89));
        db.AddDeal(company, 2_000, DealStage.Won, closedAt: TestDb.Now.AddDays(-91));

        var won = (await new DealService(db.Context, FakeUser.Rep, db.Clock).BoardAsync(null, CancellationToken.None)).Single(c => c.Stage == DealStage.Won);

        Assert.Equal((1, 1_000m), (won.Count, won.Total));
    }

    [Fact, Trait("AC", "DL-6")]
    public async Task Board_OwnerFilter_AppliesToCountsAndCards()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        db.AddDeal(company, 1_000, DealStage.Proposal, ownerId: db.Rep.Id);
        db.AddDeal(company, 2_000, DealStage.Proposal, ownerId: db.Manager.Id);

        var column = (await new DealService(db.Context, FakeUser.Rep, db.Clock).BoardAsync(db.Manager.Id, CancellationToken.None)).Single(c => c.Stage == DealStage.Proposal);

        Assert.Equal((1, 2_000m, 1), (column.Count, column.Total, column.Cards.Count));
    }

    [Fact, Trait("AC", "DL-6")]
    public async Task Board_FlagsOpenDealsPastTheirCloseDate()
    {
        using var db = new TestDb();
        var deal = db.AddDeal(db.AddCompany("Acme"), 1_000, DealStage.Proposal);
        deal.ExpectedCloseDate = TestDb.Now.Date.AddDays(-1);
        await db.Context.SaveChangesAsync();

        var card = (await new DealService(db.NewContext(), FakeUser.Rep, db.Clock).BoardAsync(null, CancellationToken.None)).SelectMany(c => c.Cards).Single();

        Assert.True(card.Overdue);
    }
}

public sealed class TaskServiceTests
{
    [Fact, Trait("AC", "TK-2")]
    public async Task SetDone_TouchesContact_AndReopenClearsCompletion()
    {
        using var db = new TestDb();
        var contact = db.AddContact(null, "c@acme.test");
        var task = db.AddTask(TestDb.Now.AddDays(-1), contact: contact);
        var service = new TaskService(db.Context, FakeUser.Rep, db.Clock);

        var done = await service.SetDoneAsync(task.Id, true, CancellationToken.None);
        Assert.Equal((true, (DateTime?)TestDb.Now, false), (done.IsDone, done.CompletedAt, done.Overdue));
        Assert.Equal(TestDb.Now, (await db.NewContext().Contacts.SingleAsync()).LastContactedAt);

        var reopened = await service.SetDoneAsync(task.Id, false, CancellationToken.None);
        Assert.Equal((false, (DateTime?)null), (reopened.IsDone, reopened.CompletedAt));
    }

    [Fact, Trait("AC", "TK-3")]
    public async Task Overdue_UsesTheInjectedClock()
    {
        using var db = new TestDb();
        db.AddTask(TestDb.Now.AddMinutes(-1));
        db.AddTask(TestDb.Now.AddMinutes(1));
        db.AddTask(TestDb.Now.AddDays(-3), done: true);

        var rows = await new TaskService(db.Context, FakeUser.Rep, db.Clock).Query().OrderBy(t => t.DueAt).ToListAsync();

        Assert.Equal([false, true, false], rows.Select(t => t.Overdue));
    }

    [Fact, Trait("AC", "TK-1")]
    public async Task Create_UnknownContactOrDeal_IsRejected()
    {
        using var db = new TestDb();
        var service = new TaskService(db.Context, FakeUser.Rep, db.Clock);

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(new("Call", TaskType.Call, TaskPriority.Low, TestDb.Now, null, 404, null, db.Rep.Id), CancellationToken.None));
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(new("Call", TaskType.Call, TaskPriority.Low, TestDb.Now, null, null, 404, db.Rep.Id), CancellationToken.None));
    }
}

public sealed class LookupServiceTests
{
    [Fact, Trait("AC", "LK-1")]
    public async Task Companies_AreCappedAt25_AndFilteredByTerm()
    {
        using var db = new TestDb();
        for (var i = 0; i < 30; i++) db.AddCompany($"Alpha {i:00}");
        db.AddCompany("Beta");
        var lookups = new LookupService(db.Context);

        Assert.Equal(25, (await lookups.CompaniesAsync("", CancellationToken.None)).Count);
        Assert.Equal("Beta", (await lookups.CompaniesAsync("Bet", CancellationToken.None)).Single().Label);
    }

    [Fact, Trait("AC", "LK-1")]
    public async Task Owners_ListActiveUsersOnly()
    {
        using var db = new TestDb();
        db.AddUser("gone", UserRole.SalesRep, active: false);
        var owners = await new LookupService(db.Context).OwnersAsync(CancellationToken.None);
        Assert.DoesNotContain(owners, o => o.Label.StartsWith("Gone"));
        Assert.Equal(2, owners.Count);
    }
}
