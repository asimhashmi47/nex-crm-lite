using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexCrm.Domain.Entities;
using NexCrm.Infrastructure.Persistence;

namespace NexCrm.IntegrationTests;

/// <summary>
/// The real API in memory: full middleware pipeline, auth, validation and EF migrations, on a throwaway
/// SQLite file per test class. Nothing is mocked; the API has no third-party services to stub.
/// </summary>
public class CrmApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexcrm-it", Guid.NewGuid().ToString("N"));

    protected virtual int LoginPerMinute => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder) => Configure(builder);

    protected virtual void Configure(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, "crm.db")}");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("App:DemoMode", "true");
        builder.UseSetting("App:ClientOrigin", "http://localhost:4300");
        builder.UseSetting("Database:SeedDemoData", "false");
        builder.UseSetting("RateLimiting:LoginPerMinute", LoginPerMinute.ToString());
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:ExportsPerMinute", "1000");
        builder.UseSetting("RateLimiting:FailedLoginsPerSource", "50");
        builder.UseSetting("RateLimiting:FailedLoginsPerAccount", "50");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Serilog:WriteTo:1:Args:path", Path.Combine(_dir, "log-.txt"));
    }

    private bool _seeded;
    private readonly SemaphoreSlim _seedLock = new(1, 1);

    /// <summary>admin / manager / sara, password = username, plus one deactivated account.</summary>
    public async Task EnsureUsersAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (name, role, active) in new[] { ("admin", UserRole.Admin, true), ("manager", UserRole.Manager, true), ("sara", UserRole.SalesRep, true), ("gone", UserRole.SalesRep, false) })
            {
                var user = new User { Username = name, FullName = $"{name} user", Email = $"{name}@test", Role = role, IsActive = active };
                user.PasswordHash = hasher.HashPassword(user, name);
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();
            _seeded = true;
        }
        finally { _seedLock.Release(); }
    }

    public async Task<HttpClient> ClientAsAsync(string username)
    {
        await EnsureUsersAsync();
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = username });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenBody>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<T> QueryDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task<int> UserIdAsync(string username) => QueryDbAsync(db => db.Users.Where(u => u.Username == username).Select(u => u.Id).SingleAsync());

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* a file handle may still be closing */ }
    }

    public sealed record TokenBody(string AccessToken, DateTime ExpiresAt, UserBody User);
    public sealed record UserBody(int Id, string Username, string FullName, string Email, string Role, bool CanDelete);
}

/// <summary>Same API with a tight sign-in limit, for the rate-limit test.</summary>
public sealed class StrictLoginFactory : CrmApiFactory
{
    protected override int LoginPerMinute => 3;
}
