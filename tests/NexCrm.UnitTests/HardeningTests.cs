using Microsoft.EntityFrameworkCore;
using NexCrm.Api.Infrastructure;
using NexCrm.Application.Common;
using NexCrm.Application.Features.Auth;
using NexCrm.Application.Features.Companies;
using NexCrm.Domain.Entities;

namespace NexCrm.UnitTests;

public sealed class LoginThrottleTests
{
    [Fact, Trait("AC", "AUTH-7"), Trait("Finding", "F-08")]
    public void FiveFailuresFromOneAddress_LockThatPair_ForFifteenMinutes()
    {
        var clock = new FixedClock(TestDb.Now);
        var throttle = TestDb.Throttle(clock);
        for (var i = 0; i < 5; i++) throttle.RecordFailure("sara", "10.0.0.1");

        var ex = Assert.Throws<TooManyRequestsException>(() => throttle.EnsureAllowed("sara", "10.0.0.1"));
        Assert.Equal("Too many failed sign-ins. Try again in 15 minutes.", ex.Message);
        throttle.EnsureAllowed("sara", "10.0.0.2");     // the real user at another desk is not locked out

        clock.Now = TestDb.Now.AddMinutes(15).AddSeconds(1);
        throttle.EnsureAllowed("sara", "10.0.0.1");
    }

    [Fact, Trait("AC", "AUTH-7"), Trait("Finding", "F-08")]
    public void GuessingFromManyAddresses_LocksTheAccount()
    {
        var throttle = TestDb.Throttle(new FixedClock(TestDb.Now), perSource: 5, perAccount: 20);
        for (var i = 0; i < 20; i++) throttle.RecordFailure("sara", $"10.0.0.{i}");

        Assert.Throws<TooManyRequestsException>(() => throttle.EnsureAllowed("sara", "192.168.1.50"));
    }

    [Fact, Trait("AC", "AUTH-7"), Trait("Finding", "F-08")]
    public void SuccessResetsTheAddressCounter_AndOldFailuresAge_Out()
    {
        var clock = new FixedClock(TestDb.Now);
        var throttle = TestDb.Throttle(clock);
        for (var i = 0; i < 4; i++) throttle.RecordFailure("sara", "10.0.0.1");
        throttle.RecordSuccess("sara", "10.0.0.1");
        for (var i = 0; i < 4; i++) throttle.RecordFailure("sara", "10.0.0.1");
        throttle.EnsureAllowed("sara", "10.0.0.1");

        clock.Now = TestDb.Now.AddMinutes(16);
        throttle.RecordFailure("sara", "10.0.0.1");     // a new window: this is failure 1, not 5
        throttle.EnsureAllowed("sara", "10.0.0.1");
    }

    [Fact, Trait("AC", "AUTH-7"), Trait("Finding", "F-08")]
    public async Task LockedAccount_IsRefusedEvenWithTheRightPassword_AndTheFailureIsAudited()
    {
        using var db = new TestDb();
        var throttle = TestDb.Throttle(db.Clock);
        var auth = TestDb.Auth(db, throttle);
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(new LoginRequest("sara", "nope"), "10.0.0.1", CancellationToken.None));

        await Assert.ThrowsAsync<TooManyRequestsException>(() => auth.LoginAsync(new LoginRequest("sara", "sara"), "10.0.0.1", CancellationToken.None));
        Assert.Equal(5, await db.NewContext().AuditLog.CountAsync(a => a.Action == "Auth.SignInFailed"));
    }
}

public sealed class SessionTests
{
    [Fact, Trait("AC", "AUTH-9"), Trait("Finding", "F-02")]
    public async Task Logout_BumpsTheSessionVersion_SoOldTokensFail()
    {
        using var db = new TestDb();
        var auth = TestDb.Auth(db);
        Assert.True(await auth.IsSessionCurrentAsync(db.Rep.Id, 0, CancellationToken.None));

        await auth.LogoutAsync(db.Rep.Id, CancellationToken.None);

        Assert.False(await auth.IsSessionCurrentAsync(db.Rep.Id, 0, CancellationToken.None));
        Assert.True(await auth.IsSessionCurrentAsync(db.Rep.Id, 1, CancellationToken.None));
        Assert.Equal(1, await db.NewContext().AuditLog.CountAsync(a => a.Action == "Auth.SignedOut"));
    }

    [Fact, Trait("AC", "AUTH-3"), Trait("Finding", "F-01")]
    public async Task DeactivatedUser_HasNoCurrentSession()
    {
        using var db = new TestDb();
        var gone = db.AddUser("gone", UserRole.SalesRep, active: false);
        Assert.False(await TestDb.Auth(db).IsSessionCurrentAsync(gone.Id, 0, CancellationToken.None));
    }
}

public sealed class OwnershipAndAuditTests
{
    private static SaveCompanyRequest Request(int ownerId) => new("Acme Ltd", "Software", null, null, "Karachi", "Pakistan", 5, 0, ownerId);

    [Fact, Trait("AC", "PERM-4"), Trait("Finding", "F-03")]
    public async Task Rep_ReassigningAColleaguesRecord_IsForbidden_AndNothingIsSaved()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd", ownerId: db.Manager.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new CompanyService(db.Context, FakeUser.RepAs(db.Rep.Id), db.Clock).UpdateAsync(company.Id, Request(db.Rep.Id), CancellationToken.None));

        Assert.Equal(db.Manager.Id, (await db.NewContext().Companies.SingleAsync()).OwnerId);
    }

    [Fact, Trait("AC", "PERM-4"), Trait("Finding", "F-03")]
    public async Task Rep_HandingOverTheirOwnRecord_IsAllowed_AndAudited()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme Ltd", ownerId: db.Rep.Id);

        await new CompanyService(db.Context, FakeUser.RepAs(db.Rep.Id), db.Clock).UpdateAsync(company.Id, Request(db.Manager.Id), CancellationToken.None);

        var entry = await db.NewContext().AuditLog.SingleAsync(a => a.Action == "Company.Updated");
        Assert.Equal($"Owner {db.Rep.Id} → {db.Manager.Id}", entry.Summary);
        Assert.Equal("127.0.0.1", entry.IpAddress);
    }

    [Fact, Trait("AC", "X-6"), Trait("Finding", "F-06")]
    public async Task CreateAndArchive_LeaveAnAuditTrail_WithTheActorAndTime()
    {
        using var db = new TestDb();
        var service = new CompanyService(db.Context, FakeUser.Manager, db.Clock);
        var created = await service.CreateAsync(Request(db.Rep.Id), CancellationToken.None);
        await service.ArchiveAsync(created.Id, CancellationToken.None);

        var trail = await db.NewContext().AuditLog.OrderBy(a => a.Id).Select(a => new { a.Action, a.Username, a.EntityId, a.At }).ToListAsync();
        Assert.Equal(["Company.Created", "Company.Archived"], trail.Select(a => a.Action));
        Assert.All(trail, a => Assert.Equal(("manager-test", created.Id.ToString(), TestDb.Now), (a.Username, a.EntityId, a.At)));
    }

    [Fact, Trait("AC", "CO-2"), Trait("Finding", "F-04")]
    public async Task DatabaseRejectsADuplicateNameKey_EvenWhenTheCheckIsBypassed()
    {
        using var db = new TestDb();
        db.AddCompany("Acme Ltd");
        db.Context.Companies.Add(new Company { Name = " ACME LTD", Industry = "x", City = "x", Country = "x", OwnerId = db.Rep.Id });

        await Assert.ThrowsAsync<UniqueConstraintException>(() => db.Context.SaveChangesAsync());
    }

    [Fact, Trait("AC", "CO-5"), Trait("Finding", "F-04")]
    public async Task ArchivedCompany_FreesItsName()
    {
        using var db = new TestDb();
        var old = db.AddCompany("Acme Ltd");
        old.Archive(TestDb.Now);
        await db.Context.SaveChangesAsync();

        var reborn = await new CompanyService(db.Context, FakeUser.Rep, db.Clock).CreateAsync(Request(db.Rep.Id), CancellationToken.None);

        Assert.NotEqual(old.Id, reborn.Id);
    }
}

public sealed class DevSigningKeyTests
{
    [Fact, Trait("Finding", "F-14")]
    public void CreatesARandomKeyOnce_ThenReusesIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexcrm-key-tests", Guid.NewGuid().ToString("N"), "dev-signing.key");
        try
        {
            var first = DevSigningKey.LoadOrCreate(path);
            var second = DevSigningKey.LoadOrCreate(path);
            var other = DevSigningKey.LoadOrCreate(path + ".other");

            Assert.True(first.Length >= 32);
            Assert.Equal(first, second);
            Assert.NotEqual(first, other);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact, Trait("Finding", "F-14")]
    public void NoSigningKeyIsCommittedInSettings()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "NexCrm.slnx"))) root = Path.GetDirectoryName(root)!;
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "NexCrm.Api"), "appsettings*.json"))
            Assert.DoesNotMatch("\"SigningKey\"\\s*:\\s*\"[^\"]+\"", File.ReadAllText(file));
    }
}
