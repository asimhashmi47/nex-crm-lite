using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Features.Tasks;

public sealed record TaskDto : IHasId
{
    public int Id { get; init; }
    public string Subject { get; init; } = default!;
    public TaskType Type { get; init; }
    public TaskPriority Priority { get; init; }
    public DateTime DueAt { get; init; }
    public bool IsDone { get; init; }
    public bool Overdue { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Notes { get; init; }
    public int? ContactId { get; init; }
    public string? Contact { get; init; }
    public int? DealId { get; init; }
    public string? Deal { get; init; }
    public int OwnerId { get; init; }
    public string Owner { get; init; } = default!;
}

public sealed record SaveTaskRequest(
    string Subject, TaskType Type, TaskPriority Priority, DateTime DueAt,
    string? Notes, int? ContactId, int? DealId, int OwnerId);

public sealed record CompleteTaskRequest(bool Done);

public sealed class SaveTaskValidator : AbstractValidator<SaveTaskRequest>
{
    public SaveTaskValidator(IClock clock)
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.DueAt).NotEmpty().WithMessage("Choose when the task is due.")
            .InclusiveBetween(clock.Today.AddYears(-DateRules.YearsBack), clock.Today.AddYears(DateRules.YearsAhead))
            .WithMessage(DateRules.Message);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.OwnerId).Owner();
    }
}

public sealed class TaskService(IAppDbContext db, ICurrentUser user, IClock clock)
    : CrudService<CrmTask, TaskDto, SaveTaskRequest>(db, user, clock)
{
    protected override DbSet<CrmTask> Set => Db.Tasks;
    protected override string Noun => "Task";

    public override IQueryable<TaskDto> Query()
    {
        var now = Clock.Now;
        return Db.Tasks.AsNoTracking().Select(t => new TaskDto
        {
            Id = t.Id,
            Subject = t.Subject,
            Type = t.Type,
            Priority = t.Priority,
            DueAt = t.DueAt,
            IsDone = t.IsDone,
            Overdue = !t.IsDone && t.DueAt < now,
            CompletedAt = t.CompletedAt,
            Notes = t.Notes,
            ContactId = t.ContactId,
            Contact = t.Contact != null ? t.Contact.FirstName + " " + t.Contact.LastName : null,
            DealId = t.DealId,
            Deal = t.Deal != null ? t.Deal.Title : null,
            OwnerId = t.OwnerId,
            Owner = t.Owner.FullName,
        });
    }

    protected override int OwnerOf(SaveTaskRequest r) => r.OwnerId;

    protected override async Task ValidateAsync(CrmTask task, SaveTaskRequest r, CancellationToken ct)
    {
        if (r.ContactId is { } contactId && !await Db.Contacts.AnyAsync(c => c.Id == contactId, ct))
            throw new BadRequestException("Choose an existing contact.");

        if (r.DealId is { } dealId && !await Db.Deals.AnyAsync(d => d.Id == dealId, ct))
            throw new BadRequestException("Choose an existing deal.");
    }

    protected override void Apply(CrmTask t, SaveTaskRequest r)
    {
        t.Subject = r.Subject.Trim();
        t.Type = r.Type;
        t.Priority = r.Priority;
        t.DueAt = r.DueAt;
        t.Notes = r.Notes.Clean();
        t.ContactId = r.ContactId;
        t.DealId = r.DealId;
    }

    public async Task<TaskDto> SetDoneAsync(int id, bool done, CancellationToken ct)
    {
        var task = await Db.Tasks.Include(t => t.Contact).FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound();
        if (done) task.Complete(Clock.Now);
        else task.Reopen();

        await Db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }
}
