using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NexCrm.Api.Controllers;
using NexCrm.Api.Infrastructure;
using NexCrm.Application;
using NexCrm.Application.Common;
using NexCrm.Application.Features.Auth;
using NexCrm.Infrastructure;
using NexCrm.Infrastructure.Persistence;
using NexCrm.Infrastructure.Seeding;
using NexCrm.Infrastructure.Services;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        // A unique index refusing a duplicate is an expected outcome (the API answers 409 and logs one warning);
        // EF would otherwise log it twice at Error with a stack trace. Every other database error still logs.
        .Filter.ByExcluding(e => LogFilters.IsUniqueViolation(e.Exception)));

    var config = builder.Configuration;

    // Microsoft.Data.Sqlite's async calls complete synchronously, so each one holds a pool thread. The pool only
    // adds a thread or two a second, which starved Kestrel's accept loop in the spike test; start it wider.
    var minThreads = config.GetValue("Performance:MinThreadPoolThreads", Environment.ProcessorCount * 4);
    ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
    ThreadPool.SetMinThreads(Math.Max(minWorkers, minThreads), Math.Max(minIo, minThreads));
    if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(config["Jwt:SigningKey"]))
        config["Jwt:SigningKey"] = DevSigningKey.LoadOrCreate(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dev-signing.key"));
    var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
    if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
        throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes. Set it with user-secrets or the Jwt__SigningKey environment variable.");
    var app = config.GetSection("App").Get<AppOptions>() ?? new AppOptions();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(config);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddScoped<ValidationFilter>();
    builder.Services.AddScoped<IdempotencyFilter>();

    var maxBody = config.GetValue<long>("Limits:MaxRequestBodyBytes", 1_048_576);
    builder.WebHost.ConfigureKestrel(o =>
    {
        o.Limits.MaxRequestBodySize = maxBody;
        o.AddServerHeader = false;
    });

    builder.Services.AddControllers(o =>
        {
            o.Filters.AddService<ValidationFilter>();
            o.Filters.AddService<IdempotencyFilter>();
        })
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "unique_name",
            RoleClaimType = "role",
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var principal = ctx.Principal!;
                var current = int.TryParse(principal.FindFirst("sub")?.Value, out var userId)
                    && int.TryParse(principal.FindFirst(JwtTokenService.SessionClaim)?.Value, out var version)
                    && await ctx.HttpContext.RequestServices.GetRequiredService<AuthService>().IsSessionCurrentAsync(userId, version, ctx.HttpContext.RequestAborted);
                if (!current) ctx.Fail("The session has ended. Sign in again.");
            },
        };
    });
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    builder.Services.AddRateLimiter(o =>
    {
        static string Caller(HttpContext ctx) => ctx.User.FindFirst("sub")?.Value is { } sub ? $"user:{sub}" : $"ip:{ctx.Connection.RemoteIpAddress}";
        var perMinute = (string key, int fallback) => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue(key, fallback), Window = TimeSpan.FromMinutes(1) };

        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(AuthController.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => perMinute("RateLimiting:LoginPerMinute", 20)));
        // Exports are the cheapest way to copy the whole book; keep them rare per person.
        o.AddPolicy(ExportRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(Caller(ctx), _ => perMinute("RateLimiting:ExportsPerMinute", 10)));
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => ctx.Request.Path.StartsWithSegments("/health")
            ? RateLimitPartition.GetNoLimiter("health")
            : RateLimitPartition.GetFixedWindowLimiter(Caller(ctx), _ => perMinute("RateLimiting:RequestsPerMinute", 600)));
        o.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.ContentType = "application/problem+json";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new { status = 429, title = "Too many requests. Wait a minute and try again." }, ct);
        };
    });

    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(app.ClientOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Content-Disposition")));

    builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
    builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });
    // Only proxies named in config may set the client address. With none configured the middleware is not added
    // at all: empty trust lists would make it accept X-Forwarded-For from anyone, and the sign-in lock is per address.
    var knownProxies = (config.GetSection("Network:KnownProxies").Get<string[]>() ?? [])
        .Select(p => System.Net.IPAddress.TryParse(p, out var address) ? address : null).OfType<System.Net.IPAddress>().ToList();
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear();
        foreach (var proxy in knownProxies) o.KnownProxies.Add(proxy);
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(o =>
    {
        o.SwaggerDoc("v1", new OpenApiInfo { Title = "NexCRM Lite API", Version = "v1", Description = "Leads, deals, contacts and tasks for small sales teams." });
        o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the accessToken from POST /api/auth/login.",
        });
        o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
        o.CustomSchemaIds(t => t.FullName?.Replace("+", ".", StringComparison.Ordinal));
    });

    var web = builder.Build();

    if (app.DemoMode && !web.Environment.IsDevelopment())
        Log.Warning("Demo mode is on outside Development: the sign-in page lists accounts whose password is their username");

    Directory.CreateDirectory(Path.Combine(web.Environment.ContentRootPath, "App_Data"));

    // CLI: dotnet run -- seed [--scale N] [--load-users N]   (drops the database and builds a fresh year of demo data;
    //      load users need the LOADTEST_PASSWORD environment variable)
    if (args.FirstOrDefault() == "seed")
    {
        int Option(string name, int fallback) =>
            args.SkipWhile(a => a != name).Skip(1).Select(a => int.TryParse(a, out var n) ? n : fallback).FirstOrDefault(fallback);
        var scale = Math.Max(1, Option("--scale", 1));
        var loadUsers = Math.Max(0, Option("--load-users", 0));

        using var scope = web.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>()
            .SeedAsync(reset: true, scale, loadUsers, Environment.GetEnvironmentVariable("LOADTEST_PASSWORD"));
        Log.Information("Seeded {Users} users, {Companies} companies, {Contacts} contacts, {Leads} leads, {Deals} deals and {Tasks} tasks in {Elapsed}",
            result.Users, result.Companies, result.Contacts, result.Leads, result.Deals, result.Tasks, result.Elapsed);
        return 0;
    }

    using (var scope = web.Services.CreateScope())
    {
        // A fresh clone runs straight away: migrate, and fill an empty database with the demo year.
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        if (config.GetValue("Database:SeedDemoData", true) && !await seeder.HasDataAsync()) await seeder.SeedAsync(reset: false);
    }

    if (knownProxies.Count > 0) web.UseForwardedHeaders();
    web.UseMiddleware<RequestSizeGuard>(maxBody);
    web.UseSerilogRequestLogging(o =>
    {
        // This host's logger, not the static Log.Logger, so several hosts can run in one process (integration tests).
        o.Logger = web.Services.GetRequiredService<Serilog.ILogger>();
        o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms for {UserName}";
        o.EnrichDiagnosticContext = (diagnostics, http) =>
        {
            diagnostics.Set("UserName", http.User.FindFirst("unique_name")?.Value ?? "anonymous");
            diagnostics.Set("UserId", http.User.FindFirst("sub")?.Value);
            diagnostics.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
        };
        o.GetLevel = (ctx, _, ex) => ex is not null || ctx.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information;
    });
    web.UseExceptionHandler();
    web.UseStatusCodePages();
    web.UseSecurityHeaders();
    if (!web.Environment.IsDevelopment())
    {
        web.UseHsts();
        web.UseHttpsRedirection();
    }

    if (web.Environment.IsDevelopment())
    {
        web.UseSwagger();
        web.UseSwaggerUI(o => { o.SwaggerEndpoint("/swagger/v1/swagger.json", "NexCRM Lite API v1"); o.DocumentTitle = "NexCRM Lite API"; });
    }

    web.UseDefaultFiles();
    web.UseStaticFiles();
    web.UseRouting();
    web.UseCors();
    web.UseAuthentication();
    web.UseRateLimiter();
    web.UseAuthorization();

    web.MapControllers();
    web.MapHealthChecks("/health").AllowAnonymous();
    web.Map("/api/{**rest}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found")).AllowAnonymous();
    web.MapFallbackToFile("index.html").AllowAnonymous();

    await web.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "NexCRM Lite API terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program
{
    public const string ExportRateLimit = "export";
}
