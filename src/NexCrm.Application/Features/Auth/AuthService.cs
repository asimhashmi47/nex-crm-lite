using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexCrm.Application.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Application.Features.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "NexCrmLite";
    public string Audience { get; set; } = "NexCrmLite.Client";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 480;
}

public sealed class AppOptions
{
    /// <summary>Shows the demo accounts on the sign-in page. Never on in production.</summary>
    public bool DemoMode { get; set; }
    public string ClientOrigin { get; set; } = "http://localhost:4300";
}

public sealed record LoginRequest(string Username, string Password);
public sealed record AuthUserDto(int Id, string Username, string FullName, string Email, string Role, bool CanDelete);
public sealed record TokenResponse(string AccessToken, DateTime ExpiresAt, AuthUserDto User);
public sealed record DemoAccount(string Username, string Role, string Description);
public sealed record AuthConfigDto(bool DemoMode, IReadOnlyList<DemoAccount> DemoAccounts);

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(100);
    }
}

public sealed class AuthService(
    IAppDbContext db, IPasswordHasher<User> hasher, ITokenService tokens, IClock clock, LoginThrottle throttle, AuditTrail audit)
{
    /// <summary>A fixed hash so an unknown username costs the same hashing work as a real one.</summary>
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), "not-a-real-password");

    public static readonly IReadOnlyList<DemoAccount> DemoAccounts =
    [
        new("admin", "Admin", "Everything, including deleting records"),
        new("manager", "Manager", "Runs the team: every record, deletes allowed"),
        new("sara", "Sales rep", "Works leads, deals and tasks"),
    ];

    public async Task<TokenResponse> LoginAsync(LoginRequest r, string? address, CancellationToken ct)
    {
        var username = r.Username.Trim().ToLowerInvariant();
        throttle.EnsureAllowed(username, address);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        var hash = user?.PasswordHash ?? DummyHash;
        var ok = hasher.VerifyHashedPassword(user ?? new User(), hash, r.Password) != PasswordVerificationResult.Failed;
        if (user is null || !ok)
        {
            throttle.RecordFailure(username, address);
            await audit.WriteAsync("Auth.SignInFailed", "User", user?.Id, null, user?.Id, username, ct);
            throw new UnauthorizedAccessException("Wrong username or password.");
        }
        if (!user.IsActive) throw new UnauthorizedAccessException("This account is switched off. Ask an admin.");

        throttle.RecordSuccess(username, address);
        user.LastLoginAt = clock.Now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("Auth.SignedIn", "User", user.Id, null, user.Id, user.Username, ct);
        var (token, expires) = tokens.CreateAccessToken(user);
        return new TokenResponse(token, expires, ToDto(user));
    }

    /// <summary>Ends every session of the user: tokens carry the session version, and this bumps it.</summary>
    public async Task LogoutAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;
        user.RevokeSessions();
        audit.Record("Auth.SignedOut", "User", user.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Checked on every authenticated request: the account is still active and the token is current.</summary>
    public Task<bool> IsSessionCurrentAsync(int userId, int sessionVersion, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.SessionVersion == sessionVersion, ct);

    public async Task<AuthUserDto> MeAsync(int userId, CancellationToken ct) =>
        ToDto(await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct) ?? throw new UnauthorizedAccessException("Session ended."));

    public static AuthUserDto ToDto(User u) =>
        new(u.Id, u.Username, u.FullName, u.Email, u.Role == UserRole.SalesRep ? "Sales rep" : u.Role.ToString(), u.IsManager);
}
