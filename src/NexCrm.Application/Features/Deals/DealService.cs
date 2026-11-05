using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Features.Deals;

public sealed record DealDto : IHasId
{
    public int Id { get; init; }
    public string Title { get; init; } = default!;
    public decimal Amount { get; init; }
    public DealStage Stage { get; init; }
    public int Probability { get; init; }
    public decimal Weighted { get; init; }
    public DateTime ExpectedCloseDate { get; init; }
    public DateTime? ClosedAt { get; init; }
    public int CompanyId { get; init; }
    public string Company { get; init; } = default!;
    public int? ContactId { get; init; }
    public string? Contact { get; init; }
    public int OwnerId { get; init; }
    public string Owner { get; init; } = default!;
    public DateTime CreatedAt { get; init; }
}

public sealed record BoardCardDto(int Id, string Title, decimal Amount, int Probability, DateTime ExpectedCloseDate, string Company, string Owner, bool Overdue);
public sealed record BoardColumnDto(DealStage Stage, int Count, decimal Total, IReadOnlyList<BoardCardDto> Cards);

public sealed record SaveDealRequest(
    string Title, decimal Amount, DealStage Stage, int Probability,
    DateTime ExpectedCloseDate, int CompanyId, int? ContactId, int OwnerId);

public sealed record MoveDealRequest(DealStage? Stage);

public sealed class SaveDealValidator : AbstractValidator<SaveDealRequest>
{
    public SaveDealValidator(IClock clock)
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000_000m);
        RuleFor(x => x.Stage).IsInEnum();
        RuleFor(x => x.Probability).InclusiveBetween(0, 100);
        RuleFor(x => x.ExpectedCloseDate).NotEmpty().WithMessage("Choose an expected close date.")
            .InclusiveBetween(clock.Today.AddYears(-DateRules.YearsBack), clock.Today.AddYears(DateRules.YearsAhead))
            .WithMessage(DateRules.Message);
        RuleFor(x => x.CompanyId).GreaterThan(0).WithMessage("Choose a company.");
        RuleFor(x => x.OwnerId).Owner();
    }
}

public sealed class MoveDealValidator : AbstractValidator<MoveDealRequest>
{
    public MoveDealValidator() => RuleFor(x => x.Stage).NotNull().WithMessage("Choose a stage.").IsInEnum();
}

public sealed class DealService(IAppDbContext db, ICurrentUser user, IClock clock)
    : CrudService<Deal, DealDto, SaveDealRequest>(db, user, clock)
{
    public const int CardsPerColumn = 40;
    private const int ClosedWindowDays = 90;

    protected override DbSet<Deal> Set => Db.Deals;
    protected override string Noun => "Deal";

    public override IQueryable<DealDto> Query() => Db.Deals.AsNoTracking().Select(d => new DealDto
    {
        Id = d.Id,
        Title = d.Title,
        Amount = d.Amount,
        Stage = d.Stage,
        Probability = d.Probability,
        Weighted = d.Amount * d.Probability / 100m,
        ExpectedCloseDate = d.ExpectedCloseDate,
        ClosedAt = d.ClosedAt,
        CompanyId = d.CompanyId,
        Company = d.Company.Name,
        ContactId = d.ContactId,
        Contact = d.Contact != null ? d.Contact.FirstName + " " + d.Contact.LastName : null,
        OwnerId = d.OwnerId,
        Owner = d.Owner.FullName,
        CreatedAt = d.CreatedAt,
    });

    protected override int OwnerOf(SaveDealRequest r) => r.OwnerId;

    protected override async Task ValidateAsync(Deal deal, SaveDealRequest r, CancellationToken ct)
    {
        if (!await Db.Companies.AnyAsync(c => c.Id == r.CompanyId, ct))
            throw new BadRequestException("Choose an existing company.");

        if (r.ContactId is { } contactId && !await Db.Contacts.AnyAsync(c => c.Id == contactId, ct))
            throw new BadRequestException("Choose an existing contact.");
    }

    protected override void Apply(Deal d, SaveDealRequest r)
    {
        d.Title = r.Title.Trim();
        d.Amount = r.Amount;
        d.ExpectedCloseDate = r.ExpectedCloseDate.Date;
        d.CompanyId = r.CompanyId;
        d.ContactId = r.ContactId;
        d.MoveTo(r.Stage, Clock.Now);
        d.Forecast(r.Probability);
    }

    /// <summary>
    /// The Kanban board. Each column shows its most valuable cards, while the count and total cover every deal in it.
    /// Won and Lost only look back 90 days so the board stays about the work in hand.
    /// </summary>
    public async Task<IReadOnlyList<BoardColumnDto>> BoardAsync(int? ownerId, CancellationToken ct)
    {
        var today = Clock.Today;
        var since = today.AddDays(-ClosedWindowDays);

        var onBoard = Db.Deals.AsNoTracking()
            .Where(d => ownerId == null || d.OwnerId == ownerId)
            .Where(d => (d.Stage != DealStage.Won && d.Stage != DealStage.Lost) || d.ClosedAt >= since);

        var totals = await onBoard
            .GroupBy(d => d.Stage)
            .Select(g => new { Stage = g.Key, Count = g.Count(), Total = g.Sum(d => d.Amount) })
            .ToDictionaryAsync(x => x.Stage, ct);

        var columns = new List<BoardColumnDto>();
        foreach (var stage in Enum.GetValues<DealStage>())
        {
            var cards = await onBoard
                .Where(d => d.Stage == stage)
                .OrderByDescending(d => d.Amount)
                .Take(CardsPerColumn)
                .Select(d => new BoardCardDto(
                    d.Id, d.Title, d.Amount, d.Probability, d.ExpectedCloseDate, d.Company.Name, d.Owner.FullName,
                    d.Stage != DealStage.Won && d.Stage != DealStage.Lost && d.ExpectedCloseDate < today))
                .ToListAsync(ct);

            var summary = totals.GetValueOrDefault(stage);
            columns.Add(new BoardColumnDto(stage, summary?.Count ?? 0, summary?.Total ?? 0, cards));
        }
        return columns;
    }

    public async Task<DealDto> MoveAsync(int id, DealStage stage, CancellationToken ct)
    {
        var deal = await FindAsync(id, ct);
        var from = deal.Stage;
        deal.MoveTo(stage, Clock.Now);
        if (from != stage) Audit.Record("Deal.StageChanged", Noun, deal.Id, $"{from} → {stage}");
        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }
}
