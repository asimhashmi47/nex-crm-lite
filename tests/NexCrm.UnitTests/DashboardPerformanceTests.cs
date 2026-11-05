using NexCrm.Application.Features.Dashboard;
using NexCrm.Domain.Entities;

namespace NexCrm.UnitTests;

/// <summary>Regression tests for the load-test findings (load-tests/LOAD-TEST-REPORT.md). Each failed before its fix.</summary>
public sealed class LeaderboardTests
{
    [Fact, Trait("AC", "DB-5"), Trait("LoadFix", "1")]
    public async Task Leaderboard_IsTheTopTenRepsWithDeals_NotEveryUser()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        var reps = Enumerable.Range(1, 30).Select(i => db.AddUser($"rep{i:00}", UserRole.SalesRep)).ToList();
        for (var i = 0; i < 12; i++) db.AddDeal(company, (i + 1) * 1_000, DealStage.Won, closedAt: TestDb.Now.AddDays(-10), ownerId: reps[i].Id);

        var board = (await new DashboardService(db.Context, db.Clock).GetAsync(CancellationToken.None)).Leaderboard;

        Assert.Equal(10, board.Count);
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(i => i * 1_000m), board.Select(r => r.Won));
        Assert.Equal("Rep12 Test", board[0].Owner);
        Assert.DoesNotContain(board, r => r.Won == 0 && r.Pipeline == 0);
    }

    [Fact, Trait("AC", "DB-5"), Trait("LoadFix", "1")]
    public async Task Leaderboard_RanksByWonThenPipeline_AndIgnoresOldWinsAndArchivedDeals()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        var a = db.AddUser("alpha", UserRole.SalesRep);
        var b = db.AddUser("bravo", UserRole.SalesRep);
        var c = db.AddUser("charlie", UserRole.SalesRep);
        db.AddDeal(company, 5_000, DealStage.Won, closedAt: TestDb.Now.AddDays(-20), ownerId: a.Id);
        db.AddDeal(company, 9_000, DealStage.Won, closedAt: TestDb.Now.AddMonths(-6), ownerId: b.Id);   // outside the window
        db.AddDeal(company, 7_000, DealStage.Proposal, ownerId: b.Id);
        db.AddDeal(company, 3_000, DealStage.Negotiation, ownerId: c.Id);
        db.AddDeal(company, 99_000, DealStage.Won, closedAt: TestDb.Now.AddDays(-1), ownerId: c.Id).Archive(TestDb.Now);
        await db.Context.SaveChangesAsync();

        var board = (await new DashboardService(db.NewContext(), db.Clock).GetAsync(CancellationToken.None)).Leaderboard;

        Assert.Equal(["Alpha Test", "Bravo Test", "Charlie Test"], board.Select(r => r.Owner));
        Assert.Equal((5_000m, 1, 0m), (board[0].Won, board[0].Deals, board[0].Pipeline));
        Assert.Equal((0m, 0, 7_000m), (board[1].Won, board[1].Deals, board[1].Pipeline));
        Assert.Equal(3_000m, board[2].Pipeline);
    }

    [Fact, Trait("AC", "DB-3"), Trait("LoadFix", "4")]
    public async Task Trend_CountsLeadsAndWinsPerMonth_AcrossMonthBoundaries()
    {
        using var db = new TestDb();
        var company = db.AddCompany("Acme");
        db.AddDeal(company, 1_000, DealStage.Won, closedAt: new DateTime(2026, 10, 31, 23, 59, 0));
        db.AddDeal(company, 2_000, DealStage.Won, closedAt: new DateTime(2026, 11, 1, 0, 0, 0));
        db.AddDeal(company, 4_000, DealStage.Lost, closedAt: new DateTime(2026, 11, 2));
        db.AddLead(email: "a@x.test", createdAt: new DateTime(2026, 10, 31, 23, 0, 0));
        db.AddLead(email: "b@x.test", createdAt: new DateTime(2026, 11, 1, 1, 0, 0));
        db.AddLead(email: "c@x.test", createdAt: new DateTime(2026, 11, 3));
        db.AddLead(email: "d@x.test", createdAt: new DateTime(2025, 11, 30));     // before the 12-month window

        var trend = (await new DashboardService(db.Context, db.Clock).GetAsync(CancellationToken.None)).Trend;

        Assert.Equal((1_000m, 1), (trend.Single(p => p.Month == "Oct 26").Won, trend.Single(p => p.Month == "Oct 26").NewLeads));
        Assert.Equal((2_000m, 2), (trend.Single(p => p.Month == "Nov 26").Won, trend.Single(p => p.Month == "Nov 26").NewLeads));
        Assert.Equal(3, trend.Sum(p => p.NewLeads));
    }
}

public sealed class CachedDashboardTests
{
    private static DashboardDto Empty(int marker) => new(
        new KpiDto(marker, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), [], [], [], [], [], []);

    [Fact, Trait("AC", "DB-6"), Trait("LoadFix", "2")]
    public async Task WithinThirtySeconds_EveryCallerGetsTheSameResult()
    {
        var clock = new FixedClock(TestDb.Now);
        var calls = 0;
        var cache = new CachedDashboard(_ => Task.FromResult(Empty(++calls)), clock);

        var first = await cache.GetAsync(CancellationToken.None);
        clock.Now = TestDb.Now.AddSeconds(29);
        var second = await cache.GetAsync(CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Same(first, second);
    }

    [Fact, Trait("AC", "DB-6"), Trait("LoadFix", "2")]
    public async Task AfterThirtySeconds_ItRecomputes()
    {
        var clock = new FixedClock(TestDb.Now);
        var calls = 0;
        var cache = new CachedDashboard(_ => Task.FromResult(Empty(++calls)), clock);

        await cache.GetAsync(CancellationToken.None);
        clock.Now = TestDb.Now.AddSeconds(31);
        var fresh = await cache.GetAsync(CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(2, fresh.Kpis.OpenPipeline);
    }

    [Fact, Trait("AC", "DB-6"), Trait("LoadFix", "2")]
    public async Task ManyCallersOnAColdCache_TriggerOneComputation()
    {
        var calls = 0;
        var gate = new TaskCompletionSource();
        var cache = new CachedDashboard(async _ => { Interlocked.Increment(ref calls); await gate.Task; return Empty(1); }, new FixedClock(TestDb.Now));

        var callers = Enumerable.Range(0, 50).Select(_ => cache.GetAsync(CancellationToken.None)).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact, Trait("AC", "DB-6"), Trait("LoadFix", "2")]
    public async Task AFailedComputation_IsNotCached()
    {
        var calls = 0;
        var cache = new CachedDashboard(_ => ++calls == 1 ? throw new InvalidOperationException("db down") : Task.FromResult(Empty(calls)), new FixedClock(TestDb.Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(CancellationToken.None));
        var retried = await cache.GetAsync(CancellationToken.None);

        Assert.Equal(2, retried.Kpis.OpenPipeline);
    }
}
