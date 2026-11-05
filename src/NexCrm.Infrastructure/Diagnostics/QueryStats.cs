using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NexCrm.Infrastructure.Diagnostics;

public sealed record StatementStats(string Statement, long Count, double TotalMs, double MaxMs, double MeanMs);

/// <summary>
/// Times every SQL statement EF runs, grouped by statement shape. Only registered when Diagnostics:Enabled is on
/// (load tests); the cost is one dictionary update per command.
/// </summary>
public sealed partial class QueryStats : DbCommandInterceptor
{
    private sealed class Bucket
    {
        public long Count;
        public long TotalTicks;
        public long MaxTicks;
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    // Readers are timed when disposed: SQLite does most of a query's work while rows are read, after ReaderExecuted.
    public override InterceptionResult DataReaderDisposing(DbCommand command, DataReaderDisposingEventData data, InterceptionResult result) { Record(command, data.Duration); return result; }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData data, int result) { Record(command, data.Duration); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken ct = default) { Record(command, data.Duration); return ValueTask.FromResult(result); }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData data, object? result) { Record(command, data.Duration); return result; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData data, object? result, CancellationToken ct = default) { Record(command, data.Duration); return ValueTask.FromResult(result); }
    public override void CommandFailed(DbCommand command, CommandErrorEventData data) => Record(command, data.Duration);
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData data, CancellationToken ct = default) { Record(command, data.Duration); return Task.CompletedTask; }

    public IReadOnlyList<StatementStats> Top(int count) => _buckets
        .Select(kv => new StatementStats(kv.Key, kv.Value.Count, Ms(kv.Value.TotalTicks), Ms(kv.Value.MaxTicks), Ms(kv.Value.TotalTicks) / Math.Max(1, kv.Value.Count)))
        .OrderByDescending(s => s.TotalMs)
        .Take(count)
        .ToList();

    public void Reset() => _buckets.Clear();

    private void Record(DbCommand command, TimeSpan duration)
    {
        var bucket = _buckets.GetOrAdd(Shape(command.CommandText), _ => new Bucket());
        Interlocked.Increment(ref bucket.Count);
        Interlocked.Add(ref bucket.TotalTicks, duration.Ticks);
        long max;
        while (duration.Ticks > (max = Interlocked.Read(ref bucket.MaxTicks)) && Interlocked.CompareExchange(ref bucket.MaxTicks, duration.Ticks, max) != max) { }
    }

    // One line, whitespace collapsed, trimmed: enough to tell statements apart without storing every variant.
    private static string Shape(string sql)
    {
        var flat = Whitespace().Replace(sql, " ").Trim();
        return flat.Length > 160 ? flat[..160] : flat;
    }

    private static double Ms(long ticks) => Math.Round(TimeSpan.FromTicks(ticks).TotalMilliseconds, 2);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
