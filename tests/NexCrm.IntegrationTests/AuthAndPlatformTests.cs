using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace NexCrm.IntegrationTests;

public sealed class AuthTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact, Trait("AC", "AUTH-1")]
    public async Task Login_Valid_Returns200WithTokenAndUserContract()
    {
        await api.EnsureUsersAsync();
        var res = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = " Manager ", password = "manager" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<CrmApiFactory.TokenBody>();
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.True(body.ExpiresAt > DateTime.UtcNow.AddHours(7));
        Assert.Equal(("manager", "Manager", true), (body.User.Username, body.User.Role, body.User.CanDelete));
        Assert.NotNull(await api.QueryDbAsync(db => db.Users.Where(u => u.Username == "manager").Select(u => u.LastLoginAt).SingleAsync()));
    }

    [Theory, Trait("AC", "AUTH-2")]
    [InlineData("sara", "wrong")]
    [InlineData("nobody", "sara")]
    [InlineData("gone", "gone")]
    public async Task Login_BadOrInactive_Returns401ProblemWithoutDetail(string username, string password)
    {
        await api.EnsureUsersAsync();
        var res = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { username, password });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    [Fact, Trait("AC", "AUTH-4")]
    public async Task Login_EmptyFields_Returns400WithFieldErrors()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("username", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact, Trait("AC", "AUTH-6")]
    public async Task Config_InDemoMode_ListsDemoAccounts_Anonymously()
    {
        var body = await api.CreateClient().GetFromJsonAsync<JsonElement>("/api/auth/config");
        Assert.True(body.GetProperty("demoMode").GetBoolean());
        Assert.Equal(3, body.GetProperty("demoAccounts").GetArrayLength());
    }

    [Fact, Trait("AC", "AUTH-1")]
    public async Task Me_ReturnsTheSignedInUser()
    {
        var client = await api.ClientAsAsync("sara");
        var me = await client.GetFromJsonAsync<CrmApiFactory.UserBody>("/api/auth/me");
        Assert.Equal(("sara", false), (me!.Username, me.CanDelete));
    }

    public static TheoryData<string> ProtectedEndpoints => new()
    {
        "/api/dashboard", "/api/auth/me", "/api/lookups/owners", "/api/deals/board", "/api/companies/1",
    };

    [Theory, Trait("AC", "AUTH-5")]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task ProtectedEndpoint_WithoutToken_Returns401(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(url)).StatusCode);

    [Theory, Trait("AC", "AUTH-5")]
    [InlineData("expired")]
    [InlineData("wrong-signature")]
    [InlineData("alg-none")]
    [InlineData("wrong-issuer")]
    [InlineData("garbage")]
    public async Task TamperedToken_Returns401(string kind)
    {
        await api.EnsureUsersAsync();
        var adminId = await api.UserIdAsync("admin");
        var token = kind switch
        {
            "expired" => Jwt(adminId, CrmApiFactory.SigningKey, expires: DateTime.UtcNow.AddMinutes(-5)),
            "wrong-signature" => Jwt(adminId, "a-completely-different-key-0123456789abcdef"),
            "wrong-issuer" => Jwt(adminId, CrmApiFactory.SigningKey, issuer: "Evil"),
            "alg-none" => AlgNone(adminId),
            _ => "not.a.jwt",
        };
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact, Trait("AC", "AUTH-5")]
    public async Task CorrectlySignedToken_IsAccepted_SoTheTamperTestsAreMeaningful()
    {
        await api.EnsureUsersAsync();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Jwt(await api.UserIdAsync("admin"), CrmApiFactory.SigningKey));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard")).StatusCode);
    }

    private static string Jwt(int userId, string key, DateTime? expires = null, string issuer = "NexCrmLite")
    {
        var exp = expires ?? DateTime.UtcNow.AddHours(1);
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, "NexCrmLite.Client",
            [new Claim("sub", userId.ToString()), new Claim("unique_name", "admin"), new Claim("role", "Admin"), new Claim("sv", "0")],
            notBefore: exp.AddHours(-2), expires: exp, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string AlgNone(int userId)
    {
        static string B64(string s) => Base64UrlEncoder.Encode(s);
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        return $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{userId}\",\"role\":\"Admin\",\"iss\":\"NexCrmLite\",\"aud\":\"NexCrmLite.Client\",\"exp\":{exp}}}")}.";
    }
}

public sealed class RateLimitTests(StrictLoginFactory api) : IClassFixture<StrictLoginFactory>
{
    [Fact, Trait("AC", "AUTH-7")]
    public async Task Login_OverTheLimit_Returns429Problem()
    {
        var client = api.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { username = "x", password = "y" })).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }
}

public sealed class PlatformTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact, Trait("AC", "X-4")]
    public async Task Health_IsAnonymous_AndHealthy()
    {
        var res = await api.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }

    [Fact, Trait("AC", "X-3")]
    public async Task UnknownApiRoute_Returns404Problem_NotTheSpa()
    {
        var res = await (await api.ClientAsAsync("sara")).GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact, Trait("AC", "X-2")]
    public async Task ApiResponses_CarrySecurityHeaders()
    {
        var res = await api.CreateClient().GetAsync("/api/auth/config");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", res.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact, Trait("AC", "X-1")]
    public async Task ValidationErrors_AreProblemDetails_WithCamelCaseFields()
    {
        var client = await api.ClientAsAsync("sara");
        var res = await client.PostAsJsonAsync("/api/companies", new { name = "", industry = "", city = "", country = "", employees = -1, annualRevenue = -1, ownerId = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Please fix the highlighted fields.", body.GetProperty("title").GetString());
        var fields = body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Superset(new HashSet<string> { "name", "industry", "city", "country", "employees", "annualRevenue", "ownerId" }, fields);
    }

    [Fact, Trait("AC", "X-1")]
    public async Task MalformedJson_Returns400_WithoutLeakingInternals()
    {
        var client = await api.ClientAsAsync("sara");
        var res = await client.PostAsync("/api/companies", new StringContent("{\"name\": ", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.DoesNotContain("   at ", await res.Content.ReadAsStringAsync());
    }

    [Fact, Trait("AC", "X-1")]
    public async Task Swagger_IsNotExposed_OutsideDevelopment() =>
        Assert.NotEqual(HttpStatusCode.OK, (await api.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode);
}
