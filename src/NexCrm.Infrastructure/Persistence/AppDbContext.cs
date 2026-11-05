using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NexCrm.Application.Common;
using NexCrm.Domain.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<CrmTask> Tasks => Set<CrmTask>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite has no decimal type, so EF cannot sort or SUM a decimal column in SQL.
        // Storing money as REAL keeps grids, totals and the dashboard server-side.
        builder.Properties<decimal>().HaveConversion<double>();
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var type in model.Model.GetEntityTypes().Where(t => typeof(ISoftDelete).IsAssignableFrom(t.ClrType)))
            model.Entity(type.ClrType).HasQueryFilter(NotArchived(type.ClrType));
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => Database.BeginTransactionAsync(ct);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimes();
        try { return base.SaveChanges(acceptAllChangesOnSuccess); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { DetachRejected(ex); throw new UniqueConstraintException(ex); }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimes();
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { DetachRejected(ex); throw new UniqueConstraintException(ex); }
    }

    // SQLITE_CONSTRAINT_UNIQUE: a unique index refused the row.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 };

    // Leave the context usable for the caller's next step (the error response, an audit row).
    private static void DetachRejected(DbUpdateException ex)
    {
        foreach (var entry in ex.Entries) entry.State = EntityState.Detached;
    }

    // Both save paths end up here, so no caller can write a row without its timestamps.
    private void StampTimes()
    {
        var now = clock.Now;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            // The seeder back-dates history, so only stamp what the caller left empty.
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }

    private static LambdaExpression NotArchived(Type type)
    {
        var e = Expression.Parameter(type, "e");
        var isDeleted = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(bool)], e, Expression.Constant(nameof(ISoftDelete.IsDeleted)));
        return Expression.Lambda(Expression.Not(isDeleted), e);
    }
}
