using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Features.Contacts;

public sealed record ContactDto : IHasId
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string FirstName { get; init; } = default!;
    public string LastName { get; init; } = default!;
    public string Email { get; init; } = default!;
    public string? Phone { get; init; }
    public string? JobTitle { get; init; }
    public ContactStage Stage { get; init; }
    public int? CompanyId { get; init; }
    public string? Company { get; init; }
    public int OwnerId { get; init; }
    public string Owner { get; init; } = default!;
    public int OpenTasks { get; init; }
    public DateTime? LastContactedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record SaveContactRequest(
    string FirstName, string LastName, string Email, string? Phone,
    string? JobTitle, ContactStage Stage, int? CompanyId, int OwnerId);

public sealed class SaveContactValidator : AbstractValidator<SaveContactRequest>
{
    public SaveContactValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(160).EmailAddress();
        RuleFor(x => x.Phone).PhoneNumber();
        RuleFor(x => x.JobTitle).MaximumLength(100);
        RuleFor(x => x.Stage).IsInEnum();
        RuleFor(x => x.OwnerId).Owner();
    }
}

public sealed class ContactService(IAppDbContext db, ICurrentUser user, IClock clock)
    : CrudService<Contact, ContactDto, SaveContactRequest>(db, user, clock)
{
    protected override DbSet<Contact> Set => Db.Contacts;
    protected override string Noun => "Contact";

    public override IQueryable<ContactDto> Query() => Db.Contacts.AsNoTracking().Select(c => new ContactDto
    {
        Id = c.Id,
        Name = c.FirstName + " " + c.LastName,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email,
        Phone = c.Phone,
        JobTitle = c.JobTitle,
        Stage = c.Stage,
        CompanyId = c.CompanyId,
        Company = c.Company != null ? c.Company.Name : null,
        OwnerId = c.OwnerId,
        Owner = c.Owner.FullName,
        OpenTasks = Db.Tasks.Count(t => t.ContactId == c.Id && !t.IsDone),
        LastContactedAt = c.LastContactedAt,
        CreatedAt = c.CreatedAt,
    });

    protected override int OwnerOf(SaveContactRequest r) => r.OwnerId;

    protected override async Task ValidateAsync(Contact contact, SaveContactRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (await Db.Contacts.AnyAsync(c => c.Email == email && c.Id != contact.Id, ct))
            throw new ConflictException(DuplicateMessage(r));

        if (r.CompanyId is { } companyId && !await Db.Companies.AnyAsync(c => c.Id == companyId, ct))
            throw new BadRequestException("Choose an existing company.");
    }

    protected override string DuplicateMessage(SaveContactRequest r) => $"Another contact already uses {r.Email.Trim().ToLowerInvariant()}.";

    protected override void Apply(Contact c, SaveContactRequest r)
    {
        c.FirstName = r.FirstName.Trim();
        c.LastName = r.LastName.Trim();
        c.Email = r.Email.Trim().ToLowerInvariant();
        c.Phone = r.Phone.Clean();
        c.JobTitle = r.JobTitle.Clean();
        c.Stage = r.Stage;
        c.CompanyId = r.CompanyId;
    }
}
