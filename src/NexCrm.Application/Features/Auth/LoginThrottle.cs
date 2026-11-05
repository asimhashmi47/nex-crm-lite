using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NexCrm.Application.Common;

namespace NexCrm.Application.Features.Auth;

public sealed class LoginProtectionOptions
{
    /// <summary>Failures from one address against one account before that pair is locked.</summary>
    public int FailedLoginsPerSource { get; set; } = 5;
    /// <summary>Failures against one account from anywhere before the account itself is locked.</summary>
    public int FailedLoginsPerAccount { get; set; } = 20;
    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>
/// Counts failed sign-ins per account and per account-and-address. The per-address lock stops a guesser
/// without locking the real user out from their own desk; the account-wide lock catches guessing from many
/// addresses at once. State lives in memory, which suits a single API instance.
/// </summary>
public sealed class LoginThrottle(IMemoryCache cache, IClock clock, IOptions<LoginProtectionOptions> options)
{
    private sealed class Counter
    {
        public int Failures;
        public DateTime FirstAt;
        public DateTime? LockedUntil;
    }

    private LoginProtectionOptions O => options.Value;
    private TimeSpan Window => TimeSpan.FromMinutes(O.LockoutMinutes);

    public void EnsureAllowed(string username, string? address)
    {
        var until = new[] { Get(AccountKey(username)), Get(SourceKey(username, address)) }
            .Select(c => c?.LockedUntil).Where(t => t > clock.Now).Max();
        if (until is null) return;

        var minutes = Math.Max(1, (int)Math.Ceiling((until.Value - clock.Now).TotalMinutes));
        throw new TooManyRequestsException($"Too many failed sign-ins. Try again in {minutes} minutes.");
    }

    public void RecordFailure(string username, string? address)
    {
        Bump(AccountKey(username), O.FailedLoginsPerAccount);
        Bump(SourceKey(username, address), Math.Min(O.FailedLoginsPerSource, O.FailedLoginsPerAccount));
    }

    public void RecordSuccess(string username, string? address) => cache.Remove(SourceKey(username, address));

    private void Bump(string key, int threshold)
    {
        var now = clock.Now;
        var counter = Get(key);
        if (counter is null || now - counter.FirstAt > Window) counter = new Counter { FirstAt = now };
        counter.Failures++;
        if (counter.Failures >= threshold) counter.LockedUntil = now + Window;
        cache.Set(key, counter, TimeSpan.FromMinutes(O.LockoutMinutes * 2));
    }

    private Counter? Get(string key) => cache.TryGetValue(key, out Counter? c) ? c : null;
    private static string AccountKey(string username) => $"login:acct:{username}";
    private static string SourceKey(string username, string? address) => $"login:src:{username}|{address ?? "?"}";
}
