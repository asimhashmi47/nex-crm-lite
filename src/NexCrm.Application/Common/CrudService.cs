using Microsoft.EntityFrameworkCore;
using NexCrm.Domain.Common;

namespace NexCrm.Application.Common;

/// <summary>Every row a grid shows carries its key.</summary>
public interface IHasId
{
    int Id { get; }
}

/// <summary>What a grid controller needs from a feature, whatever the entity behind it.</summary>
public interface ICrudService<TDto, in TRequest>
{
    IQueryable<TDto> Query();
    Task<TDto> GetAsync(int id, CancellationToken ct);
    Task<TDto> CreateAsync(TRequest request, CancellationToken ct);
    Task<TDto> UpdateAsync(int id, TRequest request, CancellationToken ct);
    Task ArchiveAsync(int id, CancellationToken ct);
}

/// <summary>
/// Template for the owned CRM records. The base class runs the workflow (find, check, map, save, re-read);
/// a feature supplies the projection and the mapping, and overrides the hooks it cares about.
/// </summary>
public abstract class CrudService<TEntity, TDto, TRequest>(IAppDbContext db, ICurrentUser user, IClock clock)
    : ICrudService<TDto, TRequest>
    where TEntity : OwnedEntity, new()
    where TDto : class, IHasId
{
    protected IAppDbContext Db { get; } = db;
    protected IClock Clock { get; } = clock;
    protected AuditTrail Audit { get; } = new(db, user, clock);

    protected abstract DbSet<TEntity> Set { get; }
    protected abstract string Noun { get; }

    /// <summary>Server-side projection. The grid filters and sorts on these properties only.</summary>
    public abstract IQueryable<TDto> Query();

    protected abstract void Apply(TEntity entity, TRequest request);
    protected abstract int OwnerOf(TRequest request);

    protected virtual Task ValidateAsync(TEntity entity, TRequest request, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task BeforeArchiveAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;

    /// <summary>What to say when a unique index rejects the save (two people creating the same record at once).</summary>
    protected virtual string DuplicateMessage(TRequest request) => $"This {Noun.ToLowerInvariant()} already exists.";

    public async Task<TDto> GetAsync(int id, CancellationToken ct) =>
        await Query().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();

    public Task<TDto> CreateAsync(TRequest request, CancellationToken ct) => SaveAsync(new TEntity(), request, isNew: true, ct);

    public async Task<TDto> UpdateAsync(int id, TRequest request, CancellationToken ct) =>
        await SaveAsync(await FindAsync(id, ct), request, isNew: false, ct);

    public async Task ArchiveAsync(int id, CancellationToken ct)
    {
        if (!user.IsManager) throw new ForbiddenException("Only admins and managers can delete records.");
        var entity = await FindAsync(id, ct);
        await BeforeArchiveAsync(entity, ct);
        entity.Archive(Clock.Now);
        Audit.Record($"{Noun}.Archived", Noun, entity.Id);
        await Db.SaveChangesAsync(ct);
    }

    protected async Task<TEntity> FindAsync(int id, CancellationToken ct) =>
        await Set.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw NotFound();

    protected NotFoundException NotFound() => new($"{Noun} not found.");

    private async Task<TDto> SaveAsync(TEntity entity, TRequest request, bool isNew, CancellationToken ct)
    {
        var ownerId = OwnerOf(request);
        if (!await Db.Users.AnyAsync(u => u.Id == ownerId && u.IsActive, ct))
            throw new BadRequestException("Choose an active owner.");

        var previousOwner = entity.OwnerId;
        var reassigning = !isNew && previousOwner != ownerId;
        // Reps work the whole team's records, but only a manager may take a record away from a colleague.
        if (reassigning && previousOwner != user.UserId && !user.IsManager)
            throw new ForbiddenException("Only managers can reassign a colleague's record.");

        await ValidateAsync(entity, request, ct);
        Apply(entity, request);
        entity.OwnerId = ownerId;
        if (isNew) Set.Add(entity);

        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintException)
        {
            throw new ConflictException(DuplicateMessage(request));
        }

        Audit.Record(isNew ? $"{Noun}.Created" : $"{Noun}.Updated", Noun, entity.Id, reassigning ? $"Owner {previousOwner} → {ownerId}" : null);
        await Db.SaveChangesAsync(ct);
        return await GetAsync(entity.Id, ct);
    }
}
