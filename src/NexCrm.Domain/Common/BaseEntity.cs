namespace NexCrm.Domain.Common;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>CRM records are archived, not erased. A global query filter keeps archived rows out of every query.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; }
    DateTime? DeletedAt { get; }
    void Archive(DateTime at);
}

/// <summary>Every record a sales rep owns: it has an owner and it can be archived.</summary>
public abstract class OwnedEntity : BaseEntity, ISoftDelete
{
    public int OwnerId { get; set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    public void Archive(DateTime at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

public sealed class DomainException(string message) : Exception(message);
