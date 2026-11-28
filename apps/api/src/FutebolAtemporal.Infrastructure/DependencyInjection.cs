using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Infrastructure.Notifications;
using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Infrastructure.MercadoPago;
using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using FutebolAtemporal.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
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
                options.User.RequireUniqueEmail = false;
                // Decisão do Lucas: bloqueio após 5 tentativas erradas, por 5 minutos.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "fa_auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Sem renovação: só login/cadastro/setup/recuperação emitem fa_auth (fluxos sob o authLock do app).
            // Uma resposta comum atrasada não pode reinstalar a sessão de outra conta.
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            // Sessão no servidor (fa_sid) + carimbo de segurança conferidos a cada requisição, sem renovar o cookie.
            // Substitui o SecurityStampValidator do Identity, que renovaria o cookie a cada validação.
            options.Events.OnSigningIn = SessionCookieEvents.OnSigningIn;
            options.Events.OnValidatePrincipal = SessionCookieEvents.OnValidatePrincipal;
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

        // Formato de cookie versionado: o ticket v2 é cifrado com outro propósito do Data Protection.
        // Uma versão antiga do app (rollback ou réplica antiga com as mesmas chaves) não consegue ler um
        // cookie v2 — portanto não aceita um fa_sid que a versão nova revogou. E a versão nova não lê
        // cookies v1 (sem fa_sid). Ver SessionCookieEvents.TicketPurpose.
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IDataProtectionProvider>((options, dataProtection) =>
                options.TicketDataFormat = new TicketDataFormat(dataProtection.CreateProtector(SessionCookieEvents.TicketPurpose)));

        services.AddHttpContextAccessor();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IGroupQueryService, GroupQueryService>();
        services.AddScoped<Application.Matches.IMatchService, MatchService>();
        services.AddScoped<Application.Matches.IRegistrationService, RegistrationService>();
        services.AddScoped<Application.Memberships.IMembershipService, MembershipService>();
        services.AddScoped<Application.Draws.IDrawService, DrawService>();
        services.AddScoped<Application.Results.IResultService, ResultService>();
        services.AddScoped<Application.Results.IStatsService, StatsService>();
        services.AddScoped<Application.Results.IRoundResultService, RoundResultService>();

        services.AddScoped<IPixPaymentService, PixPaymentService>();
        services.AddSingleton<SimulatedMercadoPagoStore>();
        services.AddSingleton(sp => MercadoPagoOptions.Resolve(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<IHostEnvironment>()));

        services.AddHttpClient<MercadoPagoHttpClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<MercadoPagoOptions>();
            client.BaseAddress = new Uri(opts.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<SimulatedMercadoPagoClient>();

        services.AddSingleton(sp => WebPushOptions.Resolve(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetService<ILoggerFactory>()?.CreateLogger("WebPush")));
        services.AddSingleton<SimulatedWebPushStore>();
        services.AddSingleton<NotificationWakeupSignal>();
        services.AddHttpClient<LiveWebPushSender>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            // Checagem na conexão de saída, depois do DNS: só serviço de push permitido, porta 443 e IP público.
            // Fecha o caminho para rede interna mesmo se o DNS de um host permitido apontar para IP privado.
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                if (context.DnsEndPoint.Port != 443 || !NotificationPolicies.IsAllowedPushHost(host))
                    throw new HttpRequestException("Destino de push não autorizado.");

                var addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct);
                var target = addresses.FirstOrDefault(NotificationPolicies.IsPublicAddress)
                    ?? throw new HttpRequestException("Destino de push resolve para endereço não público.");

                var socket = new System.Net.Sockets.Socket(target.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new System.Net.IPEndPoint(target, 443), ct);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        })
        // A URL da inscrição identifica o aparelho (RFC 8030 §8.3): não registrar requisições do cliente de push.
        .RemoveAllLoggers();
        services.AddScoped<SimulatedWebPushSender>();
        services.AddScoped<IWebPushSender>(sp =>
        {
            var opts = sp.GetRequiredService<WebPushOptions>();
            return opts.Mode == WebPushMode.Live
                ? sp.GetRequiredService<LiveWebPushSender>()
                : sp.GetRequiredService<SimulatedWebPushSender>();
        });
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddScoped<INotificationTriggerService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<INotificationDispatchRunner, NotificationDispatchRunner>();
        services.AddHostedService<NotificationDispatcher>();
        services.AddScoped<IMercadoPagoClient>(sp =>
        {
            var opts = sp.GetRequiredService<MercadoPagoOptions>();
            return opts.ShouldUseSimulator
                ? sp.GetRequiredService<SimulatedMercadoPagoClient>()
                : sp.GetRequiredService<MercadoPagoHttpClient>();
        });
        services.AddScoped<ISimulatedMercadoPagoControl>(sp => sp.GetRequiredService<SimulatedMercadoPagoClient>());

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

        var seedGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var seedAdminId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var seedProfileId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        if (!await db.FootballGroups.AnyAsync(g => g.Id == seedGroupId) && !await db.FootballGroups.AnyAsync())
        {
            db.FootballGroups.Add(new FootballGroup
            {
                Id = seedGroupId,
                Name = "Futebol Atemporal",
                Slug = "futebol-atemporal",
                TimeZoneId = "America/Sao_Paulo",
                SettingsJson = """{"defaultPlayerLimit":14,"teamCount":2,"defaultMonthlyFee":150}"""
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded default football group {GroupId}", seedGroupId);
        }

        if (!PhoneNormalizer.TryNormalize(configuration["Seed:AdminPhone"] ?? "11999999999", out var adminPhone, out var phoneError))
            throw new InvalidOperationException($"Seed:AdminPhone inválido: {phoneError}");

        var admin = await userManager.FindByIdAsync(seedAdminId.ToString())
            ?? await userManager.FindByNameAsync(adminPhone);

        if (admin is null)
        {
            var legacyEmail = configuration["Seed:AdminEmail"] ?? "admin@futebolatemporal.local";
            admin = await userManager.FindByEmailAsync(legacyEmail);
        }

        if (admin is null)
            admin = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == seedAdminId);

        if (admin is not null && db.Entry(admin).State == EntityState.Detached)
            db.Attach(admin);

        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = seedAdminId,
                UserName = adminPhone,
                PhoneNumber = adminPhone,
                PhoneNumberConfirmed = true,
                DisplayName = "Administrador",
                IsActive = true
            };
            var created = await userManager.CreateAsync(admin);
            if (!created.Succeeded)
            {
                admin = await userManager.FindByIdAsync(seedAdminId.ToString())
                    ?? await userManager.FindByNameAsync(adminPhone);
                if (admin is null)
                    throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
                logger.LogInformation("Admin already existed; reused {UserId}", admin.Id);
            }
            else
            {
                logger.LogInformation("Seeded admin user {Phone}", adminPhone);
            }
        }

        if (!string.Equals(admin.UserName, adminPhone, StringComparison.Ordinal)
            || !string.Equals(admin.PhoneNumber, adminPhone, StringComparison.Ordinal)
            || admin.Email is not null)
        {
            admin.UserName = adminPhone;
            admin.NormalizedUserName = userManager.NormalizeName(adminPhone);
            admin.PhoneNumber = adminPhone;
            admin.PhoneNumberConfirmed = true;
            admin.Email = null;
            admin.NormalizedEmail = null;
            var updated = await userManager.UpdateAsync(admin);
            if (!updated.Succeeded)
                throw new InvalidOperationException(string.Join("; ", updated.Errors.Select(e => e.Description)));
            logger.LogInformation("Migrated admin login to phone {Phone}", adminPhone);
        }

        var profile = await db.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == admin.Id)
            ?? await db.PlayerProfiles.FirstOrDefaultAsync(p => p.Id == seedProfileId);
        if (profile is null)
        {
            db.PlayerProfiles.Add(new PlayerProfile
            {
                Id = seedProfileId,
                UserId = admin.Id,
                Phone = adminPhone,
                SkillRating = 7.5m
            });
        }
        else
        {
            profile.Phone = adminPhone;
        }

        var group = await db.FootballGroups.OrderBy(g => g.Id).FirstAsync();
        var membership = await db.GroupMemberships
            .FirstOrDefaultAsync(m => m.GroupId == group.Id && m.UserId == admin.Id);
        if (membership is null)
        {
            db.GroupMemberships.Add(new GroupMembership
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                UserId = admin.Id,
                Role = GroupRole.Admin,
                Status = MembershipStatus.Active
            });
        }
        else
        {
            membership.Role = GroupRole.Admin;
            membership.Status = MembershipStatus.Active;
        }

        await db.SaveChangesAsync();
        if (!await userManager.GetLockoutEnabledAsync(admin))
            await userManager.SetLockoutEnabledAsync(admin, true);
        if (!await userManager.HasPasswordAsync(admin))
            logger.LogInformation("Admin ainda sem senha: crie-a uma vez em POST /api/auth/admin-setup com o código de configuração.");
    }
}
