using NexCrm.Application.Common;

namespace NexCrm.Application.Features.Dashboard;

public sealed record AlertsDto(int OverdueTasks, int TasksDueToday, int ClosingSoon, int NewLeadsThisMonth, decimal OpenPipeline);

/// <summary>
/// The dashboard is the same for the whole team, so it is computed at most once every 30 seconds and shared.
/// Callers that arrive while it is being computed wait for that one computation instead of starting their own;
/// a failed computation is not kept. Figures can be up to 30 seconds old, which suits a summary page.
/// </summary>
public sealed class CachedDashboard(Func<CancellationToken, Task<DashboardDto>> compute, IClock clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DashboardDto Value, DateTime At)? _entry;

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        if (Fresh() is { } hit) return hit;

        await _gate.WaitAsync(ct);
        try
        {
            if (Fresh() is { } computedMeanwhile) return computedMeanwhile;
            var value = await compute(ct);
            _entry = (value, clock.Now);
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlertsDto> AlertsAsync(CancellationToken ct)
    {
        var d = await GetAsync(ct);
        return new AlertsDto(d.Kpis.OverdueTasks, d.Kpis.TasksDueToday, d.ClosingSoon.Count, d.Kpis.NewLeadsThisMonth, d.Kpis.OpenPipeline);
    }

    private DashboardDto? Fresh() => _entry is { } e && clock.Now - e.At < Lifetime ? e.Value : null;
}
