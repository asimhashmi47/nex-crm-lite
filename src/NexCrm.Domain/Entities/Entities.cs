using NexCrm.Domain.Common;

namespace NexCrm.Domain.Entities;

public sealed class User : BaseEntity
{
    public string Username { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Stamped into every token. Bumping it signs the user out everywhere.</summary>
    public int SessionVersion { get; private set; }

    public bool IsManager => Role is UserRole.Admin or UserRole.Manager;

    public void RevokeSessions() => SessionVersion++;
}

public sealed class Company : OwnedEntity
{
    private string _name = default!;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            NameKey = KeyFor(value);
        }
    }

    /// <summary>The name trimmed and lower-cased; a unique index on it keeps company names unique ignoring case.</summary>
    public string NameKey { get; private set; } = default!;

    public string Industry { get; set; } = default!;
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string City { get; set; } = default!;
    public string Country { get; set; } = default!;
    public int Employees { get; set; }
    public decimal AnnualRevenue { get; set; }
    public User Owner { get; set; } = default!;
    public List<Contact> Contacts { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];

    public static string KeyFor(string name) => name.Trim().ToLowerInvariant();

    public static Company Placeholder(string name, int ownerId) =>
        new() { Name = name, Industry = "Unassigned", City = "Unknown", Country = "Unknown", OwnerId = ownerId };
}

public sealed class Contact : OwnedEntity
{
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public ContactStage Stage { get; set; }
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
    public User Owner { get; set; } = default!;
    public DateTime? LastContactedAt { get; set; }

    public void Touch(DateTime at) => LastContactedAt = at;
}

public sealed class Lead : OwnedEntity
{
    public string Name { get; set; } = default!;
    public string CompanyName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? Phone { get; set; }
    public LeadSource Source { get; set; }
    public LeadStatus Status { get; set; }
    public int Score { get; set; }
    public decimal EstimatedValue { get; set; }
    public User Owner { get; set; } = default!;
    public int? ConvertedDealId { get; private set; }
    public DateTime? ConvertedAt { get; private set; }

    public bool IsConverted => Status == LeadStatus.Converted;

    public (string First, string Last) SplitName()
    {
        var parts = Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : "-");
    }

    public void EnsureCanConvert()
    {
        if (IsConverted) throw new DomainException("This lead has already been converted.");
        if (Status != LeadStatus.Qualified) throw new DomainException("Qualify the lead before converting it.");
    }

    public void MarkConverted(int dealId, DateTime at)
    {
        EnsureCanConvert();
        Status = LeadStatus.Converted;
        ConvertedDealId = dealId;
        ConvertedAt = at;
    }
}

public sealed class Deal : OwnedEntity
{
    public string Title { get; set; } = default!;
    public decimal Amount { get; set; }
    public DealStage Stage { get; private set; } = DealStage.Prospecting;
    public int Probability { get; private set; } = DefaultProbability(DealStage.Prospecting);
    public DateTime ExpectedCloseDate { get; set; }
    public DateTime? ClosedAt { get; private set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = default!;
    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public User Owner { get; set; } = default!;

    public bool IsClosed => Stage is DealStage.Won or DealStage.Lost;

    public static int DefaultProbability(DealStage stage) => stage switch
    {
        DealStage.Prospecting => 10,
        DealStage.Qualification => 25,
        DealStage.Proposal => 50,
        DealStage.Negotiation => 75,
        DealStage.Won => 100,
        _ => 0,
    };

    /// <summary>Closing stamps the close date and locks the probability; reopening clears both.</summary>
    public void MoveTo(DealStage stage, DateTime at)
    {
        if (stage == Stage) return;
        Stage = stage;
        Probability = DefaultProbability(stage);
        ClosedAt = IsClosed ? at : null;
    }

    /// <summary>Reps may adjust their own confidence on open deals; a closed deal is either 0 or 100.</summary>
    public void Forecast(int probability)
    {
        if (IsClosed) return;
        Probability = Math.Clamp(probability, 0, 100);
    }

    /// <summary>For the demo seeder only: back-dates a deal that closed in the past.</summary>
    public void Close(DealStage outcome, DateTime at)
    {
        if (outcome is not (DealStage.Won or DealStage.Lost)) throw new DomainException("A deal closes as Won or Lost.");
        MoveTo(outcome, at);
    }
}

public sealed class CrmTask : OwnedEntity
{
    public string Subject { get; set; } = default!;
    public TaskType Type { get; set; }
    public TaskPriority Priority { get; set; }
    public DateTime DueAt { get; set; }
    public bool IsDone { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? Notes { get; set; }
    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int? DealId { get; set; }
    public Deal? Deal { get; set; }
    public User Owner { get; set; } = default!;

    public void Complete(DateTime at)
    {
        IsDone = true;
        CompletedAt = at;
        Contact?.Touch(at);
    }

    public void Reopen()
    {
        IsDone = false;
        CompletedAt = null;
    }
}

/// <summary>Who did what, when. Written in the same transaction as the change it describes.</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = default!;
    public string Action { get; set; } = default!;
    public string EntityType { get; set; } = default!;
    public string? EntityId { get; set; }
    public string? Summary { get; set; }
    public string? IpAddress { get; set; }
}
