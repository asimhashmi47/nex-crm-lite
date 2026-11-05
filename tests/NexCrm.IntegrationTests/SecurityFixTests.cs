using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace NexCrm.IntegrationTests;

/// <summary>One test per pentest finding (security/SECURITY-REVIEW.md). Each failed before its fix.</summary>
public sealed class SecurityFixTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    private async Task<int> CreateCompanyAsync(HttpClient client, string name, string owner = "sara")
    {
        var res = await client.PostAsJsonAsync("/api/companies", new { name, industry = "Software", city = "Karachi", country = "Pakistan", employees = 1, annualRevenue = 0, ownerId = await api.UserIdAsync(owner) });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact, Trait("AC", "AUTH-3"), Trait("Finding", "F-01")]
    public async Task DeactivatedUser_LosesAccess_WithTheTokenTheyAlreadyHave()
    {
        await api.EnsureUsersAsync();
        await api.QueryDbAsync(async db => { db.Users.Add(new() { Username = "leaver", FullName = "Leaver", Email = "l@test", Role = Domain.Entities.UserRole.SalesRep, PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Domain.Entities.User>().HashPassword(new(), "leaver") }); return await db.SaveChangesAsync(); });
        var client = await api.ClientAsAsync("leaver");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard")).StatusCode);

        await api.QueryDbAsync(db => db.Users.Where(u => u.Username == "leaver").ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/companies/query", new { })).StatusCode);
    }

    [Fact, Trait("AC", "AUTH-9"), Trait("Finding", "F-02")]
    public async Task Logout_RevokesTheToken_OnTheServer()
    {
        var client = await api.ClientAsAsync("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
        var fresh = await api.ClientAsAsync("admin");
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact, Trait("AC", "PERM-4"), Trait("Finding", "F-03")]
    public async Task SalesRep_CannotReownAColleaguesRecord_ButManagersCan()
    {
        var manager = await api.ClientAsAsync("manager");
        var rep = await api.ClientAsAsync("sara");
        var id = await CreateCompanyAsync(manager, "Owned By Manager", owner: "manager");
        var managerId = await api.UserIdAsync("manager");
        var saraId = await api.UserIdAsync("sara");
        var body = (string owner) => new { name = "Owned By Manager", industry = "Retail", city = "Lahore", country = "Pakistan", employees = 2, annualRevenue = 0, ownerId = owner == "sara" ? saraId : managerId };

        Assert.Equal(HttpStatusCode.OK, (await rep.PutAsJsonAsync($"/api/companies/{id}", body("manager"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.PutAsJsonAsync($"/api/companies/{id}", body("sara"))).StatusCode);
        Assert.Equal(managerId, await api.QueryDbAsync(db => db.Companies.Where(c => c.Id == id).Select(c => c.OwnerId).SingleAsync()));

        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync($"/api/companies/{id}", body("sara"))).StatusCode);
        // Now it is sara's, she may hand it back.
        Assert.Equal(HttpStatusCode.OK, (await rep.PutAsJsonAsync($"/api/companies/{id}", body("manager"))).StatusCode);
    }

    [Fact, Trait("AC", "CO-2"), Trait("Finding", "F-04")]
    public async Task ParallelCreates_OfOneCompanyName_MakeExactlyOneRow()
    {
        var client = await api.ClientAsAsync("sara");
        var owner = await api.UserIdAsync("sara");
        var rs = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => client.PostAsJsonAsync("/api/companies",
            new { name = i % 2 == 0 ? "Parallel Co" : "PARALLEL CO ", industry = "Retail", city = "Lahore", country = "Pakistan", employees = 1, annualRevenue = 0, ownerId = owner })));

        Assert.Equal(1, rs.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(rs.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await api.QueryDbAsync(db => db.Companies.CountAsync(c => c.Name.ToLower().Trim() == "parallel co")));
    }

    [Fact, Trait("AC", "CT-1"), Trait("Finding", "F-05")]
    public async Task ParallelCreates_OfOneContactEmail_MakeExactlyOneRow()
    {
        var client = await api.ClientAsAsync("sara");
        var owner = await api.UserIdAsync("sara");
        var rs = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => client.PostAsJsonAsync("/api/contacts",
            new { firstName = "Twin", lastName = "Race", email = "twin@race.test", stage = "Lead", ownerId = owner })));

        Assert.Equal(1, rs.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(rs.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact, Trait("AC", "X-6"), Trait("Finding", "F-06")]
    public async Task SensitiveActions_AreAudited_AndOnlyAdminsCanReadTheLog()
    {
        var manager = await api.ClientAsAsync("manager");
        var id = await CreateCompanyAsync(manager, "Audited Co", owner: "manager");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/companies/{id}")).StatusCode);
        (await manager.PostAsJsonAsync("/api/contacts/export", new { format = "csv", query = new { }, columns = new[] { new { field = "email", header = "Email" } } })).EnsureSuccessStatusCode();

        var admin = await api.ClientAsAsync("admin");
        var log = await (await admin.PostAsJsonAsync("/api/audit/query", new { pageSize = 50, sortBy = "at", sortDesc = true })).Content.ReadFromJsonAsync<JsonElement>();
        var rows = log.GetProperty("items").EnumerateArray().Select(r => (Action: r.GetProperty("action").GetString(), User: r.GetProperty("username").GetString(), Entity: r.GetProperty("entityId").ToString())).ToList();

        Assert.Contains(rows, r => r.Action == "Company.Archived" && r.User == "manager" && r.Entity == id.ToString());
        Assert.Contains(rows, r => r.Action == "Company.Created" && r.User == "manager");
        Assert.Contains(rows, r => r.Action == "Contacts.Exported" && r.User == "manager");
        Assert.Contains(rows, r => r.Action == "Auth.SignedIn" && r.User == "manager");

        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.ClientAsAsync("sara")).PostAsJsonAsync("/api/audit/query", new { })).StatusCode);
    }

    [Theory, Trait("AC", "LD-5"), Trait("Finding", "F-09")]
    [InlineData("New")]
    [InlineData("Contacted")]
    public async Task OnlyQualifiedLeads_CanBeConverted(string status)
    {
        var client = await api.ClientAsAsync("sara");
        var res = await client.PostAsJsonAsync("/api/leads", new { name = "Early Bird", companyName = $"Early {status}", email = $"early.{status}@x.test", source = "Ads", status, score = 10, estimatedValue = 100, ownerId = await api.UserIdAsync("sara") });
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/leads/{id}/convert", null)).StatusCode);
    }

    [Fact, Trait("AC", "X-7"), Trait("Finding", "F-10")]
    public async Task IdempotencyKey_MakesARetriedCreateReturnTheFirstResult()
    {
        var client = await api.ClientAsAsync("sara");
        var owner = await api.UserIdAsync("sara");
        var key = Guid.NewGuid().ToString();
        HttpRequestMessage Request(string subject) => new(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { subject, type = "Call", priority = "Low", dueAt = DateTime.Today.AddDays(1).ToString("s"), ownerId = owner }),
            Headers = { { "Idempotency-Key", key } },
        };

        var rs = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.SendAsync(Request("Idempotent call"))));
        var ids = await Task.WhenAll(rs.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32()));

        Assert.All(rs, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await api.QueryDbAsync(db => db.Tasks.CountAsync(t => t.Subject == "Idempotent call")));
        Assert.Equal((HttpStatusCode)422, (await client.SendAsync(Request("Different body, same key"))).StatusCode);
    }

    [Theory, Trait("AC", "DL-1"), Trait("Finding", "F-11")]
    [InlineData("deals", "9999-12-31")]
    [InlineData("deals", "1900-01-01")]
    [InlineData("tasks", "0001-01-02T09:00")]
    [InlineData("tasks", "2200-01-01T09:00")]
    public async Task AbsurdDates_AreRejected(string resource, string date)
    {
        var client = await api.ClientAsAsync("sara");
        var owner = await api.UserIdAsync("sara");
        object body = resource == "deals"
            ? new { title = "Far", amount = 1, stage = "Prospecting", probability = 10, expectedCloseDate = date, companyId = await CreateCompanyAsync(client, $"Dates {Guid.NewGuid():N}"), ownerId = owner }
            : new { subject = "Far", type = "Call", priority = "Low", dueAt = date, ownerId = owner };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/{resource}", body)).StatusCode);
    }

    [Fact, Trait("AC", "X-8"), Trait("Finding", "F-12")]
    public async Task OversizedBody_IsRejectedBeforeParsing()
    {
        var client = await api.ClientAsAsync("sara");
        var big = new StringContent($"{{\"notes\":\"{new string('A', 2 * 1024 * 1024)}\"}}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/tasks", big)).StatusCode);
    }

    [Fact, Trait("AC", "X-2"), Trait("Finding", "F-13")]
    public async Task HttpsResponses_CarryHsts()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://crm.test") });
        var res = await client.GetAsync("/api/auth/config");
        Assert.StartsWith("max-age=", res.Headers.GetValues("Strict-Transport-Security").Single());
    }
}

public sealed class TightLimitsFactory : CrmApiFactory
{
    protected override void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.Configure(builder);
        builder.UseSetting("RateLimiting:ExportsPerMinute", "2");
        builder.UseSetting("RateLimiting:RequestsPerMinute", "40");
        builder.UseSetting("RateLimiting:FailedLoginsPerSource", "5");
        builder.UseSetting("RateLimiting:FailedLoginsPerAccount", "20");
    }
}

public sealed class LimitTests(TightLimitsFactory api) : IClassFixture<TightLimitsFactory>
{
    [Fact, Trait("AC", "X-9"), Trait("Finding", "F-07")]
    public async Task Exports_AreThrottledPerUser()
    {
        var client = await api.ClientAsAsync("sara");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/companies/export", new { format = "csv", query = new { }, columns = new[] { new { field = "name", header = "n" } } })).StatusCode);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact, Trait("AC", "X-9"), Trait("Finding", "F-07")]
    public async Task ApiCalls_AreThrottledPerUser_WithoutAffectingOthers()
    {
        var greedy = await api.ClientAsAsync("manager");
        var rs = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => greedy.GetAsync("/api/lookups/owners")));
        Assert.Contains(rs, r => r.StatusCode == HttpStatusCode.TooManyRequests);

        var other = await api.ClientAsAsync("admin");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/lookups/owners")).StatusCode);
    }

    [Fact, Trait("AC", "AUTH-7"), Trait("Finding", "F-08")]
    public async Task RepeatedWrongPasswords_LockTheAccount_EvenWithTheRightPassword()
    {
        // Its own account, so locking it can't affect the other tests sharing this host.
        await api.EnsureUsersAsync();
        await api.QueryDbAsync(async db => { db.Users.Add(new() { Username = "locky", FullName = "Locky", Email = "locky@test", Role = Domain.Entities.UserRole.SalesRep, PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Domain.Entities.User>().HashPassword(new(), "locky") }); return await db.SaveChangesAsync(); });
        var client = api.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "locky", password = $"wrong{i}" })).StatusCode);

        var locked = await client.PostAsJsonAsync("/api/auth/login", new { username = "locky", password = "locky" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Contains("minutes", (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        // Claiming to be someone else through X-Forwarded-For must not open a fresh per-address counter.
        var spoofed = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { username = "locky", password = "locky" }) };
        spoofed.Headers.Add("X-Forwarded-For", "203.0.113.9");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(spoofed)).StatusCode);
    }
}

public sealed class DiagnosticsTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact, Trait("AC", "X-10")]
    public async Task RuntimeCounters_AreOffByDefault_AndAdminOnly()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.ClientAsAsync("admin")).GetAsync("/api/ops/runtime")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.ClientAsAsync("sara")).GetAsync("/api/ops/runtime")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/ops/runtime")).StatusCode);
    }
}

public sealed class DiagnosticsOnFactory : CrmApiFactory
{
    protected override void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.Configure(builder);
        builder.UseSetting("Diagnostics:Enabled", "true");
    }
}

public sealed class DiagnosticsOnTests(DiagnosticsOnFactory api) : IClassFixture<DiagnosticsOnFactory>
{
    [Fact, Trait("AC", "X-10")]
    public async Task RuntimeCounters_WhenEnabled_ReportProcessGcAndSqlTimings()
    {
        var admin = await api.ClientAsAsync("admin");
        (await admin.GetAsync("/api/dashboard")).EnsureSuccessStatusCode();

        var body = await admin.GetFromJsonAsync<JsonElement>("/api/ops/runtime");

        Assert.True(body.GetProperty("workingSetMb").GetDouble() > 0);
        Assert.True(body.GetProperty("threadPoolThreads").GetInt32() > 0);
        var statements = body.GetProperty("statements").EnumerateArray().ToList();
        Assert.NotEmpty(statements);
        Assert.All(statements, s => Assert.True(s.GetProperty("count").GetInt64() > 0));
    }
}

public sealed class ThreadPoolTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact, Trait("LoadFix", "5")]
    public async Task ThreadPool_StartsWideEnoughForBlockingSqliteCalls()
    {
        await api.EnsureUsersAsync();   // starts the host, which applies the setting
        ThreadPool.GetMinThreads(out var workers, out _);
        Assert.True(workers >= Math.Min(32, Environment.ProcessorCount * 4), $"min worker threads {workers}");
    }
}
