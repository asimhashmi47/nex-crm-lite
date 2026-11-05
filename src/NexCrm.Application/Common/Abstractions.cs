using NexCrm.Application.Common.Querying;
using NexCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace NexCrm.Application.Common;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Company> Companies { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<Lead> Leads { get; }
    DbSet<Deal> Deals { get; }
    DbSet<CrmTask> Tasks { get; }
    DbSet<AuditEntry> AuditLog { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
}

public interface ICurrentUser
{
    int? UserId { get; }
    string Username { get; }
    bool IsManager { get; }
    string? IpAddress { get; }
}

public interface IClock
{
    DateTime Now { get; }
    DateTime Today => Now.Date;
}

public interface IExportService
{
    FileResultDto Export<T>(IReadOnlyList<T> rows, ExportRequest request, string defaultName);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);
}

public sealed class NotFoundException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
public sealed class ForbiddenException(string message) : Exception(message);
public sealed class BadRequestException(string message) : Exception(message);
public sealed class TooManyRequestsException(string message) : Exception(message);

/// <summary>Raised by the persistence layer when a unique index rejects a write, so services can explain the clash.</summary>
public sealed class UniqueConstraintException(Exception inner) : Exception("A unique constraint was violated.", inner);
