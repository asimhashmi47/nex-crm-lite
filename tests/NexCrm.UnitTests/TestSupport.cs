using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;
using NexCrm.Infrastructure.Persistence;

namespace NexCrm.UnitTests;

/// <summary>A real SQLite database in memory, so tests exercise the same SQL translation the app uses.</summary>
public sealed class TestDb : IDisposable
{
    public static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        Context = NewContext();
        Context.Database.EnsureCreated();

        Rep = AddUser("sara", UserRole.SalesRep);
        Manager = AddUser("hira", UserRole.Manager);
    }

    public FixedClock Clock { get; } = new(Now);
    public AppDbContext Context { get; }
    public User Rep { get; }
    public User Manager { get; }

    /// <summary>A second context on the same database, for checking what was really written.</summary>
    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, Clock);

    public User AddUser(string username, UserRole role, bool active = true, string? password = null)
    {
        var user = new User { Username = username, FullName = $"{char.ToUpper(username[0])}{username[1..]} Test", Email = $"{username}@test", Role = role, IsActive = active };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password ?? username);
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    public Company AddCompany(string name, int? ownerId = null)
    {
        var company = new Company { Name = name, Industry = "Software", City = "Karachi", Country = "Pakistan", OwnerId = ownerId ?? Rep.Id };
        Context.Companies.Add(company);
        Context.SaveChanges();
        return company;
    }

    public Contact AddContact(Company? company, string email, string first = "Ali", string last = "Khan")
    {
        var contact = new Contact { FirstName = first, LastName = last, Email = email, Company = company, OwnerId = Rep.Id };
        Context.Contacts.Add(contact);
        Context.SaveChanges();
        return contact;
    }

    public Deal AddDeal(Company company, decimal amount, DealStage stage, DateTime? closedAt = null, int? ownerId = null, DateTime? createdAt = null)
    {
        var deal = new Deal
        {
            Title = $"{company.Name} deal",
            Amount = amount,
            Company = company,
            OwnerId = ownerId ?? Rep.Id,
            ExpectedCloseDate = Now.Date.AddDays(10),
            CreatedAt = createdAt ?? default,
        };
        deal.MoveTo(stage, closedAt ?? Now);
        Context.Deals.Add(deal);
        Context.SaveChanges();
        return deal;
    }

    public Lead AddLead(LeadStatus status = LeadStatus.Qualified, string company = "Acme Ltd", string email = "lead@acme.test", DateTime? createdAt = null)
    {
        var lead = new Lead
        {
            Name = "Bilal Rana",
            CompanyName = company,
            Email = email,
            Status = status,
            EstimatedValue = 12_500,
            Score = 60,
            OwnerId = Rep.Id,
            CreatedAt = createdAt ?? default,
        };
        Context.Leads.Add(lead);
        Context.SaveChanges();
        return lead;
    }

    public CrmTask AddTask(DateTime dueAt, bool done = false, Contact? contact = null)
    {
        var task = new CrmTask { Subject = "Call back", Type = TaskType.Call, DueAt = dueAt, Contact = contact, OwnerId = Rep.Id };
        if (done) task.Complete(dueAt);
        Context.Tasks.Add(task);
        Context.SaveChanges();
        return task;
    }

    public static NexCrm.Application.Features.Auth.AuthService Auth(TestDb db, NexCrm.Application.Features.Auth.LoginThrottle? throttle = null) => new(
        db.Context, new PasswordHasher<User>(),
        new NexCrm.Infrastructure.Services.JwtTokenService(Microsoft.Extensions.Options.Options.Create(new NexCrm.Application.Features.Auth.JwtOptions { SigningKey = new string('k', 40) })),
        db.Clock, throttle ?? Throttle(db.Clock), new AuditTrail(db.Context, FakeUser.Rep, db.Clock));

    public static NexCrm.Application.Features.Auth.LoginThrottle Throttle(IClock clock, int perSource = 5, int perAccount = 20) => new(
        new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), clock,
        Microsoft.Extensions.Options.Options.Create(new NexCrm.Application.Features.Auth.LoginProtectionOptions { FailedLoginsPerSource = perSource, FailedLoginsPerAccount = perAccount }));

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

public sealed class FixedClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;
}

public sealed class FakeUser(bool isManager, int userId = 1) : ICurrentUser
{
    public int? UserId => userId;
    public string Username => isManager ? "manager-test" : "rep-test";
    public bool IsManager => isManager;
    public string? IpAddress => "127.0.0.1";

    public static FakeUser Rep => new(isManager: false);
    public static FakeUser Manager => new(isManager: true);

    /// <summary>A rep acting as a specific user, for ownership rules.</summary>
    public static FakeUser RepAs(int userId) => new(isManager: false, userId);
}
