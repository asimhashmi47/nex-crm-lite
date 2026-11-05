using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NexCrm.Application.Common;
using NexCrm.Application.Common.Querying;
using NexCrm.Application.Features.Auth;
using NexCrm.Application.Features.Dashboard;
using NexCrm.Application.Features.Lookups;

namespace NexCrm.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(AuthService auth, IOptions<AppOptions> app) : ApiController
{
    public const string LoginRateLimit = "login";

    [AllowAnonymous, HttpGet("config")]
    public AuthConfigDto Config() =>
        new(app.Value.DemoMode, app.Value.DemoMode ? AuthService.DemoAccounts : []);

    [AllowAnonymous, EnableRateLimiting(LoginRateLimit), HttpPost("login")]
    public Task<TokenResponse> Login(LoginRequest request) =>
        auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), Aborted);

    /// <summary>Ends the session on the server: every token issued so far stops working.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromServices] ICurrentUser user)
    {
        if (user.UserId is { } id) await auth.LogoutAsync(id, Aborted);
        return NoContent();
    }

    [HttpGet("me")]
    public Task<AuthUserDto> Me([FromServices] ICurrentUser user) =>
        auth.MeAsync(user.UserId ?? throw new UnauthorizedAccessException("Session ended."), Aborted);
}

[Route("api/audit"), Authorize(Roles = nameof(Domain.Entities.UserRole.Admin))]
public sealed class AuditController(AuditQueryService audit) : ApiController
{
    [HttpPost("query")]
    public Task<PagedResult<AuditDto>> Query(TableQuery query) => audit.PageAsync(query, Aborted);
}

/// <summary>Runtime counters for load tests. Admin only, and 404 unless Diagnostics:Enabled is on.</summary>
[Route("api/ops"), Authorize(Roles = nameof(Domain.Entities.UserRole.Admin))]
public sealed class OpsController(IConfiguration config, IServiceProvider services) : ApiController
{
    [HttpGet("runtime")]
    public IActionResult Runtime([FromQuery] bool reset = false)
    {
        if (!config.GetValue("Diagnostics:Enabled", false)) return NotFound();
        var stats = services.GetService<NexCrm.Infrastructure.Diagnostics.QueryStats>();
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var body = new
        {
            at = DateTime.UtcNow,
            processCpuMs = process.TotalProcessorTime.TotalMilliseconds,
            workingSetMb = process.WorkingSet64 / 1048576.0,
            gcHeapMb = GC.GetTotalMemory(false) / 1048576.0,
            allocatedMb = GC.GetTotalAllocatedBytes() / 1048576.0,
            gcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds,
            gen0 = GC.CollectionCount(0),
            gen1 = GC.CollectionCount(1),
            gen2 = GC.CollectionCount(2),
            threadPoolThreads = ThreadPool.ThreadCount,
            threadPoolQueue = ThreadPool.PendingWorkItemCount,
            lockContentions = Monitor.LockContentionCount,
            handles = process.HandleCount,
            statements = stats?.Top(12),
        };
        if (reset) stats?.Reset();
        return Ok(body);
    }
}

[Route("api/dashboard")]
public sealed class DashboardController(CachedDashboard dashboard) : ApiController
{
    [HttpGet]
    public Task<DashboardDto> Get() => dashboard.GetAsync(Aborted);

    /// <summary>The bell's counts, from the same cached dashboard: a tiny payload polled once a minute.</summary>
    [HttpGet("alerts")]
    public Task<AlertsDto> Alerts() => dashboard.AlertsAsync(Aborted);
}

[Route("api/lookups")]
public sealed class LookupsController(LookupService lookups) : ApiController
{
    [HttpGet("owners")]
    public Task<List<Option>> Owners() => lookups.OwnersAsync(Aborted);

    [HttpGet("companies")]
    public Task<List<Option>> Companies([FromQuery] string? term) => lookups.CompaniesAsync(term, Aborted);

    [HttpGet("contacts")]
    public Task<List<Option>> Contacts([FromQuery] string? term, [FromQuery] int? companyId) => lookups.ContactsAsync(term, companyId, Aborted);

    [HttpGet("deals")]
    public Task<List<Option>> Deals([FromQuery] string? term) => lookups.DealsAsync(term, Aborted);
}
