using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;

namespace NexCrm.Application.Features.Lookups;

public sealed record Option(int Id, string Label, string? Hint = null);

/// <summary>Small id/label lists for the pickers in the drawers. Matching ignores case, as people type.</summary>
public sealed class LookupService(IAppDbContext db)
{
    private const int Limit = 25;

    public Task<List<Option>> OwnersAsync(CancellationToken ct) =>
        db.Users.Where(u => u.IsActive).OrderBy(u => u.FullName)
            .Select(u => new Option(u.Id, u.FullName, u.Role.ToString()))
            .ToListAsync(ct);

    public Task<List<Option>> CompaniesAsync(string? term, CancellationToken ct)
    {
        var t = Normalize(term);
        return db.Companies.Where(c => t == "" || c.Name.ToLower().Contains(t))
            .OrderBy(c => c.Name).Take(Limit)
            .Select(c => new Option(c.Id, c.Name, c.City))
            .ToListAsync(ct);
    }

    public Task<List<Option>> ContactsAsync(string? term, int? companyId, CancellationToken ct)
    {
        var t = Normalize(term);
        return db.Contacts
            .Where(c => companyId == null || c.CompanyId == companyId)
            .Where(c => t == "" || (c.FirstName + " " + c.LastName).ToLower().Contains(t) || c.Email.Contains(t))
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName).Take(Limit)
            .Select(c => new Option(c.Id, c.FirstName + " " + c.LastName, c.Email))
            .ToListAsync(ct);
    }

    public Task<List<Option>> DealsAsync(string? term, CancellationToken ct)
    {
        var t = Normalize(term);
        return db.Deals.Where(d => t == "" || d.Title.ToLower().Contains(t))
            .OrderByDescending(d => d.CreatedAt).Take(Limit)
            .Select(d => new Option(d.Id, d.Title, d.Company.Name))
            .ToListAsync(ct);
    }

    // Emails are stored lower-case already, so lowering the term is enough for them.
    private static string Normalize(string? term) => term?.Trim().ToLowerInvariant() ?? "";
}
