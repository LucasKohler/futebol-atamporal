using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FutebolAtemporal.Application.Abstractions;
using FutebolAtemporal.Application.Auth;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _configuration = configuration;
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<(bool Succeeded, string? Error)> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (!PhoneNormalizer.TryNormalize(request.Phone, out var phone, out var phoneError))
            return (false, phoneError);

        var name = request.DisplayName?.Trim() ?? string.Empty;
        if (name.Length < 2)
            return (false, "Informe o nome.");

        if (string.IsNullOrEmpty(request.Password))
            return (false, "Informe a senha.");

        if (await _userManager.FindByNameAsync(phone) is not null)
            return (false, "Já existe uma conta com este celular.");

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            DisplayName = name,
            IsActive = true
        };

        // A política de senha é a já configurada no Identity (8+, maiúscula, minúscula e dígito).
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return (false, string.Join(" ", result.Errors.Select(e => e.Description)));

        var profile = new PlayerProfile { Id = Guid.NewGuid(), UserId = user.Id, Phone = phone };
        _db.PlayerProfiles.Add(profile);

        var defaultGroup = await _db.FootballGroups.OrderBy(g => g.Id).FirstOrDefaultAsync(ct);
        if (defaultGroup is not null)
        {
            _db.GroupMemberships.Add(new GroupMembership
            {
                Id = Guid.NewGuid(),
                GroupId = defaultGroup.Id,
                UserId = user.Id,
                Role = GroupRole.Player,
                Status = MembershipStatus.Active
            });
        }

        await _db.SaveChangesAsync(ct);
        await _signInManager.SignInAsync(user, isPersistent: true);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error, string? Code)> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (!PhoneNormalizer.TryNormalize(request.Phone, out var phone, out var phoneError))
            return (false, phoneError, "invalid_phone");

        // Mensagem única: não revela se o celular existe, se a conta não tem senha ou se a senha está errada.
        const string invalid = "Celular ou senha inválidos.";
        if (string.IsNullOrEmpty(request.Password))
            return (false, invalid, "invalid_credentials");

        var user = await _userManager.FindByNameAsync(phone);
        if (user is null || !user.IsActive)
            return (false, invalid, "invalid_credentials");

        // Conta tentativas erradas: 5 seguidas bloqueiam por 5 minutos (configuração do Identity).
        var check = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return (false, "Conta bloqueada por 5 minutos após tentativas erradas. Tente mais tarde ou peça reset ao admin.", "locked_out");
        if (!check.Succeeded)
            return (false, invalid, "invalid_credentials");

        await _signInManager.SignInAsync(user, isPersistent: true);
        return (true, null, null);
    }

    private static readonly Guid SeedAdminId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task<(bool Succeeded, int Status, string? Error, string? Code)> SetupFirstAdminAsync(AdminSetupRequest request, CancellationToken ct = default)
    {
        // Código de uso único vindo do servidor (Seed__AdminSetupCode). Sem código configurado, o fluxo não existe.
        var expected = _configuration["Seed:AdminSetupCode"];
        // Código curto seria adivinhável (o endpoint não conta tentativas): exige 20+ caracteres.
        if (string.IsNullOrEmpty(expected) || expected.Length < 20)
            return (false, 404, "Configuração inicial indisponível.", "setup_disabled");

        var admin = await _userManager.FindByIdAsync(SeedAdminId.ToString());
        if (admin is null)
            return (false, 404, "Configuração inicial indisponível.", "setup_disabled");
        // Consumido para sempre: vale a senha existente ou o registro de uso, mesmo se a senha for removida depois.
        var consumed = await _db.AuditEntries.AsNoTracking()
            .AnyAsync(e => e.Action == "auth.first_admin_setup" && e.EntityId == admin.Id, ct);
        if (consumed || await _userManager.HasPasswordAsync(admin))
            return (false, 409, "O primeiro admin já foi configurado.", "setup_already_done");

        var given = Encoding.UTF8.GetBytes(request.SetupCode ?? string.Empty);
        if (!CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(expected)))
            return (false, 401, "Código de configuração inválido.", "invalid_setup_code");

        if (!PhoneNormalizer.TryNormalize(request.Phone, out var phone, out _) || !string.Equals(phone, admin.UserName, StringComparison.Ordinal))
            return (false, 400, "Celular diferente do configurado para o admin.", "admin_phone_mismatch");
        if (string.IsNullOrEmpty(request.Password))
            return (false, 400, "Informe a senha.", "password_required");

        var added = await _userManager.AddPasswordAsync(admin, request.Password);
        if (!added.Succeeded)
            return (false, 400, string.Join(" ", added.Errors.Select(e => e.Description)), "weak_password");

        // Depois daqui o admin tem senha e o código deixa de valer para sempre.
        var groupId = await _db.GroupMemberships.AsNoTracking()
            .Where(m => m.UserId == admin.Id && m.Role == GroupRole.Admin).Select(m => m.GroupId).FirstOrDefaultAsync(ct);
        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            ActorUserId = admin.Id,
            Action = "auth.first_admin_setup",
            EntityType = "User",
            EntityId = admin.Id,
            PayloadJson = "{}"
        });
        await _db.SaveChangesAsync(ct);
        await _signInManager.SignInAsync(admin, isPersistent: true);
        return (true, 200, null, null);
    }

    /// <summary>Validade máxima do código de recuperação (decisão técnica: o produto pediu "prazo curto").</summary>
    public static readonly TimeSpan MaxRecoveryWindow = TimeSpan.FromMinutes(10);

    public async Task<(bool Succeeded, int Status, string? Error, string? Code)> RecoverAdminAsync(AdminRecoveryRequest request, CancellationToken ct = default)
    {
        // Operador define Seed__AdminRecoveryCode (20+ caracteres) e Seed__AdminRecoveryExpiresAt (UTC, ISO 8601)
        // no servidor. Sem os dois, ou com validade maior que 10 minutos à frente, o fluxo não existe.
        var expected = _configuration["Seed:AdminRecoveryCode"];
        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(expected) || expected.Length < 20
            || !DateTimeOffset.TryParse(_configuration["Seed:AdminRecoveryExpiresAt"], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var expiresAt)
            || expiresAt - now > MaxRecoveryWindow)
            return (false, 404, "Recuperação indisponível.", "recovery_disabled");
        if (now >= expiresAt)
            return (false, 410, "O código de recuperação expirou. Peça um novo a quem opera o servidor.", "recovery_code_expired");

        var admin = await _userManager.FindByIdAsync(SeedAdminId.ToString());
        if (admin is null)
            return (false, 404, "Recuperação indisponível.", "recovery_disabled");
        // Recuperação não substitui a configuração inicial: admin nunca configurado usa /admin-setup.
        if (!await _userManager.HasPasswordAsync(admin)
            && !await _db.AuditEntries.AsNoTracking().AnyAsync(e => e.Action == "auth.first_admin_setup" && e.EntityId == admin.Id, ct))
            return (false, 409, "O admin ainda não foi configurado. Use a configuração inicial.", "setup_pending");

        var given = Encoding.UTF8.GetBytes(request.RecoveryCode ?? string.Empty);
        if (!CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(expected)))
            return (false, 401, "Código de recuperação inválido.", "invalid_recovery_code");

        // Uso único: guarda só o hash do código consumido, nunca o código.
        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        var marker = $"{{\"codeHash\":\"{codeHash}\"}}";
        if (await _db.AuditEntries.AsNoTracking().AnyAsync(e => e.Action == "auth.admin_recovery" && e.PayloadJson == marker, ct))
            return (false, 409, "Este código de recuperação já foi usado.", "recovery_code_used");

        if (!PhoneNormalizer.TryNormalize(request.Phone, out var phone, out _) || !string.Equals(phone, admin.UserName, StringComparison.Ordinal))
            return (false, 400, "Celular diferente do configurado para o admin.", "admin_phone_mismatch");
        if (string.IsNullOrEmpty(request.NewPassword))
            return (false, 400, "Informe a nova senha.", "password_required");

        // Valida a senha antes de remover a atual, para não deixar o admin sem senha.
        foreach (var validator in _userManager.PasswordValidators)
        {
            var check = await validator.ValidateAsync(_userManager, admin, request.NewPassword);
            if (!check.Succeeded)
                return (false, 400, string.Join(" ", check.Errors.Select(e => e.Description)), "weak_password");
        }

        // Tudo numa transação: registro de consumo, troca de senha, novo security stamp e desbloqueio.
        // Se qualquer passo falhar, nada fica gravado e o código continua sem uso.
        // Duas requisições simultâneas: a segunda esbarra no ConcurrencyStamp do usuário e é desfeita.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var groupId = await _db.GroupMemberships.AsNoTracking()
                .Where(m => m.UserId == admin.Id && m.Role == GroupRole.Admin).Select(m => m.GroupId).FirstOrDefaultAsync(ct);
            _db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                ActorUserId = admin.Id,
                Action = "auth.admin_recovery",
                EntityType = "User",
                EntityId = admin.Id,
                PayloadJson = marker
            });
            await _db.SaveChangesAsync(ct);

            if (await _userManager.HasPasswordAsync(admin))
                Ensure(await _userManager.RemovePasswordAsync(admin));
            Ensure(await _userManager.AddPasswordAsync(admin, request.NewPassword));
            Ensure(await _userManager.UpdateSecurityStampAsync(admin));
            Ensure(await _userManager.ResetAccessFailedCountAsync(admin));
            Ensure(await _userManager.SetLockoutEndDateAsync(admin, null));

            // O código pode ter vencido durante a operação.
            if (DateTimeOffset.UtcNow >= expiresAt)
            {
                await tx.RollbackAsync(ct);
                return (false, 410, "O código de recuperação expirou. Peça um novo a quem opera o servidor.", "recovery_code_expired");
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is RecoveryWriteFailed or DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return (false, 409, "A recuperação não foi concluída (outra tentativa ao mesmo tempo ou falha de gravação). Nada foi alterado; tente de novo.", "recovery_conflict");
        }

        await _signInManager.SignInAsync(admin, isPersistent: true);
        return (true, 200, null, null);
    }

    private sealed class RecoveryWriteFailed : Exception { }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new RecoveryWriteFailed();
    }

    public async Task AdminResetPasswordAsync(Guid adminUserId, Guid groupId, Guid playerProfileId, AdminResetPasswordRequest request, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, adminUserId, groupId, ct, adminOnly: true);
        if (string.IsNullOrEmpty(request.NewPassword))
            throw new DomainException("Informe a nova senha.");

        var profile = await _db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerProfileId, ct)
            ?? throw new NotFoundException("Jogador não encontrado.");
        var inGroup = await _db.GroupMemberships.AsNoTracking()
            .AnyAsync(m => m.GroupId == groupId && m.UserId == profile.UserId, ct);
        if (!inGroup)
            throw new NotFoundException("Jogador não encontrado.");

        var user = await _userManager.FindByIdAsync(profile.UserId.ToString())
            ?? throw new NotFoundException("Jogador não encontrado.");

        // Remove e define: funciona também para conta sem senha. Troca o security stamp (encerra sessões).
        if (await _userManager.HasPasswordAsync(user))
        {
            var removed = await _userManager.RemovePasswordAsync(user);
            if (!removed.Succeeded)
                throw new DomainException(string.Join(" ", removed.Errors.Select(e => e.Description)));
        }
        var added = await _userManager.AddPasswordAsync(user, request.NewPassword);
        if (!added.Succeeded)
            throw new DomainException(string.Join(" ", added.Errors.Select(e => e.Description)));
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            ActorUserId = adminUserId,
            Action = "auth.password_reset_by_admin",
            EntityType = nameof(PlayerProfile),
            EntityId = playerProfileId,
            PayloadJson = "{}" // nunca registrar a senha
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _signInManager.SignOutAsync();
    }

    public async Task<UserDto?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return null;

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return null;

        var profile = await _db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var memberships = await _db.GroupMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(_db.FootballGroups, m => m.GroupId, g => g.Id, (m, g) => new MembershipDto(
                g.Id, g.Name, g.Slug, m.Role.ToString(), m.Status.ToString()))
            .ToListAsync(ct);

        var phone = user.UserName ?? profile?.Phone ?? string.Empty;
        return new UserDto(
            user.Id,
            phone,
            user.DisplayName,
            user.Nickname,
            PlayerDisplayName.Effective(user.DisplayName, user.Nickname),
            profile?.Id,
            memberships);
    }
}
