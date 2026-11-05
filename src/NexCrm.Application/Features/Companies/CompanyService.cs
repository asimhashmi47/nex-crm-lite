using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Features.Companies;

public sealed record CompanyDto : IHasId
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string Industry { get; init; } = default!;
    public string? Website { get; init; }
    public string? Phone { get; init; }
    public string City { get; init; } = default!;
    public string Country { get; init; } = default!;
    public int Employees { get; init; }
    public decimal AnnualRevenue { get; init; }
    public int OwnerId { get; init; }
    public string Owner { get; init; } = default!;
    public int Contacts { get; init; }
    public int OpenDeals { get; init; }
    public decimal WonRevenue { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record SaveCompanyRequest(
    string Name, string Industry, string? Website, string? Phone,
    string City, string Country, int Employees, decimal AnnualRevenue, int OwnerId);

public sealed class SaveCompanyValidator : AbstractValidator<SaveCompanyRequest>
{
    public SaveCompanyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Industry).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Website).MaximumLength(200);
        RuleFor(x => x.Phone).PhoneNumber();
        RuleFor(x => x.City).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Employees).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.AnnualRevenue).InclusiveBetween(0, 1_000_000_000_000m);
        RuleFor(x => x.OwnerId).Owner();
    }
}

public sealed class CompanyService(IAppDbContext db, ICurrentUser user, IClock clock)
    : CrudService<Company, CompanyDto, SaveCompanyRequest>(db, user, clock)
{
    protected override DbSet<Company> Set => Db.Companies;
    protected override string Noun => "Company";

    public override IQueryable<CompanyDto> Query() => Db.Companies.AsNoTracking().Select(c => new CompanyDto
    {
        Id = c.Id,
        Name = c.Name,
        Industry = c.Industry,
        Website = c.Website,
        Phone = c.Phone,
        City = c.City,
        Country = c.Country,
        Employees = c.Employees,
        AnnualRevenue = c.AnnualRevenue,
        OwnerId = c.OwnerId,
        Owner = c.Owner.FullName,
        Contacts = c.Contacts.Count(x => !x.IsDeleted),
        OpenDeals = c.Deals.Count(d => !d.IsDeleted && d.Stage != DealStage.Won && d.Stage != DealStage.Lost),
        WonRevenue = c.Deals.Where(d => !d.IsDeleted && d.Stage == DealStage.Won).Sum(d => d.Amount),
        CreatedAt = c.CreatedAt,
    });

    protected override int OwnerOf(SaveCompanyRequest r) => r.OwnerId;

    protected override async Task ValidateAsync(Company company, SaveCompanyRequest r, CancellationToken ct)
    {
        var key = Company.KeyFor(r.Name);
        if (await Db.Companies.AnyAsync(c => c.NameKey == key && c.Id != company.Id, ct))
            throw new ConflictException(DuplicateMessage(r));
    }

    protected override string DuplicateMessage(SaveCompanyRequest r) => $"A company called {r.Name.Trim()} already exists.";

    protected override void Apply(Company c, SaveCompanyRequest r)
    {
        c.Name = r.Name.Trim();
        c.Industry = r.Industry.Trim();
        c.Website = r.Website.Clean();
        c.Phone = r.Phone.Clean();
        c.City = r.City.Trim();
        c.Country = r.Country.Trim();
        c.Employees = r.Employees;
        c.AnnualRevenue = r.AnnualRevenue;
    }

    protected override async Task BeforeArchiveAsync(Company company, CancellationToken ct)
    {
        var hasOpenDeals = await Db.Deals.AnyAsync(d => d.CompanyId == company.Id && d.Stage != DealStage.Won && d.Stage != DealStage.Lost, ct);
        if (hasOpenDeals) throw new ConflictException($"{company.Name} still has open deals. Close or reassign them first.");
    }
}
