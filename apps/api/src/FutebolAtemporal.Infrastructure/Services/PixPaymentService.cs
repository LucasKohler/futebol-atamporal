using System.Text.Json;
using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.MercadoPago;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class PixPaymentService : IPixPaymentService
{
    public const string WebhookPath = "/api/webhooks/mercadopago";

    private readonly AppDbContext _db;
    private readonly IMercadoPagoClient _mp;
    private readonly ISimulatedMercadoPagoControl? _simControl;
    private readonly MercadoPagoOptions _options;
    private readonly ILogger<PixPaymentService> _logger;

    public PixPaymentService(
        AppDbContext db,
        IMercadoPagoClient mp,
        MercadoPagoOptions options,
        ILogger<PixPaymentService> logger,
        ISimulatedMercadoPagoControl? simControl = null)
    {
        _db = db;
        _mp = mp;
        _options = options;
        _logger = logger;
        _simControl = simControl;
    }

    public async Task<MyPaymentsDto> GetMyPaymentsAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        var (_, profile) = await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
        var settings = GroupSettings.Parse(group.SettingsJson);
        var localNow = LocalNow(group.TimeZoneId);
        var now = DateTimeOffset.UtcNow;

        var activeMonthly = await _db.MonthlyMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.PlayerProfileId == profile.Id && m.Status == MonthlyMembershipStatus.Active, ct);

        var myMemberships = await (
            from p in _db.MembershipPayments.AsNoTracking()
            join m in _db.MonthlyMemberships.AsNoTracking() on p.MonthlyMembershipId equals m.Id
            join per in _db.MembershipBillingPeriods.AsNoTracking() on p.BillingPeriodId equals per.Id
            where m.GroupId == groupId && m.PlayerProfileId == profile.Id
            orderby per.Year descending, per.Month descending
            select new { p, m, per }
        ).ToListAsync(ct);

        var myFees = await (
            from f in _db.MatchFeePayments.AsNoTracking()
            join match in _db.Matches.AsNoTracking() on f.MatchId equals match.Id
            where f.GroupId == groupId && f.PlayerProfileId == profile.Id
            select new { f, match }
        ).ToListAsync(ct);

        var feeMatchIds = myFees.Select(x => x.match.Id).ToHashSet();
        var eligibleMatches = activeMonthly is not null
            ? new List<Match>()
            : await (
                from r in _db.MatchRegistrations.AsNoTracking()
                join match in _db.Matches.AsNoTracking() on r.MatchId equals match.Id
                where match.GroupId == groupId
                    && r.PlayerProfileId == profile.Id
                    && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Participated)
                    && match.Status != MatchStatus.Cancelled
                    && match.Status != MatchStatus.Draft
                select match
            ).ToListAsync(ct);

        var membershipPaymentIds = myMemberships.Select(x => x.p.Id).ToList();
        var matchFeeIds = myFees.Select(x => x.f.Id).ToList();

        var activeCharges = await _db.PixCharges.AsNoTracking()
            .Where(c => c.GroupId == groupId
                && c.PlayerProfileId == profile.Id
                && c.Status == PixChargeStatus.Pending
                && ((c.MembershipPaymentId != null && membershipPaymentIds.Contains(c.MembershipPaymentId.Value))
                    || (c.MatchFeePaymentId != null && matchFeeIds.Contains(c.MatchFeePaymentId.Value))))
            .ToListAsync(ct);
        activeCharges = activeCharges.Where(c => c.ExpiresAt > now).OrderByDescending(c => c.CreatedAt).ToList();

        var playerName = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [profile.Id], ct))
            .GetValueOrDefault(profile.Id, "Jogador");

        var membershipDtos = myMemberships.Select(x =>
        {
            var charge = activeCharges.FirstOrDefault(c => c.MembershipPaymentId == x.p.Id);
            return new PlayerMembershipPaymentDto(
                x.p.Id, x.m.Id, x.per.Id, x.per.Label, x.per.Year, x.per.Month,
                x.p.Amount, x.p.Status.ToString(), x.p.PaymentSource.ToString(), x.p.PaidAt,
                charge is null ? null : MapChargeDto(charge, playerName, x.p.Status.ToString(), true, true, now));
        }).ToList();

        var matchDayDtos = new List<PlayerMatchFeeDto>();
        foreach (var x in myFees)
        {
            var charge = activeCharges.FirstOrDefault(c => c.MatchFeePaymentId == x.f.Id);
            matchDayDtos.Add(new PlayerMatchFeeDto(
                x.f.Id, x.match.Id,
                string.IsNullOrWhiteSpace(x.match.Title) ? $"Pelada {x.match.StartsAt:dd/MM}" : x.match.Title!,
                x.match.StartsAt, x.match.Location, x.f.Amount,
                x.f.Status.ToString(), x.f.PaymentSource.ToString(), x.f.PaidAt,
                charge is null ? null : MapChargeDto(charge, playerName, x.f.Status.ToString(), true, true, now)));
        }

        foreach (var m in eligibleMatches.Where(m => !feeMatchIds.Contains(m.Id)))
        {
            matchDayDtos.Add(new PlayerMatchFeeDto(
                null, m.Id,
                string.IsNullOrWhiteSpace(m.Title) ? $"Pelada {m.StartsAt:dd/MM}" : m.Title!,
                m.StartsAt, m.Location, settings.DefaultMatchDayFee,
                PaymentStatus.Pending.ToString(), PaymentSource.None.ToString(), null, null));
        }

        matchDayDtos = matchDayDtos.OrderByDescending(x => x.StartsAt).ToList();
        return new MyPaymentsDto(
            BuildGatewayStatus(settings),
            activeMonthly is not null,
            new PlayerPeriodDto(localNow.Year, localNow.Month, $"{localNow.Year:0000}-{localNow.Month:00}"),
            membershipDtos,
            matchDayDtos);
    }

    public async Task<PixChargeDto> CreateOrReuseMembershipPixAsync(Guid userId, Guid membershipPaymentId, CancellationToken ct = default)
    {
        var pay = await _db.MembershipPayments
            .Include(p => p.MonthlyMembership)
            .Include(p => p.BillingPeriod)
            .FirstOrDefaultAsync(p => p.Id == membershipPaymentId, ct)
            ?? throw new DomainException("Mensalidade não encontrada.");

        var groupId = pay.MonthlyMembership.GroupId;
        var (actorMembership, actorProfile) = await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);
        if (actorMembership.Role != GroupRole.Admin && actorProfile.Id != pay.MonthlyMembership.PlayerProfileId)
            throw new ForbiddenException("Você só pode gerar Pix para a sua própria mensalidade.");

        if (pay.Status == PaymentStatus.Paid)
            throw new ConflictException("Esta mensalidade já está paga.");
        if (pay.Status is PaymentStatus.Exempt or PaymentStatus.Cancelled)
            throw new DomainException("Esta mensalidade está isenta ou cancelada e não aceita pagamento via Pix.");

        PixChargePolicy.ValidateChargeAmount(pay.Amount);

        var now = DateTimeOffset.UtcNow;
        var existing = await _db.PixCharges
            .Where(c => c.MembershipPaymentId == pay.Id && c.Status == PixChargeStatus.Pending)
            .ToListAsync(ct);
        existing = existing.OrderByDescending(c => c.CreatedAt).ToList();

        var targetProfileId = pay.MonthlyMembership.PlayerProfileId;
        var playerName = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [targetProfileId], ct))
            .GetValueOrDefault(targetProfileId, "Jogador");

        var reusable = existing.FirstOrDefault(c => PixChargePolicy.IsReusable(c, now));
        if (reusable is not null)
            return MapChargeDto(reusable, playerName, pay.Status.ToString(), true, true, now);

        foreach (var exp in existing.Where(c => PixChargePolicy.IsExpired(c, now)))
        {
            exp.Status = PixChargeStatus.Expired;
            exp.UpdatedAt = now;
        }

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
        var settings = GroupSettings.Parse(group.SettingsJson);
        var expiresAt = now.AddMinutes(PixChargePolicy.ClampExpirationMinutes(settings.PixExpirationMinutes));
        var idempotencyKey = Guid.NewGuid().ToString("D");
        var description = $"Mensalidade {group.Name} - {pay.BillingPeriod.Month:00}/{pay.BillingPeriod.Year:0000}";
        var payerEmail = await ResolvePayerEmailAsync(targetProfileId, ct);

        var mpResult = await _mp.CreatePixPaymentAsync(new MpCreatePixPaymentRequest(
            pay.Amount, description, $"membership:{pay.Id:D}", expiresAt, payerEmail, playerName, idempotencyKey), ct);

        var charge = new PixCharge
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            PlayerProfileId = targetProfileId,
            Kind = PixChargeKind.Membership,
            MembershipPaymentId = pay.Id,
            Amount = pay.Amount,
            Description = description,
            MpPaymentId = mpResult.MpPaymentId,
            IdempotencyKey = idempotencyKey,
            QrCode = mpResult.QrCode,
            QrCodeBase64 = mpResult.QrCodeBase64,
            ExpiresAt = mpResult.ExpiresAt,
            Status = PixChargePolicy.MapProviderStatus(mpResult.Status),
            ProviderStatus = mpResult.Status,
            IsSimulated = mpResult.IsSimulated,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.PixCharges.Add(charge);
        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            ActorUserId = userId,
            Action = "PixChargeCreated",
            EntityType = nameof(PixCharge),
            EntityId = charge.Id,
            PayloadJson = JsonSerializer.Serialize(new { kind = "Membership", membershipPaymentId = pay.Id, mpPaymentId = charge.MpPaymentId, amount = charge.Amount, isSimulated = charge.IsSimulated })
        });
        await _db.SaveChangesAsync(ct);

        return MapChargeDto(charge, playerName, pay.Status.ToString(), true, false, now);
    }

    public async Task<PixChargeDto> CreateOrReuseMatchDayPixAsync(Guid userId, Guid matchId, CreateMatchDayPixRequest? request, CancellationToken ct = default)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct)
            ?? throw new DomainException("Partida não encontrada.");
        if (match.Status == MatchStatus.Cancelled)
            throw new DomainException("Não é possível gerar diária para uma partida cancelada.");

        var (actorMembership, actorProfile) = await AccessHelper.RequireMemberAsync(_db, userId, match.GroupId, ct);
        var isAdmin = actorMembership.Role == GroupRole.Admin;
        var targetProfileId = request?.PlayerProfileId ?? actorProfile.Id;

        if (!isAdmin && targetProfileId != actorProfile.Id)
            throw new ForbiddenException("Você só pode gerar Pix para a sua própria diária.");
        if (!isAdmin && request?.Amount is not null)
            throw new ForbiddenException("Apenas administradores podem alterar o valor da diária.");

        var targetProfile = await _db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == targetProfileId, ct)
            ?? throw new DomainException("Perfil de jogador não encontrado.");
        var isMemberOfGroup = await _db.GroupMemberships.AsNoTracking()
            .AnyAsync(gm => gm.GroupId == match.GroupId && gm.UserId == targetProfile.UserId && gm.Status == MembershipStatus.Active, ct);
        if (!isMemberOfGroup)
            throw new DomainException("Jogador não pertence a este grupo.");

        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == match.GroupId, ct);
        var settings = GroupSettings.Parse(group.SettingsJson);
        var fee = await _db.MatchFeePayments.FirstOrDefaultAsync(f => f.MatchId == matchId && f.PlayerProfileId == targetProfileId, ct);

        if (fee is null)
        {
            if (!isAdmin)
            {
                var isActiveMonthly = await _db.MonthlyMemberships.AsNoTracking().AnyAsync(
                    m => m.GroupId == match.GroupId && m.PlayerProfileId == targetProfileId && m.Status == MonthlyMembershipStatus.Active, ct);
                if (isActiveMonthly)
                    throw new ConflictException("Mensalistas ativos não pagam diária avulsa.");

                var hasReg = await _db.MatchRegistrations.AsNoTracking().AnyAsync(
                    r => r.MatchId == matchId && r.PlayerProfileId == targetProfileId
                        && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Participated), ct);
                if (!hasReg)
                    throw new DomainException("Você precisa estar confirmado na partida para gerar o Pix da diária.");
            }

            fee = new MatchFeePayment
            {
                Id = Guid.NewGuid(),
                GroupId = match.GroupId,
                MatchId = matchId,
                PlayerProfileId = targetProfileId,
                Amount = request?.Amount ?? settings.DefaultMatchDayFee,
                Status = PaymentStatus.Pending,
                PaymentSource = PaymentSource.None,
                RecordedByUserId = userId
            };
            _db.MatchFeePayments.Add(fee);
            await _db.SaveChangesAsync(ct);
        }

        if (fee.Status == PaymentStatus.Paid)
            throw new ConflictException("Esta diária já está paga.");
        if (fee.Status is PaymentStatus.Exempt or PaymentStatus.Cancelled)
            throw new DomainException("Esta diária está isenta ou cancelada e não aceita pagamento via Pix.");

        var requestedAmount = request?.Amount ?? fee.Amount;
        PixChargePolicy.ValidateChargeAmount(requestedAmount);

        var now = DateTimeOffset.UtcNow;
        var existing = await LoadOutstandingMatchDayChargesAsync(fee.Id, ct);
        var amountChanged = requestedAmount != fee.Amount;
        InvalidateMatchDayCharges(existing.Where(c => amountChanged || c.Amount != requestedAmount), now);
        fee.Amount = requestedAmount;

        var playerName = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [targetProfileId], ct))
            .GetValueOrDefault(targetProfileId, "Jogador");

        var reusable = existing.OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault(c => PixChargePolicy.IsReusable(c, now));
        if (reusable is not null)
        {
            await _db.SaveChangesAsync(ct);
            return MapChargeDto(reusable, playerName, fee.Status.ToString(), true, true, now);
        }

        foreach (var exp in existing.Where(c => PixChargePolicy.IsExpired(c, now)))
        {
            exp.Status = PixChargeStatus.Expired;
            exp.UpdatedAt = now;
        }

        var expiresAt = now.AddMinutes(PixChargePolicy.ClampExpirationMinutes(settings.PixExpirationMinutes));
        var idempotencyKey = Guid.NewGuid().ToString("D");
        var localDate = ConvertToGroupTime(match.StartsAt, group.TimeZoneId);
        var description = $"Diária {group.Name} - {localDate:dd/MM/yyyy}";
        var payerEmail = await ResolvePayerEmailAsync(targetProfileId, ct);

        var mpResult = await _mp.CreatePixPaymentAsync(new MpCreatePixPaymentRequest(
            fee.Amount, description, $"matchday:{fee.Id:D}", expiresAt, payerEmail, playerName, idempotencyKey), ct);

        var charge = new PixCharge
        {
            Id = Guid.NewGuid(),
            GroupId = match.GroupId,
            PlayerProfileId = targetProfileId,
            Kind = PixChargeKind.MatchDay,
            MatchFeePaymentId = fee.Id,
            MatchId = match.Id,
            Amount = fee.Amount,
            Description = description,
            MpPaymentId = mpResult.MpPaymentId,
            IdempotencyKey = idempotencyKey,
            QrCode = mpResult.QrCode,
            QrCodeBase64 = mpResult.QrCodeBase64,
            ExpiresAt = mpResult.ExpiresAt,
            Status = PixChargePolicy.MapProviderStatus(mpResult.Status),
            ProviderStatus = mpResult.Status,
            IsSimulated = mpResult.IsSimulated,
            CreatedAt = now,
            UpdatedAt = now
        };

        // A criação também deve detectar alterações concorrentes na diária.
        TouchMatchFee(fee, now);
        _db.PixCharges.Add(charge);
        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = match.GroupId,
            ActorUserId = userId,
            Action = "PixChargeCreated",
            EntityType = nameof(PixCharge),
            EntityId = charge.Id,
            PayloadJson = JsonSerializer.Serialize(new { kind = "MatchDay", matchFeePaymentId = fee.Id, matchId = match.Id, mpPaymentId = charge.MpPaymentId, amount = charge.Amount, isSimulated = charge.IsSimulated })
        });
        await _db.SaveChangesAsync(ct);

        return MapChargeDto(charge, playerName, fee.Status.ToString(), true, false, now);
    }

    public async Task<PixChargeDto> GetPixChargeAsync(Guid userId, Guid pixChargeId, CancellationToken ct = default)
    {
        var charge = await _db.PixCharges.FirstOrDefaultAsync(c => c.Id == pixChargeId, ct)
            ?? throw new DomainException("Cobrança Pix não encontrada.");

        var (actorMembership, actorProfile) = await AccessHelper.RequireMemberAsync(_db, userId, charge.GroupId, ct);
        if (actorMembership.Role != GroupRole.Admin && actorProfile.Id != charge.PlayerProfileId)
            throw new ForbiddenException("Você não tem permissão para visualizar esta cobrança Pix.");

        var now = DateTimeOffset.UtcNow;
        if (PixChargePolicy.IsExpired(charge, now))
        {
            charge.Status = PixChargeStatus.Expired;
            charge.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        var obligationStatus = await ResolveObligationStatusAsync(charge, ct);
        var playerName = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [charge.PlayerProfileId], ct))
            .GetValueOrDefault(charge.PlayerProfileId, "Jogador");

        return MapChargeDto(charge, playerName, obligationStatus.ToString(), true, false, now);
    }

    public async Task<PixVerifyResultDto> VerifyPixChargeAsync(Guid userId, Guid pixChargeId, CancellationToken ct = default)
    {
        var charge = await _db.PixCharges.FirstOrDefaultAsync(c => c.Id == pixChargeId, ct)
            ?? throw new DomainException("Cobrança Pix não encontrada.");

        var (actorMembership, actorProfile) = await AccessHelper.RequireMemberAsync(_db, userId, charge.GroupId, ct);
        if (actorMembership.Role != GroupRole.Admin && actorProfile.Id != charge.PlayerProfileId)
            throw new ForbiddenException("Você não tem permissão para verificar esta cobrança Pix.");

        var mpStatus = await _mp.GetPaymentAsync(charge.MpPaymentId, ct);
        var (wasUpdated, obligationStatus) = await ApplyProviderStatusAsync(charge, mpStatus, userId, "verify", ct);

        var playerName = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [charge.PlayerProfileId], ct))
            .GetValueOrDefault(charge.PlayerProfileId, "Jogador");
        var dto = MapChargeDto(charge, playerName, obligationStatus.ToString(), true, false, DateTimeOffset.UtcNow);

        var message = obligationStatus == PaymentStatus.Paid
            ? (wasUpdated ? "Pagamento confirmado!" : "Pagamento já estava confirmado.")
            : charge.Status == PixChargeStatus.Expired
                ? "Este código Pix expirou. Gere um novo código para pagar."
                : charge.Status == PixChargeStatus.Cancelled
                    ? "Esta cobrança Pix foi cancelada."
                    : "Pagamento ainda pendente no Mercado Pago. Tente novamente em instantes.";

        return new PixVerifyResultDto(dto, obligationStatus.ToString(), wasUpdated, message);
    }

    public async Task<PixVerifyResultDto> SimulateApproveAsync(Guid userId, Guid pixChargeId, CancellationToken ct = default)
    {
        var charge = await _db.PixCharges.FirstOrDefaultAsync(c => c.Id == pixChargeId, ct)
            ?? throw new DomainException("Cobrança Pix não encontrada.");

        await AccessHelper.RequireMemberAsync(_db, userId, charge.GroupId, ct, adminOnly: true);

        if (!_mp.IsSimulated || _simControl is null || !charge.IsSimulated)
            throw new DomainException("A simulação de pagamento só está disponível em modo simulador de desenvolvimento.");

        _simControl.SetStatus(charge.MpPaymentId, "approved", DateTimeOffset.UtcNow);
        var mpStatus = await _mp.GetPaymentAsync(charge.MpPaymentId, ct);
        var (wasUpdated, obligationStatus) = await ApplyProviderStatusAsync(charge, mpStatus, userId, "simulator", ct);

        var playerName = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [charge.PlayerProfileId], ct))
            .GetValueOrDefault(charge.PlayerProfileId, "Jogador");
        var dto = MapChargeDto(charge, playerName, obligationStatus.ToString(), true, false, DateTimeOffset.UtcNow);

        var message = obligationStatus == PaymentStatus.Paid
            ? (wasUpdated ? "Pagamento simulado e confirmado!" : "Pagamento já estava confirmado.")
            : "A aprovação simulada não quitou a obrigação.";
        return new PixVerifyResultDto(dto, obligationStatus.ToString(), wasUpdated, message);
    }

    public async Task<bool> ProcessWebhookAsync(string? topic, string? dataId, string? xSignature, string? xRequestId, CancellationToken ct = default)
    {
        if (!MercadoPagoSignatureValidator.Validate(xSignature, xRequestId, dataId, _options.WebhookSecret))
        {
            _logger.LogWarning("Webhook Mercado Pago rejeitado: assinatura x-signature inválida ou ausente.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(topic) && !topic.Equals("payment", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(dataId))
            return true;

        var mpStatus = await _mp.GetPaymentAsync(dataId.Trim(), ct);
        var charge = await _db.PixCharges.FirstOrDefaultAsync(c => c.MpPaymentId == mpStatus.MpPaymentId, ct);
        if (charge is null)
        {
            _logger.LogInformation("Webhook Mercado Pago recebido para mp_payment_id desconhecido ({MpPaymentId}); ignorado com 200 OK.", mpStatus.MpPaymentId);
            return true;
        }

        try
        {
            await ApplyProviderStatusAsync(charge, mpStatus, actorUserId: null, sourceTag: "webhook", ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            var current = await _db.PixCharges.FirstOrDefaultAsync(c => c.Id == charge.Id, ct);
            if (current is not null)
                await ApplyProviderStatusAsync(current, mpStatus, actorUserId: null, sourceTag: "webhook", ct);
        }

        return true;
    }

    public async Task<GatewayStatusDto> GetPaymentSettingsAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
        return BuildGatewayStatus(GroupSettings.Parse(group.SettingsJson));
    }

    public async Task<GatewayStatusDto> UpdatePaymentSettingsAsync(Guid userId, Guid groupId, UpdatePaymentSettingsRequest request, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        PixChargePolicy.ValidateSettings(request.DefaultMonthlyFee, request.DefaultMatchDayFee, request.PixExpirationMinutes);

        var group = await _db.FootballGroups.FirstAsync(g => g.Id == groupId, ct);
        var settings = GroupSettings.Parse(group.SettingsJson);
        settings.DefaultMonthlyFee = request.DefaultMonthlyFee;
        settings.DefaultMatchDayFee = request.DefaultMatchDayFee;
        settings.PixExpirationMinutes = request.PixExpirationMinutes;
        group.SettingsJson = settings.ToJson();

        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            ActorUserId = userId,
            Action = "PaymentSettingsUpdated",
            EntityType = nameof(FootballGroup),
            EntityId = groupId,
            PayloadJson = JsonSerializer.Serialize(new { settings.DefaultMonthlyFee, settings.DefaultMatchDayFee, settings.PixExpirationMinutes })
        });
        await _db.SaveChangesAsync(ct);

        return BuildGatewayStatus(settings);
    }

    public async Task<IReadOnlyList<AdminMatchFeeDto>> ListMatchFeesAsync(Guid userId, Guid groupId, Guid matchId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        _ = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct)
            ?? throw new DomainException("Partida não encontrada neste grupo.");
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
        var settings = GroupSettings.Parse(group.SettingsJson);

        var activeMonthlyProfiles = (await _db.MonthlyMemberships.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MonthlyMembershipStatus.Active)
            .Select(m => m.PlayerProfileId)
            .ToListAsync(ct)).ToHashSet();

        var regs = await _db.MatchRegistrations.AsNoTracking()
            .Where(r => r.MatchId == matchId && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.Participated))
            .ToListAsync(ct);

        var fees = await _db.MatchFeePayments.AsNoTracking()
            .Where(f => f.MatchId == matchId && f.GroupId == groupId)
            .ToListAsync(ct);
        var feeByProfile = fees.ToDictionary(f => f.PlayerProfileId);
        var regByProfile = regs.ToDictionary(r => r.PlayerProfileId);

        var targetProfileIds = regs
            .Where(r => !activeMonthlyProfiles.Contains(r.PlayerProfileId))
            .Select(r => r.PlayerProfileId)
            .Concat(fees.Select(f => f.PlayerProfileId))
            .Distinct()
            .ToList();

        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, targetProfileIds, ct);
        var feeIds = fees.Select(f => f.Id).ToList();
        var now = DateTimeOffset.UtcNow;

        var latestPixByFee = feeIds.Count == 0
            ? new Dictionary<Guid, PixChargeSummaryDto>()
            : (await _db.PixCharges.AsNoTracking()
                .Where(c => c.MatchFeePaymentId != null && feeIds.Contains(c.MatchFeePaymentId.Value))
                .ToListAsync(ct)).OrderByDescending(c => c.CreatedAt)
                .GroupBy(c => c.MatchFeePaymentId!.Value)
                .ToDictionary(g => g.Key, g => MapChargeSummary(g.First(), now));

        return targetProfileIds
            .Select(pid =>
            {
                feeByProfile.TryGetValue(pid, out var f);
                regByProfile.TryGetValue(pid, out var r);
                var priority = r?.PriorityClass.ToString()
                    ?? (activeMonthlyProfiles.Contains(pid) ? PriorityClass.Monthly.ToString() : PriorityClass.Casual.ToString());
                return new AdminMatchFeeDto(
                    f?.Id, matchId, pid, names.GetValueOrDefault(pid, "Jogador"), priority,
                    f?.Amount ?? settings.DefaultMatchDayFee,
                    (f?.Status ?? PaymentStatus.Pending).ToString(),
                    (f?.PaymentSource ?? PaymentSource.None).ToString(),
                    f?.PaidAt, f?.Notes,
                    f is null ? null : latestPixByFee.GetValueOrDefault(f.Id));
            })
            .OrderBy(x => x.PlayerName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<AdminMatchFeeDto> UpsertMatchFeeAsync(Guid userId, Guid groupId, Guid matchId, Guid playerProfileId, UpsertMatchFeeRequest request, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        _ = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId && m.GroupId == groupId, ct)
            ?? throw new DomainException("Partida não encontrada neste grupo.");
        if (!Enum.IsDefined(request.Status))
            throw new DomainException("Situação da diária inválida.");
        if (request.Amount < 0)
            throw new DomainException("O valor da diária não pode ser negativo.");

        var targetProfile = await _db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerProfileId, ct)
            ?? throw new DomainException("Jogador não encontrado.");
        var isMember = await _db.GroupMemberships.AsNoTracking()
            .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == targetProfile.UserId && gm.Status == MembershipStatus.Active, ct);
        if (!isMember)
            throw new DomainException("Jogador não pertence a este grupo.");

        var now = DateTimeOffset.UtcNow;
        var fee = await _db.MatchFeePayments.FirstOrDefaultAsync(f => f.MatchId == matchId && f.PlayerProfileId == playerProfileId, ct);
        if (fee is not null && fee.Status == PaymentStatus.Paid && fee.PaymentSource == PaymentSource.Pix)
            throw new ConflictException("Esta diária já foi quitada via Pix e não pode ser alterada manualmente.");
        if (fee is null)
        {
            fee = new MatchFeePayment
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                MatchId = matchId,
                PlayerProfileId = playerProfileId,
                CreatedAt = now
            };
            _db.MatchFeePayments.Add(fee);
        }

        else if (fee.Amount != request.Amount)
        {
            var charges = await LoadOutstandingMatchDayChargesAsync(fee.Id, ct);
            InvalidateMatchDayCharges(charges, now);
        }

        fee.Amount = request.Amount;
        fee.Status = request.Status;
        fee.Notes = request.Notes;
        fee.PaidAt = request.Status == PaymentStatus.Paid ? now : null;
        fee.PaymentSource = request.Status == PaymentStatus.Paid ? PaymentSource.Manual : PaymentSource.None;
        fee.RecordedByUserId = userId;
        TouchMatchFee(fee, now);

        await _db.SaveChangesAsync(ct);

        var name = (await AccessHelper.DisplayNamesForProfilesAsync(_db, [playerProfileId], ct))
            .GetValueOrDefault(playerProfileId, "Jogador");
        var reg = await _db.MatchRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.MatchId == matchId && r.PlayerProfileId == playerProfileId, ct);
        var latestPix = (await _db.PixCharges.AsNoTracking()
            .Where(c => c.MatchFeePaymentId == fee.Id)
            .ToListAsync(ct)).OrderByDescending(c => c.CreatedAt).FirstOrDefault();

        return new AdminMatchFeeDto(
            fee.Id, matchId, playerProfileId, name,
            (reg?.PriorityClass ?? PriorityClass.Casual).ToString(),
            fee.Amount, fee.Status.ToString(), fee.PaymentSource.ToString(),
            fee.PaidAt, fee.Notes,
            latestPix is null ? null : MapChargeSummary(latestPix, now));
    }

    public async Task<AdminPixOverviewDto> GetAdminPixOverviewAsync(
        Guid userId, Guid groupId, PixChargeStatus? status, PixChargeKind? kind, int? year, int? month, Guid? matchId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);
        var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
        var settings = GroupSettings.Parse(group.SettingsJson);
        var now = DateTimeOffset.UtcNow;

        var chargesQuery = _db.PixCharges.AsNoTracking().Where(c => c.GroupId == groupId);
        if (kind is not null) chargesQuery = chargesQuery.Where(c => c.Kind == kind);
        if (matchId is not null) chargesQuery = chargesQuery.Where(c => c.MatchId == matchId);

        var allGroupCharges = (await chargesQuery.ToListAsync(ct)).OrderByDescending(c => c.CreatedAt).Take(200).ToList();

        var memQuery = from p in _db.MembershipPayments.AsNoTracking()
                       join m in _db.MonthlyMemberships.AsNoTracking() on p.MonthlyMembershipId equals m.Id
                       join per in _db.MembershipBillingPeriods.AsNoTracking() on p.BillingPeriodId equals per.Id
                       where m.GroupId == groupId
                       select new { p, per };
        if (year is not null) memQuery = memQuery.Where(x => x.per.Year == year);
        if (month is not null) memQuery = memQuery.Where(x => x.per.Month == month);
        var memPayments = await memQuery.Select(x => x.p).ToListAsync(ct);

        var feeQuery = _db.MatchFeePayments.AsNoTracking().Where(f => f.GroupId == groupId);
        if (matchId is not null) feeQuery = feeQuery.Where(f => f.MatchId == matchId);
        var feePayments = await feeQuery.ToListAsync(ct);

        int effPending = 0, effApproved = 0, effExpired = 0, effCancelled = 0, effActive = 0;
        foreach (var c in allGroupCharges)
        {
            switch (EffectiveStatus(c, now))
            {
                case PixChargeStatus.Pending: effPending++; effActive++; break;
                case PixChargeStatus.Approved: effApproved++; break;
                case PixChargeStatus.Expired: effExpired++; break;
                case PixChargeStatus.Cancelled: effCancelled++; break;
            }
        }

        var allObligations = memPayments.Select(p => (p.Status, p.PaymentSource, p.Amount))
            .Concat(feePayments.Select(f => (f.Status, f.PaymentSource, f.Amount)))
            .ToList();

        var paidByPixCount = allObligations.Count(o => o.Status == PaymentStatus.Paid && o.PaymentSource == PaymentSource.Pix);
        var paidManualCount = allObligations.Count(o => o.Status == PaymentStatus.Paid && o.PaymentSource != PaymentSource.Pix);
        var exemptCount = allObligations.Count(o => o.Status == PaymentStatus.Exempt);
        var receivedPix = allObligations.Where(o => o.Status == PaymentStatus.Paid && o.PaymentSource == PaymentSource.Pix).Sum(o => o.Amount);
        var receivedManual = allObligations.Where(o => o.Status == PaymentStatus.Paid && o.PaymentSource != PaymentSource.Pix).Sum(o => o.Amount);
        var pendingAmount = allObligations.Where(o => o.Status is PaymentStatus.Pending or PaymentStatus.Overdue).Sum(o => o.Amount);

        var filteredCharges = status is null
            ? allGroupCharges
            : allGroupCharges.Where(c => EffectiveStatus(c, now) == status.Value).ToList();

        var names = await AccessHelper.DisplayNamesForProfilesAsync(_db, filteredCharges.Select(c => c.PlayerProfileId), ct);
        var memStatusMap = memPayments.ToDictionary(p => p.Id, p => p.Status.ToString());
        var feeStatusMap = feePayments.ToDictionary(f => f.Id, f => f.Status.ToString());

        var chargeDtos = filteredCharges.Select(c =>
        {
            var obStatus = c.MembershipPaymentId is not null && memStatusMap.TryGetValue(c.MembershipPaymentId.Value, out var ms)
                ? ms
                : c.MatchFeePaymentId is not null && feeStatusMap.TryGetValue(c.MatchFeePaymentId.Value, out var fs)
                    ? fs
                    : c.Status == PixChargeStatus.Approved ? PaymentStatus.Paid.ToString() : PaymentStatus.Pending.ToString();
            return MapChargeDto(c, names.GetValueOrDefault(c.PlayerProfileId, "Jogador"), obStatus, false, false, now);
        }).ToList();

        return new AdminPixOverviewDto(
            BuildGatewayStatus(settings),
            new PixAdminKpisDto(effPending, effApproved, effExpired, effCancelled, effActive, paidByPixCount, paidManualCount, exemptCount, receivedPix, receivedManual, pendingAmount),
            chargeDtos);
    }

    private Task<List<PixCharge>> LoadOutstandingMatchDayChargesAsync(Guid feeId, CancellationToken ct)
        => _db.PixCharges.Where(c => c.MatchFeePaymentId == feeId
            && (c.Status == PixChargeStatus.Pending || c.Status == PixChargeStatus.Expired)).ToListAsync(ct);

    private static void InvalidateMatchDayCharges(IEnumerable<PixCharge> charges, DateTimeOffset now)
    {
        foreach (var charge in charges)
        {
            charge.Status = PixChargeStatus.Cancelled;
            charge.UpdatedAt = now;
        }
    }

    private static void TouchMatchFee(MatchFeePayment fee, DateTimeOffset now)
    {
        // Mantém incrementos distintos também em bancos com precisão de microssegundos.
        var next = fee.UpdatedAt.AddTicks(10);
        fee.UpdatedAt = now > next ? now : next;
    }

    public async Task<PixReconcileResultDto> ReconcilePendingAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var pendingCharges = (await _db.PixCharges.AsNoTracking()
            .Where(c => c.GroupId == groupId && c.Status == PixChargeStatus.Pending)
            .ToListAsync(ct)).Where(c => c.CreatedAt >= cutoff).OrderBy(c => c.CreatedAt).ToList();

        int checkedCount = 0, approved = 0, expired = 0, cancelled = 0, stillPending = 0, errors = 0;

        foreach (var pending in pendingCharges)
        {
            var charge = await _db.PixCharges.FirstOrDefaultAsync(c => c.Id == pending.Id, ct);
            if (charge is null)
                continue;

            checkedCount++;
            try
            {
                var mpStatus = await _mp.GetPaymentAsync(charge.MpPaymentId, ct);
                await ApplyProviderStatusAsync(charge, mpStatus, userId, "reconcile", ct);

                switch (charge.Status)
                {
                    case PixChargeStatus.Approved: approved++; break;
                    case PixChargeStatus.Expired: expired++; break;
                    case PixChargeStatus.Cancelled: cancelled++; break;
                    default: stillPending++; break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _db.ChangeTracker.Clear();
                errors++;
                _logger.LogWarning(ex, "Erro ao reconciliar PixCharge {ChargeId} (MP {MpPaymentId})", charge.Id, charge.MpPaymentId);
            }
        }

        return new PixReconcileResultDto(checkedCount, approved, expired, cancelled, stillPending, errors);
    }

    private async Task<(bool WasUpdated, PaymentStatus ObligationStatus)> ApplyProviderStatusAsync(
        PixCharge charge, MpPixPaymentStatus mpStatus, Guid? actorUserId, string sourceTag, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var mapped = PixChargePolicy.MapProviderStatus(mpStatus.Status);
        var wasUpdated = false;
        charge.ProviderStatus = mpStatus.Status;

        if (mapped == PixChargeStatus.Approved)
        {
            if (charge.Status == PixChargeStatus.Approved)
                return (false, await ResolveObligationStatusAsync(charge, ct));

            var pay = charge.Kind == PixChargeKind.Membership && charge.MembershipPaymentId is Guid paymentId
                ? await _db.MembershipPayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct)
                : null;
            var fee = charge.Kind == PixChargeKind.MatchDay && charge.MatchFeePaymentId is Guid feeId
                ? await _db.MatchFeePayments.FirstOrDefaultAsync(f => f.Id == feeId, ct)
                : null;
            var obligationStatus = pay?.Status ?? fee?.Status ?? PaymentStatus.Pending;
            var obligationAmount = pay?.Amount ?? fee?.Amount;
            var paidAmount = mpStatus.TransactionAmount;
            string? rejection = null;
            if (fee is not null && charge.Status == PixChargeStatus.Cancelled)
                rejection = "PixApprovalAfterCancellation";
            else if (obligationAmount is null)
                rejection = "PixObligationMissing";
            else if (paidAmount is null || paidAmount < Math.Max(charge.Amount, obligationAmount.Value))
                rejection = "PixAmountMismatch";

            if (rejection is not null)
            {
                if (!await _db.AuditEntries.AnyAsync(a => a.EntityId == charge.Id && a.Action == rejection, ct))
                {
                    _logger.LogWarning(
                        "Baixa da cobrança Pix {ChargeId} recusada ({Reason}). Cobrança: {ChargeAmount}, obrigação: {ObligationAmount}, recebido: {Received}.",
                        charge.Id, rejection, charge.Amount, obligationAmount, paidAmount);
                    _db.AuditEntries.Add(new AuditEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = charge.GroupId,
                        ActorUserId = actorUserId ?? Guid.Empty,
                        Action = rejection,
                        EntityType = nameof(PixCharge),
                        EntityId = charge.Id,
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            mpPaymentId = charge.MpPaymentId,
                            expected = obligationAmount,
                            charged = charge.Amount,
                            received = paidAmount,
                            source = sourceTag
                        })
                    });
                }

                await _db.SaveChangesAsync(ct);
                return (false, obligationStatus);
            }

            charge.Status = PixChargeStatus.Approved;
            charge.ApprovedAt = mpStatus.DateApproved ?? now;
            charge.UpdatedAt = now;

            if (obligationStatus != PaymentStatus.Paid)
            {
                if (pay is not null)
                {
                    pay.Status = PaymentStatus.Paid;
                    pay.PaymentSource = PaymentSource.Pix;
                    pay.PaidAt = charge.ApprovedAt;
                    pay.UpdatedAt = now;
                }
                else if (fee is not null)
                {
                    fee.Status = PaymentStatus.Paid;
                    fee.PaymentSource = PaymentSource.Pix;
                    fee.PaidAt = charge.ApprovedAt;
                    TouchMatchFee(fee, now);
                }

                _db.AuditEntries.Add(new AuditEntry
                {
                    Id = Guid.NewGuid(),
                    GroupId = charge.GroupId,
                    ActorUserId = actorUserId ?? Guid.Empty,
                    Action = "PixPaymentApproved",
                    EntityType = pay is not null ? nameof(MembershipPayment) : nameof(MatchFeePayment),
                    EntityId = pay?.Id ?? fee!.Id,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        pixChargeId = charge.Id, mpPaymentId = charge.MpPaymentId, source = sourceTag, amount = obligationAmount
                    })
                });
            }

            await _db.SaveChangesAsync(ct);
            return (true, PaymentStatus.Paid);
        }

        if (mapped == PixChargeStatus.Cancelled && charge.Status != PixChargeStatus.Cancelled && charge.Status != PixChargeStatus.Approved)
        {
            charge.Status = PixChargeStatus.Cancelled;
            charge.UpdatedAt = now;
            wasUpdated = true;
            await _db.SaveChangesAsync(ct);
        }
        else if (PixChargePolicy.IsExpired(charge, now))
        {
            charge.Status = PixChargeStatus.Expired;
            charge.UpdatedAt = now;
            wasUpdated = true;
            await _db.SaveChangesAsync(ct);
        }

        return (wasUpdated, await ResolveObligationStatusAsync(charge, ct));
    }

    private async Task<PaymentStatus> ResolveObligationStatusAsync(PixCharge charge, CancellationToken ct)
    {
        if (charge.MembershipPaymentId is not null)
        {
            var pay = await _db.MembershipPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == charge.MembershipPaymentId.Value, ct);
            if (pay is not null) return pay.Status;
        }
        if (charge.MatchFeePaymentId is not null)
        {
            var fee = await _db.MatchFeePayments.AsNoTracking().FirstOrDefaultAsync(f => f.Id == charge.MatchFeePaymentId.Value, ct);
            if (fee is not null) return fee.Status;
        }
        return charge.Status == PixChargeStatus.Approved ? PaymentStatus.Paid : PaymentStatus.Pending;
    }

    private async Task<string> ResolvePayerEmailAsync(Guid playerProfileId, CancellationToken ct)
    {
        var email = await (
            from p in _db.PlayerProfiles.AsNoTracking()
            join u in _db.Users.AsNoTracking() on p.UserId equals u.Id
            where p.Id == playerProfileId
            select u.Email
        ).FirstOrDefaultAsync(ct);

        return !string.IsNullOrWhiteSpace(email) && email.Contains('@')
            ? email
            : $"jogador+{playerProfileId:N}@futebolatemporal.local";
    }

    private GatewayStatusDto BuildGatewayStatus(GroupSettings settings)
        => new(_mp.Mode, _mp.IsConfigured, _mp.IsSimulated, _options.HasWebhookSecret,
            settings.DefaultMonthlyFee, settings.DefaultMatchDayFee,
            PixChargePolicy.ClampExpirationMinutes(settings.PixExpirationMinutes), WebhookPath);

    private static PixChargeStatus EffectiveStatus(PixCharge c, DateTimeOffset now)
        => PixChargePolicy.IsExpired(c, now) ? PixChargeStatus.Expired : c.Status;

    private static PixChargeSummaryDto MapChargeSummary(PixCharge c, DateTimeOffset now)
        => new(c.Id, c.MpPaymentId, EffectiveStatus(c, now).ToString(), c.Amount, c.ExpiresAt, c.CreatedAt, c.ApprovedAt, c.IsSimulated);

    private static PixChargeDto MapChargeDto(PixCharge c, string playerName, string obligationStatus, bool includeQr, bool reused, DateTimeOffset now)
        => new(c.Id, c.GroupId, c.PlayerProfileId, playerName, c.Kind.ToString(),
            c.MembershipPaymentId, c.MatchFeePaymentId, c.MatchId, c.Amount, c.Description,
            c.MpPaymentId, includeQr && c.Status != PixChargeStatus.Cancelled ? c.QrCode : null,
            includeQr && c.Status != PixChargeStatus.Cancelled ? c.QrCodeBase64 : null,
            c.ExpiresAt, EffectiveStatus(c, now).ToString(), obligationStatus, c.IsSimulated, reused, c.CreatedAt, c.ApprovedAt);

    private static DateTime LocalNow(string timeZoneId) => ConvertToGroupTime(DateTimeOffset.UtcNow, timeZoneId);

    private static DateTime ConvertToGroupTime(DateTimeOffset utc, string timeZoneId)
    {
        try { return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return utc.UtcDateTime; }
    }
}
