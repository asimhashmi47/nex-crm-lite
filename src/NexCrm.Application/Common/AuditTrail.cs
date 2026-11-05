using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common.Querying;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Common;

/// <summary>Records sensitive actions. <see cref="Record"/> joins the caller's unit of work; <see cref="WriteAsync"/> saves at once.</summary>
public sealed class AuditTrail(IAppDbContext db, ICurrentUser user, IClock clock)
{
    private const int SummaryLimit = 500;

    public void Record(string action, string entityType, object? entityId = null, string? summary = null) =>
        db.AuditLog.Add(Entry(action, entityType, entityId, summary, user.UserId, user.Username));

    /// <summary>For events with nothing else to save, such as sign-ins and exports. A known actor can be passed in.</summary>
    public async Task WriteAsync(string action, string entityType, object? entityId = null, string? summary = null,
        int? userId = null, string? username = null, CancellationToken ct = default)
    {
        db.AuditLog.Add(Entry(action, entityType, entityId, summary, userId ?? user.UserId, username ?? user.Username));
        await db.SaveChangesAsync(ct);
    }

    private AuditEntry Entry(string action, string entityType, object? entityId, string? summary, int? userId, string username) => new()
    {
        At = clock.Now,
        UserId = userId,
        Username = string.IsNullOrEmpty(username) || username == "anonymous" ? "system" : username,
        Action = action,
        EntityType = entityType,
        EntityId = entityId?.ToString(),
        Summary = summary is { Length: > SummaryLimit } ? summary[..SummaryLimit] : summary,
        IpAddress = user.IpAddress,
    };
}

public sealed record AuditDto
{
    public long Id { get; init; }
    public DateTime At { get; init; }
    public string Username { get; init; } = default!;
    public string Action { get; init; } = default!;
    public string EntityType { get; init; } = default!;
    public string? EntityId { get; init; }
    public string? Summary { get; init; }
    public string? IpAddress { get; init; }
}

public sealed class AuditQueryService(IAppDbContext db)
{
    public IQueryable<AuditDto> Query() => db.AuditLog.AsNoTracking().Select(a => new AuditDto
    {
        Id = a.Id,
        At = a.At,
        Username = a.Username,
        Action = a.Action,
        EntityType = a.EntityType,
        EntityId = a.EntityId,
        Summary = a.Summary,
        IpAddress = a.IpAddress,
    });

    public Task<PagedResult<AuditDto>> PageAsync(TableQuery query, CancellationToken ct) => Query().ToPagedAsync(query, nameof(AuditDto.At), true, ct);
}
