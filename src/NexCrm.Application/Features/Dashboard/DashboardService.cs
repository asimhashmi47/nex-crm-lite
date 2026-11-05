using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Features.Dashboard;

public sealed record KpiDto(
    decimal OpenPipeline, int OpenDeals, decimal WeightedForecast,
    decimal WonThisMonth, decimal WonLastMonth, int DealsWonThisMonth,
    decimal WinRate, decimal AvgDealSize,
    int NewLeadsThisMonth, int NewLeadsLastMonth, int OverdueTasks, int TasksDueToday);

public sealed record MonthPointDto(string Month, decimal Won, int NewLeads);
public sealed record StageSliceDto(DealStage Stage, int Count, decimal Amount);
public sealed record SourceSliceDto(LeadSource Source, int Leads, int Converted);
public sealed record RepRowDto(int OwnerId, string Owner, decimal Won, int Deals, decimal Pipeline);
public sealed record DueTaskDto(int Id, string Subject, TaskType Type, TaskPriority Priority, DateTime DueAt, bool Overdue, string? Contact);
public sealed record ClosingDealDto(int Id, string Title, string Company, decimal Amount, DealStage Stage, DateTime ExpectedCloseDate);

public sealed record DashboardDto(
    KpiDto Kpis,
    IReadOnlyList<MonthPointDto> Trend,
    IReadOnlyList<StageSliceDto> Pipeline,
    IReadOnlyList<SourceSliceDto> Sources,
    IReadOnlyList<RepRowDto> Leaderboard,
    IReadOnlyList<DueTaskDto> DueTasks,
    IReadOnlyList<ClosingDealDto> ClosingSoon);

public sealed class DashboardService(IAppDbContext db, IClock clock)
{
    private const int TrendMonths = 12;
    private const int LeaderboardSize = 10;

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var now = clock.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        return new DashboardDto(
            await KpisAsync(now, monthStart, ct),
            await TrendAsync(monthStart, ct),
            await PipelineAsync(ct),
            await SourcesAsync(monthStart.AddMonths(-TrendMonths + 1), ct),
            await LeaderboardAsync(monthStart.AddMonths(-2), ct),
            await DueTasksAsync(now, ct),
            await ClosingSoonAsync(now, ct));
    }

    private async Task<KpiDto> KpisAsync(DateTime now, DateTime monthStart, CancellationToken ct)
    {
        var lastMonthStart = monthStart.AddMonths(-1);
        var open = db.Deals.Where(d => d.Stage != DealStage.Won && d.Stage != DealStage.Lost);
        var won = db.Deals.Where(d => d.Stage == DealStage.Won);

        var openPipeline = await open.SumAsync(d => d.Amount, ct);
        var openCount = await open.CountAsync(ct);
        var weighted = await open.SumAsync(d => d.Amount * d.Probability / 100m, ct);

        var wonThisMonth = await won.Where(d => d.ClosedAt >= monthStart).SumAsync(d => d.Amount, ct);
        var wonLastMonth = await won.Where(d => d.ClosedAt >= lastMonthStart && d.ClosedAt < monthStart).SumAsync(d => d.Amount, ct);
        var dealsWonThisMonth = await won.CountAsync(d => d.ClosedAt >= monthStart, ct);

        // Win rate and deal size look at the last 90 days, long enough to smooth out a quiet week.
        var since = now.AddDays(-90);
        var closedRecently = db.Deals.Where(d => d.ClosedAt >= since);
        var closedCount = await closedRecently.CountAsync(ct);
        var wonRecently = await closedRecently.CountAsync(d => d.Stage == DealStage.Won, ct);
        var wonRecentAmount = await closedRecently.Where(d => d.Stage == DealStage.Won).SumAsync(d => d.Amount, ct);

        var today = now.Date;
        return new KpiDto(
            openPipeline, openCount, Math.Round(weighted, 2),
            wonThisMonth, wonLastMonth, dealsWonThisMonth,
            closedCount == 0 ? 0 : Math.Round(wonRecently * 100m / closedCount, 1),
            wonRecently == 0 ? 0 : Math.Round(wonRecentAmount / wonRecently, 2),
            await db.Leads.CountAsync(l => l.CreatedAt >= monthStart, ct),
            await db.Leads.CountAsync(l => l.CreatedAt >= lastMonthStart && l.CreatedAt < monthStart, ct),
            await db.Tasks.CountAsync(t => !t.IsDone && t.DueAt < now, ct),
            await db.Tasks.CountAsync(t => !t.IsDone && t.DueAt >= today && t.DueAt < today.AddDays(1), ct));
    }

    private async Task<IReadOnlyList<MonthPointDto>> TrendAsync(DateTime monthStart, CancellationToken ct)
    {
        var from = monthStart.AddMonths(-TrendMonths + 1);

        // Grouped in the database: twelve rows come back however many deals and leads there are.
        var won = await db.Deals
            .Where(d => d.Stage == DealStage.Won && d.ClosedAt >= from)
            .GroupBy(d => new { d.ClosedAt!.Value.Year, d.ClosedAt.Value.Month })
            .Select(g => new { Key = g.Key.Year * 100 + g.Key.Month, Won = g.Sum(d => d.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Won, ct);

        var leads = await db.Leads
            .Where(l => l.CreatedAt >= from)
            .GroupBy(l => new { l.CreatedAt.Year, l.CreatedAt.Month })
            .Select(g => new { Key = g.Key.Year * 100 + g.Key.Month, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return Enumerable.Range(0, TrendMonths)
            .Select(i => from.AddMonths(i))
            .Select(m => new MonthPointDto(m.ToString("MMM yy"), won.GetValueOrDefault(MonthKey(m)), leads.GetValueOrDefault(MonthKey(m))))
            .ToList();
    }

    private async Task<IReadOnlyList<StageSliceDto>> PipelineAsync(CancellationToken ct)
    {
        var rows = await db.Deals
            .Where(d => d.Stage != DealStage.Won && d.Stage != DealStage.Lost)
            .GroupBy(d => d.Stage)
            .Select(g => new StageSliceDto(g.Key, g.Count(), g.Sum(d => d.Amount)))
            .ToListAsync(ct);
        return rows.OrderBy(r => r.Stage).ToList();
    }

    private async Task<IReadOnlyList<SourceSliceDto>> SourcesAsync(DateTime from, CancellationToken ct)
    {
        var rows = await db.Leads
            .Where(l => l.CreatedAt >= from)
            .GroupBy(l => l.Source)
            .Select(g => new SourceSliceDto(g.Key, g.Count(), g.Count(l => l.Status == LeadStatus.Converted)))
            .ToListAsync(ct);
        return rows.OrderByDescending(r => r.Leads).ToList();
    }

    /// <summary>
    /// The ten reps with the most won in the window, then the biggest open pipeline. Two grouped queries over
    /// deals, so the cost follows the number of deals, not the number of users (it used to run three correlated
    /// sub-queries per user: 5.9 s with 4,000 users).
    /// </summary>
    private async Task<IReadOnlyList<RepRowDto>> LeaderboardAsync(DateTime from, CancellationToken ct)
    {
        var won = await db.Deals
            .Where(d => d.Stage == DealStage.Won && d.ClosedAt >= from)
            .GroupBy(d => d.OwnerId)
            .Select(g => new { OwnerId = g.Key, Won = g.Sum(d => d.Amount), Deals = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, ct);

        var pipeline = await db.Deals
            .Where(d => d.Stage != DealStage.Won && d.Stage != DealStage.Lost)
            .GroupBy(d => d.OwnerId)
            .Select(g => new { OwnerId = g.Key, Pipeline = g.Sum(d => d.Amount) })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Pipeline, ct);

        var top = won.Keys.Union(pipeline.Keys)
            .Select(id => (Id: id, Won: won.GetValueOrDefault(id)?.Won ?? 0, Deals: won.GetValueOrDefault(id)?.Deals ?? 0, Pipeline: pipeline.GetValueOrDefault(id)))
            .OrderByDescending(r => r.Won).ThenByDescending(r => r.Pipeline).ThenBy(r => r.Id)
            .Take(LeaderboardSize)
            .ToList();

        var ids = top.Select(r => r.Id).ToList();
        var names = await db.Users.Where(u => ids.Contains(u.Id) && u.IsActive).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return top.Where(r => names.ContainsKey(r.Id))
            .Select(r => new RepRowDto(r.Id, names[r.Id], r.Won, r.Deals, r.Pipeline))
            .ToList();
    }

    // Two weeks back is still actionable; anything older belongs on the Tasks page, not the dashboard.
    private Task<List<DueTaskDto>> DueTasksAsync(DateTime now, CancellationToken ct) =>
        db.Tasks
            .Where(t => !t.IsDone && t.DueAt >= now.Date.AddDays(-14) && t.DueAt < now.Date.AddDays(3))
            .OrderBy(t => t.DueAt)
            .Take(8)
            .Select(t => new DueTaskDto(t.Id, t.Subject, t.Type, t.Priority, t.DueAt, t.DueAt < now,
                t.Contact != null ? t.Contact.FirstName + " " + t.Contact.LastName : null))
            .ToListAsync(ct);

    private Task<List<ClosingDealDto>> ClosingSoonAsync(DateTime now, CancellationToken ct) =>
        db.Deals
            .Where(d => d.Stage != DealStage.Won && d.Stage != DealStage.Lost && d.ExpectedCloseDate >= now.Date && d.ExpectedCloseDate < now.Date.AddDays(30))
            .OrderByDescending(d => d.Amount)
            .Take(6)
            .Select(d => new ClosingDealDto(d.Id, d.Title, d.Company.Name, d.Amount, d.Stage, d.ExpectedCloseDate))
            .ToListAsync(ct);

    private static int MonthKey(DateTime d) => d.Year * 100 + d.Month;
}
