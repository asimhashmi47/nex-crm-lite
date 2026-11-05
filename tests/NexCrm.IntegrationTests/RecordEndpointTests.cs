using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexCrm.Domain.Entities;

namespace NexCrm.IntegrationTests;

public sealed class CompanyEndpointTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    private async Task<object> Body(string name) => new
    {
        name,
        industry = "Software",
        website = " ",
        phone = "+92 21 555 0101",
        city = "Karachi",
        country = "Pakistan",
        employees = 25,
        annualRevenue = 1_500_000,
        ownerId = await api.UserIdAsync("sara"),
    };

    [Fact, Trait("AC", "CO-1")]
    public async Task Create_Returns201_WithContract_AndWritesTheRow()
    {
        var client = await api.ClientAsAsync("sara");
        var res = await client.PostAsJsonAsync("/api/companies", await Body("  Contract Co  "));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Contract Co", dto.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("website").ValueKind);
        Assert.Equal("sara user", dto.GetProperty("owner").GetString());
        foreach (var field in new[] { "id", "contacts", "openDeals", "wonRevenue", "createdAt" }) Assert.True(dto.TryGetProperty(field, out _), field);

        var row = await api.QueryDbAsync(db => db.Companies.SingleAsync(c => c.Name == "Contract Co"));
        Assert.Equal((25, 1_500_000m, false), (row.Employees, row.AnnualRevenue, row.IsDeleted));
    }

    [Fact, Trait("AC", "CO-2")]
    public async Task Create_DuplicateIgnoringCase_Returns409_AndWritesNothing()
    {
        var client = await api.ClientAsAsync("sara");
        (await client.PostAsJsonAsync("/api/companies", await Body("Dup Co"))).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/companies", await Body("DUP CO"));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(1, await api.QueryDbAsync(db => db.Companies.CountAsync(c => c.Name.ToLower() == "dup co")));
    }

    [Fact, Trait("AC", "CO-4")]
    public async Task UnknownId_Returns404Problem_ForGetPutDelete()
    {
        var client = await api.ClientAsAsync("manager");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/companies/99999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/companies/99999", await Body("Ghost"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/companies/99999")).StatusCode);
    }

    [Fact, Trait("AC", "PERM-1")]
    public async Task Delete_BySalesRep_Is403_ByManager_Is204AndArchives()
    {
        var rep = await api.ClientAsAsync("sara");
        var created = await (await rep.PostAsJsonAsync("/api/companies", await Body("Delete Me Co"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Forbidden, (await rep.DeleteAsync($"/api/companies/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await (await api.ClientAsAsync("manager")).DeleteAsync($"/api/companies/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await rep.GetAsync($"/api/companies/{id}")).StatusCode);
        Assert.True(await api.QueryDbAsync(db => db.Companies.IgnoreQueryFilters().Where(c => c.Id == id).Select(c => c.IsDeleted).SingleAsync()));
    }

    [Fact, Trait("AC", "PERM-2")]
    public async Task Create_WithInactiveOwner_Returns400()
    {
        var client = await api.ClientAsAsync("sara");
        var body = new { name = "Orphan Co", industry = "Retail", city = "Lahore", country = "Pakistan", employees = 1, annualRevenue = 0, ownerId = await api.UserIdAsync("gone") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/companies", body)).StatusCode);
    }

    [Fact, Trait("AC", "CO-1")]
    public async Task Put_SameBodyTwice_IsIdempotent()
    {
        var client = await api.ClientAsAsync("sara");
        var id = (await (await client.PostAsJsonAsync("/api/companies", await Body("Retry Co"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var first = await client.PutAsJsonAsync($"/api/companies/{id}", await Body("Retry Co Renamed"));
        var second = await client.PutAsJsonAsync($"/api/companies/{id}", await Body("Retry Co Renamed"));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal(1, await api.QueryDbAsync(db => db.Companies.CountAsync(c => c.Name.StartsWith("Retry Co"))));
    }

    [Fact, Trait("AC", "GQ-1")]
    public async Task Query_ClampsPaging_AndReturnsTheGridContract()
    {
        var client = await api.ClientAsAsync("sara");
        (await client.PostAsJsonAsync("/api/companies", await Body("Grid Co"))).EnsureSuccessStatusCode();

        var page = await (await client.PostAsJsonAsync("/api/companies/query", new { page = -3, pageSize = 100_000, search = "GRID", sumFields = new[] { "employees" } }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((1, 500), (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32()));
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(25m, page.GetProperty("sums").GetProperty("employees").GetDecimal());
    }

    [Theory, Trait("AC", "GQ-6")]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("csv", "text/csv")]
    public async Task Export_ReturnsAFileWithTheRightTypeAndName(string format, string contentType)
    {
        var client = await api.ClientAsAsync("sara");
        var res = await client.PostAsJsonAsync("/api/companies/export", new { format, fileName = "my companies", query = new { }, columns = new[] { new { field = "name", header = "Company" } } });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(contentType, res.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("my companies-", res.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact, Trait("AC", "GQ-6")]
    public async Task Export_WithOnlyUnknownColumns_Returns400()
    {
        var client = await api.ClientAsAsync("sara");
        var res = await client.PostAsJsonAsync("/api/companies/export", new { format = "csv", query = new { }, columns = new[] { new { field = "passwordHash", header = "x" } } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

public sealed class PipelineEndpointTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    private async Task<int> CompanyAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/companies", new { name, industry = "Software", city = "Karachi", country = "Pakistan", employees = 5, annualRevenue = 0, ownerId = await api.UserIdAsync("sara") });
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task<int> LeadAsync(HttpClient client, string company, string email, string status = "Qualified")
    {
        var res = await client.PostAsJsonAsync("/api/leads", new { name = "Omer Gill", companyName = company, email, source = "Referral", status, score = 70, estimatedValue = 18_000, ownerId = await api.UserIdAsync("sara") });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact, Trait("AC", "DL-2")]
    public async Task Deal_CreateMoveAndBoard_FollowTheStageRules()
    {
        var client = await api.ClientAsAsync("sara");
        var companyId = await CompanyAsync(client, "Board Co");
        var create = await client.PostAsJsonAsync("/api/deals", new { title = "Board deal", amount = 12_000, stage = "Proposal", probability = 55, expectedCloseDate = "2027-01-15", companyId, ownerId = await api.UserIdAsync("sara") });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var deal = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Proposal", 55, 6_600m), (deal.GetProperty("stage").GetString(), deal.GetProperty("probability").GetInt32(), deal.GetProperty("weighted").GetDecimal()));
        var id = deal.GetProperty("id").GetInt32();

        var won = await (await client.PatchAsJsonAsync($"/api/deals/{id}/stage", new { stage = "Won" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, won.GetProperty("probability").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, won.GetProperty("closedAt").ValueKind);

        var again = await client.PatchAsJsonAsync($"/api/deals/{id}/stage", new { stage = "Won" });
        Assert.Equal(won.GetProperty("closedAt").GetString(), (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("closedAt").GetString());

        var board = await client.GetFromJsonAsync<JsonElement>("/api/deals/board");
        Assert.Equal(6, board.GetArrayLength());
        Assert.Contains(board.EnumerateArray().Single(c => c.GetProperty("stage").GetString() == "Won").GetProperty("cards").EnumerateArray(), c => c.GetProperty("id").GetInt32() == id);
    }

    [Theory, Trait("AC", "DL-5")]
    [InlineData("{\"stage\":\"Teleported\"}")]
    [InlineData("{\"stage\":99}")]
    [InlineData("{}")]
    public async Task MoveDeal_InvalidStage_Returns400(string json)
    {
        var client = await api.ClientAsAsync("sara");
        var companyId = await CompanyAsync(client, $"Bad stage {Guid.NewGuid():N}");
        var id = (await (await client.PostAsJsonAsync("/api/deals", new { title = "x", amount = 1, stage = "Prospecting", probability = 10, expectedCloseDate = "2027-01-01", companyId, ownerId = await api.UserIdAsync("sara") }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var res = await client.PatchAsync($"/api/deals/{id}/stage", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact, Trait("AC", "DL-1")]
    public async Task Deal_WithoutCloseDate_Returns400()
    {
        var client = await api.ClientAsAsync("sara");
        var companyId = await CompanyAsync(client, "No Date Co");
        var res = await client.PostAsJsonAsync("/api/deals", new { title = "x", amount = 10, stage = "Prospecting", probability = 10, companyId, ownerId = await api.UserIdAsync("sara") });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact, Trait("AC", "LD-3")]
    public async Task Lead_Convert_CreatesDeal_ThenRefusesSecondConvertAndEdits()
    {
        var client = await api.ClientAsAsync("sara");
        var leadId = await LeadAsync(client, "Convert Co", "omer@convert.test");

        var first = await client.PostAsync($"/api/leads/{leadId}/convert", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var result = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Convert Co - new business", result.GetProperty("dealTitle").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/leads/{leadId}/convert", null)).StatusCode);
        var edit = await client.PutAsJsonAsync($"/api/leads/{leadId}", new { name = "x", companyName = "y", email = "z@z.test", source = "Ads", status = "New", score = 1, estimatedValue = 1, ownerId = await api.UserIdAsync("sara") });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact, Trait("AC", "LD-2")]
    public async Task Lead_SavedAsConverted_Returns400()
    {
        var client = await api.ClientAsAsync("sara");
        var res = await client.PostAsJsonAsync("/api/leads", new { name = "x", companyName = "y", email = "z@z.test", source = "Ads", status = "Converted", score = 1, estimatedValue = 1, ownerId = await api.UserIdAsync("sara") });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact, Trait("AC", "LD-6")]
    public async Task Lead_Converted50TimesInParallel_CreatesExactlyOneDeal()
    {
        var client = await api.ClientAsAsync("sara");
        var leadId = await LeadAsync(client, "Race Co", "race@race.test");

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => client.PostAsync($"/api/leads/{leadId}/convert", null)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Equal(1, await api.QueryDbAsync(db => db.Deals.CountAsync(d => d.Title == "Race Co - new business")));
        Assert.Equal(1, await api.QueryDbAsync(db => db.Companies.CountAsync(c => c.Name == "Race Co")));
    }

    [Fact, Trait("AC", "TK-2")]
    public async Task Task_MarkDone_StampsCompletion_AndTouchesContact()
    {
        var client = await api.ClientAsAsync("sara");
        var contact = await client.PostAsJsonAsync("/api/contacts", new { firstName = "Zoë", lastName = "محمد", email = "ZOE@task.test", stage = "Lead", ownerId = await api.UserIdAsync("sara") });
        Assert.Equal(HttpStatusCode.Created, contact.StatusCode);
        var contactBody = await contact.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("zoe@task.test", "محمد"), (contactBody.GetProperty("email").GetString(), contactBody.GetProperty("lastName").GetString()));

        var task = await client.PostAsJsonAsync("/api/tasks", new { subject = "Call Zoë", type = "Call", priority = "High", dueAt = "2026-01-01T10:00", contactId = contactBody.GetProperty("id").GetInt32(), ownerId = await api.UserIdAsync("sara") });
        var taskBody = await task.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = taskBody.GetProperty("id").GetInt32();
        Assert.True(taskBody.GetProperty("overdue").GetBoolean());

        var done = await (await client.PatchAsJsonAsync($"/api/tasks/{taskId}/done", new { done = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(done.GetProperty("isDone").GetBoolean());
        Assert.False(done.GetProperty("overdue").GetBoolean());
        Assert.NotNull(await api.QueryDbAsync(db => db.Contacts.Where(c => c.Email == "zoe@task.test").Select(c => c.LastContactedAt).SingleAsync()));
    }

    [Fact, Trait("AC", "CT-1")]
    public async Task Contact_DuplicateEmailIgnoringCase_Returns409()
    {
        var client = await api.ClientAsAsync("sara");
        var owner = await api.UserIdAsync("sara");
        (await client.PostAsJsonAsync("/api/contacts", new { firstName = "A", lastName = "B", email = "dupe@x.test", stage = "Lead", ownerId = owner })).EnsureSuccessStatusCode();
        var res = await client.PostAsJsonAsync("/api/contacts", new { firstName = "C", lastName = "D", email = "DUPE@x.test", stage = "Lead", ownerId = owner });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact, Trait("AC", "DB-1")]
    public async Task Dashboard_AndLookups_ReturnTheirContracts()
    {
        var client = await api.ClientAsAsync("sara");
        await CompanyAsync(client, "Lookup Co");

        var dash = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        foreach (var part in new[] { "kpis", "trend", "pipeline", "sources", "leaderboard", "dueTasks", "closingSoon" }) Assert.True(dash.TryGetProperty(part, out _), part);
        Assert.Equal(12, dash.GetProperty("trend").GetArrayLength());

        var owners = await client.GetFromJsonAsync<JsonElement>("/api/lookups/owners");
        Assert.Equal(3, owners.GetArrayLength());
        var companies = await client.GetFromJsonAsync<JsonElement>("/api/lookups/companies?term=lookup");
        Assert.Equal("Lookup Co", companies[0].GetProperty("label").GetString());
    }
}

public sealed class AlertEndpointTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact, Trait("AC", "DB-7"), Trait("LoadFix", "3")]
    public async Task Alerts_AreASmallSummaryForTheBell()
    {
        var client = await api.ClientAsAsync("sara");

        var res = await client.GetAsync("/api/dashboard/alerts");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["closingSoon", "newLeadsThisMonth", "openPipeline", "overdueTasks", "tasksDueToday"], body.EnumerateObject().Select(p => p.Name).Order());
        Assert.True((await res.Content.ReadAsByteArrayAsync()).Length < 300, "the bell payload stays tiny");
    }
}
