using System.Diagnostics;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;
using NexCrm.Infrastructure.Persistence;

namespace NexCrm.Infrastructure.Seeding;

public sealed record SeedResult(int Users, int Companies, int Contacts, int Leads, int Deals, int Tasks, TimeSpan Elapsed);

/// <summary>
/// A year of believable sales history for the demo. The same seed always yields the same data,
/// so screenshots and walkthroughs stay reproducible.
/// </summary>
public sealed class DemoDataSeeder(AppDbContext db, IPasswordHasher<User> hasher, IClock clock, ILogger<DemoDataSeeder> log)
{
    private const int Seed = 2026;
    private const int Months = 12;

    private static readonly string[] Industries =
        ["Software", "Logistics", "Healthcare", "Retail", "Manufacturing", "Real estate", "Education", "Finance", "Hospitality", "Energy"];

    private static readonly (string City, string Country)[] Cities =
    [
        ("Karachi", "Pakistan"), ("Lahore", "Pakistan"), ("Islamabad", "Pakistan"), ("Dubai", "UAE"), ("Riyadh", "Saudi Arabia"),
        ("London", "United Kingdom"), ("Manchester", "United Kingdom"), ("Toronto", "Canada"), ("Austin", "United States"), ("Berlin", "Germany"),
    ];

    private static readonly string[] DealNames =
        ["annual licence", "platform rollout", "renewal", "pilot", "expansion", "support plan", "onboarding package", "data migration", "team seats", "integration"];

    private static readonly (string Username, string FullName, UserRole Role)[] Team =
    [
        ("admin", "Asim Hashmi", UserRole.Admin),
        ("manager", "Hira Qureshi", UserRole.Manager),
        ("sara", "Sara Abbasi", UserRole.SalesRep),
        ("bilal", "Bilal Rana", UserRole.SalesRep),
        ("omer", "Omer Gill", UserRole.SalesRep),
        ("ayesha", "Ayesha Malik", UserRole.SalesRep),
    ];

    private readonly Random _rng = new(Seed);
    private DateTime _now;

    public Task<bool> HasDataAsync(CancellationToken ct = default) => db.Users.AnyAsync(ct);

    /// <param name="scale">Multiplies every record count (1 = the demo year, 20 = a 1,000-person team's book).</param>
    /// <param name="loadUsers">Extra sales reps named load0001… for load tests, all with <paramref name="loadPassword"/>.</param>
    public async Task<SeedResult> SeedAsync(bool reset, int scale = 1, int loadUsers = 0, string? loadPassword = null, CancellationToken ct = default)
    {
        var clockTime = Stopwatch.StartNew();
        _now = clock.Now;
        Randomizer.Seed = new Random(Seed);

        if (reset) await db.Database.EnsureDeletedAsync(ct);
        await db.Database.MigrateAsync(ct);
        if (await HasDataAsync(ct))
        {
            log.LogInformation("Demo data already present, nothing to seed");
            return new SeedResult(0, 0, 0, 0, 0, 0, clockTime.Elapsed);
        }

        var users = SeedUsers();
        var companies = SeedCompanies(users, 120 * scale);
        var contacts = SeedContacts(users, companies, 420 * scale);
        var deals = SeedDeals(users, companies, contacts, 560 * scale);
        var leads = SeedLeads(users, 560 * scale);
        var tasks = SeedTasks(users, contacts, deals, 640 * scale);
        var testers = SeedLoadUsers(loadUsers, loadPassword);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.AddRange(users);
        db.AddRange(testers);
        db.AddRange(companies);
        db.AddRange(contacts);
        db.AddRange(deals);
        db.AddRange(leads);
        db.AddRange(tasks);
        await db.SaveChangesAsync(ct);

        LinkConvertedLeads(leads, deals);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SeedResult(users.Count + testers.Count, companies.Count, contacts.Count, leads.Count, deals.Count, tasks.Count, clockTime.Elapsed);
    }

    private List<User> SeedUsers() => Team.Select(t =>
    {
        var user = new User
        {
            Username = t.Username,
            FullName = t.FullName,
            Email = $"{t.Username}@nexcrm.demo",
            Role = t.Role,
            CreatedAt = _now.AddMonths(-Months - 1),
        };
        // Demo accounts sign in with the username as the password; the sign-in page says so.
        user.PasswordHash = hasher.HashPassword(user, t.Username);
        return user;
    }).ToList();

    // One hash for all of them: hashing a thousand passwords separately would take minutes and test nothing.
    private List<User> SeedLoadUsers(int count, string? password)
    {
        if (count <= 0) return [];
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Load users need a password.", nameof(password));
        var hash = hasher.HashPassword(new User(), password);
        return Enumerable.Range(1, count).Select(i => new User
        {
            Username = $"load{i:0000}",
            FullName = $"Load Tester {i:0000}",
            Email = $"load{i:0000}@nexcrm.test",
            Role = UserRole.SalesRep,
            PasswordHash = hash,
            CreatedAt = _now,
        }).ToList();
    }

    private List<Company> SeedCompanies(List<User> users, int count)
    {
        var faker = new Faker();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var companies = new List<Company>();

        while (companies.Count < count)
        {
            var name = faker.Company.CompanyName().Replace(",", "");
            if (!names.Add(name)) continue;

            var (city, country) = Pick(Cities);
            var employees = Weighted([(10, 30), (60, 40), (250, 20), (1200, 10)]) + _rng.Next(0, 40);
            companies.Add(new Company
            {
                Name = name,
                Industry = Pick(Industries),
                Website = $"https://{Slug(name)}.com",
                Phone = faker.Phone.PhoneNumber("+1 ###-###-####"),
                City = city,
                Country = country,
                Employees = employees,
                AnnualRevenue = RoundTo(employees * (decimal)(_rng.Next(40, 180) * 1000), 1000),
                Owner = Pick(users),
                CreatedAt = RandomPast(Months),
            });
        }
        return companies;
    }

    private List<Contact> SeedContacts(List<User> users, List<Company> companies, int count)
    {
        var faker = new Faker();
        var emails = new HashSet<string>();
        var titles = new[] { "CEO", "CTO", "Head of Sales", "Operations Manager", "Procurement Lead", "Finance Director", "IT Manager", "Marketing Lead", "Founder", "Product Manager" };
        var contacts = new List<Contact>();

        while (contacts.Count < count)
        {
            var company = Pick(companies);
            var first = faker.Name.FirstName();
            var last = faker.Name.LastName();
            var email = $"{first}.{last}@{Slug(company.Name)}.com".ToLowerInvariant();
            if (!emails.Add(email)) continue;

            var created = Later(company.CreatedAt);
            contacts.Add(new Contact
            {
                FirstName = first,
                LastName = last,
                Email = email,
                Phone = faker.Phone.PhoneNumber("+1 ###-###-####"),
                JobTitle = Pick(titles),
                Stage = Weighted([(ContactStage.Customer, 30), (ContactStage.Opportunity, 25), (ContactStage.Lead, 30), (ContactStage.Subscriber, 10), (ContactStage.Churned, 5)]),
                Company = company,
                Owner = company.Owner,
                CreatedAt = created,
                LastContactedAt = _rng.NextDouble() < 0.8 ? Later(created) : null,
            });
        }
        return contacts;
    }

    private List<Deal> SeedDeals(List<User> users, List<Company> companies, List<Contact> contacts, int count)
    {
        var byCompany = contacts.GroupBy(c => c.Company!).ToDictionary(g => g.Key, g => g.ToList());
        var deals = new List<Deal>();

        for (var i = 0; i < count; i++)
        {
            // Date first, then a company that already existed on that date, so new accounts don't hoard recent deals.
            var created = RandomPast(Months);
            var known = companies.Where(c => c.CreatedAt <= created).ToList();
            var company = known.Count > 0 ? Pick(known) : Pick(companies);
            if (created < company.CreatedAt) created = Later(company.CreatedAt);

            var deal = new Deal
            {
                Title = $"{company.Name} - {Pick(DealNames)}",
                Amount = RoundTo((decimal)(Weighted([(4_000, 35), (12_000, 35), (35_000, 22), (90_000, 8)]) * (0.6 + _rng.NextDouble())), 100),
                Company = company,
                Contact = byCompany.TryGetValue(company, out var people) ? Pick(people) : null,
                Owner = _rng.NextDouble() < 0.7 ? company.Owner : Pick(users),
                CreatedAt = created,
                ExpectedCloseDate = created.Date.AddDays(_rng.Next(20, 90)),
            };

            // Older deals have mostly closed; recent ones are still moving through the pipeline.
            var age = (_now - created).TotalDays;
            if (age > 45 && _rng.NextDouble() < 0.82)
            {
                var closedAt = Min(created.AddDays(_rng.Next(14, 75)), _now.AddHours(-_rng.Next(1, 48)));
                deal.Close(_rng.NextDouble() < 0.42 ? DealStage.Won : DealStage.Lost, closedAt);
            }
            else
            {
                deal.MoveTo(Weighted([(DealStage.Prospecting, 30), (DealStage.Qualification, 28), (DealStage.Proposal, 24), (DealStage.Negotiation, 18)]), created);
                if (deal.ExpectedCloseDate < _now.Date) deal.ExpectedCloseDate = _now.Date.AddDays(_rng.Next(-10, 60));
            }
            deals.Add(deal);
        }
        return deals;
    }

    private List<Lead> SeedLeads(List<User> users, int count)
    {
        var faker = new Faker();
        var leads = new List<Lead>();

        for (var i = 0; i < count; i++)
        {
            var created = RandomPast(Months);
            var age = (_now - created).TotalDays;
            var name = faker.Name.FullName();
            var companyName = faker.Company.CompanyName().Replace(",", "");

            leads.Add(new Lead
            {
                Name = name,
                CompanyName = companyName,
                Email = $"{name.Replace(" ", ".").Replace("'", "")}@{Slug(companyName)}.com".ToLowerInvariant(),
                Phone = faker.Phone.PhoneNumber("+1 ###-###-####"),
                Source = Weighted([(LeadSource.Website, 28), (LeadSource.Referral, 18), (LeadSource.LinkedIn, 17), (LeadSource.Ads, 13), (LeadSource.Event, 10), (LeadSource.ColdCall, 8), (LeadSource.Partner, 6)]),
                Status = age < 10
                    ? Weighted([(LeadStatus.New, 70), (LeadStatus.Contacted, 30)])
                    : Weighted([(LeadStatus.Contacted, 22), (LeadStatus.Qualified, 24), (LeadStatus.Unqualified, 22), (LeadStatus.Converted, 32)]),
                Score = _rng.Next(5, 99),
                EstimatedValue = RoundTo(_rng.Next(2_000, 60_000), 100),
                Owner = Pick(users),
                CreatedAt = created,
            });
        }
        return leads;
    }

    private List<CrmTask> SeedTasks(List<User> users, List<Contact> contacts, List<Deal> deals, int count)
    {
        var subjects = new Dictionary<TaskType, string[]>
        {
            [TaskType.Call] = ["Discovery call", "Check in on the proposal", "Pricing call", "Intro call with the buyer"],
            [TaskType.Email] = ["Send the case study", "Share the quote", "Follow up on the trial", "Send the contract"],
            [TaskType.Meeting] = ["Product demo", "Quarterly review", "Kick-off meeting", "Security review"],
            [TaskType.FollowUp] = ["Chase the signed order", "Confirm the decision date", "Ask for a referral", "Book the next step"],
        };

        var tasks = new List<CrmTask>();
        for (var i = 0; i < count; i++)
        {
            var type = Pick(Enum.GetValues<TaskType>());
            var deal = _rng.NextDouble() < 0.55 ? Pick(deals) : null;
            var contact = deal?.Contact ?? Pick(contacts);

            // Mostly the last two months and the next three weeks: what a rep's task list actually looks like.
            var due = _now.Date.AddDays(_rng.Next(-60, 21)).AddHours(_rng.Next(9, 18)).AddMinutes(_rng.Next(0, 4) * 15);
            var task = new CrmTask
            {
                Subject = $"{Pick(subjects[type])} - {contact.FirstName}",
                Type = type,
                Priority = Weighted([(TaskPriority.Normal, 60), (TaskPriority.High, 25), (TaskPriority.Low, 15)]),
                DueAt = due,
                Contact = contact,
                Deal = deal,
                Owner = deal?.Owner ?? contact.Owner,
                CreatedAt = due.AddDays(-_rng.Next(1, 14)),
            };

            if (due < _now && _rng.NextDouble() < CompletionOdds(_now - due))
                task.Complete(Min(due.AddHours(_rng.Next(0, 30)), _now));
            tasks.Add(task);
        }
        return tasks;
    }

    // Reps clear old work: last month's tasks are nearly all done, this week's still have stragglers.
    private static double CompletionOdds(TimeSpan overdueBy) => overdueBy.TotalDays switch
    {
        > 21 => 0.99,
        > 7 => 0.93,
        > 2 => 0.75,
        _ => 0.45,
    };

    private void LinkConvertedLeads(List<Lead> leads, List<Deal> deals)
    {
        var targets = deals.OrderBy(d => d.CreatedAt).ToList();
        foreach (var lead in leads.Where(l => l.Status == LeadStatus.Converted))
        {
            var deal = targets.FirstOrDefault(d => d.CreatedAt >= lead.CreatedAt) ?? targets[^1];
            lead.Status = LeadStatus.Qualified;
            lead.MarkConverted(deal.Id, Min(deal.CreatedAt, _now));
        }
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    private T Weighted<T>((T Value, int Weight)[] options)
    {
        var roll = _rng.Next(options.Sum(o => o.Weight));
        foreach (var (value, weight) in options)
        {
            if (roll < weight) return value;
            roll -= weight;
        }
        return options[^1].Value;
    }

    // Business-hours timestamps, slightly busier in recent months as the pipeline grows.
    private DateTime RandomPast(int months)
    {
        var days = (int)(Math.Pow(_rng.NextDouble(), 1.25) * months * 30);
        return _now.Date.AddDays(-days).AddHours(_rng.Next(9, 19)).AddMinutes(_rng.Next(60));
    }

    private DateTime Later(DateTime after)
    {
        var span = Math.Max(1, (int)(_now - after).TotalDays);
        return Min(after.AddDays(_rng.Next(0, span)).AddHours(_rng.Next(0, 8)), _now.AddMinutes(-_rng.Next(5, 600)));
    }

    private static decimal RoundTo(decimal value, int step) => Math.Round(value / step) * step;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string Slug(string value) =>
        new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()) is { Length: > 0 } s ? s[..Math.Min(s.Length, 24)] : "company";
}
