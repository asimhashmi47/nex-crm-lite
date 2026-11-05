using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexCrm.Application.Common;
using NexCrm.Application.Features.Auth;
using NexCrm.Infrastructure.Diagnostics;
using NexCrm.Infrastructure.Persistence;
using NexCrm.Infrastructure.Seeding;
using NexCrm.Infrastructure.Services;

namespace NexCrm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Default") ?? "Data Source=App_Data/nexcrm.db";
        var diagnostics = config.GetValue("Diagnostics:Enabled", false);
        if (diagnostics) services.AddSingleton<QueryStats>();
        services.AddDbContext<AppDbContext>((sp, o) =>
        {
            o.UseSqlite(connection);
            if (diagnostics) o.AddInterceptors(sp.GetRequiredService<QueryStats>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<AppOptions>(config.GetSection("App"));
        services.Configure<LoginProtectionOptions>(config.GetSection("RateLimiting"));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddScoped<DemoDataSeeder>();
        return services;
    }
}
