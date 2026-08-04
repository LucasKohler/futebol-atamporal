using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using FutebolAtemporal.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FutebolAtemporal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var useSqlite = configuration.GetValue("Database:UseSqlite", false);
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=futebol_atemporal;Username=futebol;Password=futebol";

        services.AddDbContext<AppDbContext>(options =>
        {
            if (useSqlite)
                options.UseSqlite(connectionString);
            else
                options.UseNpgsql(connectionString);
        });

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 8;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "fa_auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddHttpContextAccessor();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IGroupQueryService, GroupQueryService>();
        services.AddScoped<Application.Matches.IMatchService, MatchService>();
        services.AddScoped<Application.Matches.IRegistrationService, RegistrationService>();
        services.AddScoped<Application.Memberships.IMembershipService, MembershipService>();
        services.AddScoped<Application.Draws.IDrawService, DrawService>();
        services.AddScoped<Application.Results.IResultService, ResultService>();
        services.AddScoped<Application.Results.IStatsService, StatsService>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInit");

        await db.Database.MigrateAsync();

        if (!await db.FootballGroups.AnyAsync())
        {
            var group = new FootballGroup
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Futebol Atemporal",
                Slug = "futebol-atemporal",
                TimeZoneId = "America/Sao_Paulo",
                SettingsJson = """{"defaultPlayerLimit":14,"teamCount":2,"defaultMonthlyFee":150}"""
            };
            db.FootballGroups.Add(group);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded default football group {GroupId}", group.Id);
        }

        var adminEmail = configuration["Seed:AdminEmail"] ?? "admin@futebolatemporal.local";
        var adminPassword = configuration["Seed:AdminPassword"] ?? "Admin123!";
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "Administrador",
                EmailConfirmed = true,
                IsActive = true
            };
            var created = await userManager.CreateAsync(admin, adminPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

            db.PlayerProfiles.Add(new PlayerProfile
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                UserId = admin.Id,
                SkillRating = 7.5m
            });

            var group = await db.FootballGroups.OrderBy(g => g.Id).FirstAsync();
            db.GroupMemberships.Add(new GroupMembership
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                UserId = admin.Id,
                Role = GroupRole.Admin,
                Status = MembershipStatus.Active
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded admin user {Email}", adminEmail);
        }
    }
}
