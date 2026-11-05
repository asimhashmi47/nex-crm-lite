using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Features.Leads;

public sealed record LeadDto : IHasId
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string CompanyName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? Phone { get; init; }
    public LeadSource Source { get; init; }
    public LeadStatus Status { get; init; }
    public int Score { get; init; }
    public decimal EstimatedValue { get; init; }
    public int OwnerId { get; init; }
    public string Owner { get; init; } = default!;
    public int? ConvertedDealId { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record SaveLeadRequest(
    string Name, string CompanyName, string Email, string? Phone,
    LeadSource Source, LeadStatus Status, int Score, decimal EstimatedValue, int OwnerId);

public sealed record ConvertLeadResult(int DealId, int CompanyId, int ContactId, string DealTitle);

public sealed class SaveLeadValidator : AbstractValidator<SaveLeadRequest>
{
    public SaveLeadValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(160).EmailAddress();
        RuleFor(x => x.Phone).PhoneNumber();
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.Status).IsInEnum().NotEqual(LeadStatus.Converted).WithMessage("Use Convert to turn a lead into a deal.");
        RuleFor(x => x.Score).InclusiveBetween(0, 100);
        RuleFor(x => x.EstimatedValue).InclusiveBetween(0, 1_000_000_000m);
        RuleFor(x => x.OwnerId).Owner();
    }
}

public sealed class LeadService(IAppDbContext db, ICurrentUser user, IClock clock)
    : CrudService<Lead, LeadDto, SaveLeadRequest>(db, user, clock)
{
    private const int DaysToClose = 45;

    protected override DbSet<Lead> Set => Db.Leads;
    protected override string Noun => "Lead";

    public override IQueryable<LeadDto> Query() => Db.Leads.AsNoTracking().Select(l => new LeadDto
    {
        Id = l.Id,
        Name = l.Name,
        CompanyName = l.CompanyName,
        Email = l.Email,
        Phone = l.Phone,
        Source = l.Source,
        Status = l.Status,
        Score = l.Score,
        EstimatedValue = l.EstimatedValue,
        OwnerId = l.OwnerId,
        Owner = l.Owner.FullName,
        ConvertedDealId = l.ConvertedDealId,
        CreatedAt = l.CreatedAt,
    });

    protected override int OwnerOf(SaveLeadRequest r) => r.OwnerId;

    protected override Task ValidateAsync(Lead lead, SaveLeadRequest r, CancellationToken ct) =>
        lead.IsConverted ? throw new ConflictException("Converted leads are read-only. Edit the deal instead.") : Task.CompletedTask;

    protected override void Apply(Lead l, SaveLeadRequest r)
    {
        l.Name = r.Name.Trim();
        l.CompanyName = r.CompanyName.Trim();
        l.Email = r.Email.Trim().ToLowerInvariant();
        l.Phone = r.Phone.Clean();
        l.Source = r.Source;
        l.Status = r.Status;
        l.Score = r.Score;
        l.EstimatedValue = r.EstimatedValue;
    }

    /// <summary>
    /// Lead to pipeline in one step. An existing company (by name) and contact (by email) are reused,
    /// so converting twice from the same account never duplicates the address book.
    /// </summary>
    public async Task<ConvertLeadResult> ConvertAsync(int id, CancellationToken ct)
    {
        // One transaction for the read and both writes, so a double click can never open two deals.
        await using var tx = await Db.BeginTransactionAsync(ct);
        var lead = await FindAsync(id, ct);
        lead.EnsureCanConvert();

        var companyKey = Company.KeyFor(lead.CompanyName);
        var company = await Db.Companies.FirstOrDefaultAsync(c => c.NameKey == companyKey, ct)
            ?? Db.Companies.Add(Company.Placeholder(lead.CompanyName, lead.OwnerId)).Entity;

        var contact = await Db.Contacts.FirstOrDefaultAsync(c => c.Email == lead.Email, ct)
            ?? Db.Contacts.Add(NewContactFrom(lead, company)).Entity;
        contact.Stage = ContactStage.Opportunity;

        var deal = new Deal
        {
            Title = $"{lead.CompanyName} - new business",
            Amount = lead.EstimatedValue,
            ExpectedCloseDate = Clock.Today.AddDays(DaysToClose),
            Company = company,
            Contact = contact,
            OwnerId = lead.OwnerId,
        };
        Db.Deals.Add(deal);
        await Db.SaveChangesAsync(ct);

        lead.MarkConverted(deal.Id, Clock.Now);
        Audit.Record("Lead.Converted", Noun, lead.Id, $"Deal {deal.Id}");
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ConvertLeadResult(deal.Id, company.Id, contact.Id, deal.Title);
    }

    private static Contact NewContactFrom(Lead lead, Company company)
    {
        var (first, last) = lead.SplitName();
        return new Contact
        {
            FirstName = first,
            LastName = last,
            Email = lead.Email,
            Phone = lead.Phone,
            Company = company,
            OwnerId = lead.OwnerId,
        };
    }
}
