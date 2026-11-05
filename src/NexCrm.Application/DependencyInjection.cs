using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NexCrm.Application.Common;
using NexCrm.Application.Features.Auth;
using NexCrm.Application.Features.Companies;
using NexCrm.Application.Features.Contacts;
using NexCrm.Application.Features.Dashboard;
using NexCrm.Application.Features.Deals;
using NexCrm.Application.Features.Leads;
using NexCrm.Application.Features.Lookups;
using NexCrm.Application.Features.Tasks;
using NexCrm.Domain.Entities;

namespace NexCrm.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddMemoryCache();
        services.AddSingleton<LoginThrottle>();
        services.AddScoped<AuditTrail>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<AuthService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<ContactService>();
        services.AddScoped<LeadService>();
        services.AddScoped<DealService>();
        services.AddScoped<TaskService>();
        services.AddScoped<DashboardService>();
        services.AddSingleton(sp => new CachedDashboard(async ct =>
        {
            // The cache outlives requests, so each computation gets its own scope (and DbContext).
            using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DashboardService>().GetAsync(ct);
        }, sp.GetRequiredService<IClock>()));
        services.AddScoped<LookupService>();
        return services;
    }
}
