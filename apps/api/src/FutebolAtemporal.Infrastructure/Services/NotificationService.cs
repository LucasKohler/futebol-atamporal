using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Notifications;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FutebolAtemporal.Infrastructure.Services;

public sealed class NotificationService : INotificationService, INotificationTriggerService
{
    private readonly AppDbContext _db;
    private readonly WebPushOptions _options;
    private readonly NotificationWakeupSignal _wakeup;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext db,
        WebPushOptions options,
        NotificationWakeupSignal wakeup,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _options = options;
        _wakeup = wakeup;
        _logger = logger;
    }

    // ---------- Jogador ----------

    public VapidPublicKeyDto GetVapidPublicKey()
    {
        if (!_options.IsConfigured)
            throw new ServiceUnavailableException("Notificações push não estão configuradas neste ambiente.", "push_not_configured");
        return new VapidPublicKeyDto(_options.PublicKey!, _options.KeyId!);
    }

    public async Task<SubscribePushResponseDto> UpsertSubscriptionAsync(Guid userId, Guid sessionId, SubscribePushRequest request, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            throw new ServiceUnavailableException("Notificações push não estão configuradas neste ambiente.", "push_not_configured");

        var endpoint = NotificationPolicies.ValidateEndpoint(request.Endpoint, _options.Mode == WebPushMode.Simulator);
        if (request.Keys is null)
            throw new DomainException("Chaves da inscrição push são obrigatórias.", "invalid_subscription");
        var (p256dh, auth) = NotificationPolicies.ValidatePushKeys(request.Keys.P256dh, request.Keys.Auth);

        var userAgent = TrimMax(request.UserAgent, NotificationPolicies.MaxUserAgentLength);
        var deviceLabel = TrimMax(request.DeviceLabel, NotificationPolicies.MaxDeviceLabelLength);
        var now = DateTimeOffset.UtcNow;

        var existing = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is null)
        {
            existing = new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionId = sessionId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                VapidKeyId = _options.KeyId!,
                UserAgent = userAgent,
                DeviceLabel = deviceLabel,
                IsActive = true,
                CreatedAt = now,
                LastSeenAt = now
            };
            _db.PushSubscriptions.Add(existing);
        }
        else if (existing.UserId != userId)
        {
            // Aparelho compartilhado: só transfere com prova de posse — as mesmas chaves que o navegador
            // gerou para esta inscrição (o segredo auth não sai do aparelho). Quem só conhece a URL é recusado.
            var sameKeys = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(existing.P256dh), Encoding.UTF8.GetBytes(p256dh))
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(existing.Auth), Encoding.UTF8.GetBytes(auth));
            if (!sameKeys)
                throw new ConflictException("Esta inscrição push pertence a outro aparelho ou conta.", "subscription_conflict");

            // Revoga a inscrição da conta anterior e cria outra para a conta atual (novo Id; histórico não migra).
            // Remoção e criação vão no mesmo SaveChanges (atômico; o EF ordena a exclusão antes do índice único).
            _db.PushSubscriptions.Remove(existing);
            existing = new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionId = sessionId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                VapidKeyId = _options.KeyId!,
                UserAgent = userAgent,
                DeviceLabel = deviceLabel,
                IsActive = true,
                CreatedAt = now,
                LastSeenAt = now
            };
            _db.PushSubscriptions.Add(existing);
        }
        else
        {
            // Mesma conta renovando a inscrição deste aparelho.
            existing.UserId = userId;
            existing.SessionId = sessionId; // passa a valer só enquanto a sessão atual valer
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.VapidKeyId = _options.KeyId!;
            existing.UserAgent = userAgent ?? existing.UserAgent;
            existing.DeviceLabel = deviceLabel ?? existing.DeviceLabel;
            existing.IsActive = true;
            existing.LastSeenAt = now;
            existing.FailureCount = 0;
            existing.FailureReason = null;
            existing.DeactivatedAt = null;
        }

        await _db.SaveChangesAsync(ct);
        return new SubscribePushResponseDto(existing.Id, existing.IsActive, _options.KeyId!);
    }

    public async Task DeleteSubscriptionAsync(Guid userId, UnsubscribePushRequest request, CancellationToken ct = default)
    {
        var endpoint = (request?.Endpoint ?? string.Empty).Trim();
        if (endpoint.Length == 0) return;

        var existing = await _db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.UserId == userId, ct);
        if (existing is null) return;

        _db.PushSubscriptions.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<MyNotificationsResponseDto> GetMyNotificationsAsync(Guid userId, Guid groupId, Guid? before, int limit, CancellationToken ct = default)
    {
        if (groupId == Guid.Empty)
            throw new DomainException("groupId é obrigatório.", "validation_failed");

        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);

        var clampedLimit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 50);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);

        // ADR-011: filtros/ordenação por DateTimeOffset em memória no SQLite.
        var all = (await _db.UserNotifications.AsNoTracking()
                .Where(n => n.UserId == userId && n.GroupId == groupId)
                .ToListAsync(ct))
            .Where(n => n.CreatedAt >= cutoff)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .ToList();

        var unreadCount = all.Count(n => n.ReadAt is null);

        IEnumerable<UserNotification> slice = all;
        if (before is { } cursorId)
        {
            var idx = all.FindIndex(n => n.Id == cursorId);
            if (idx >= 0)
                slice = all.Skip(idx + 1);
        }

        var page = slice.Take(clampedLimit + 1).ToList();
        var hasMore = page.Count > clampedLimit;
        if (hasMore) page = page.Take(clampedLimit).ToList();
        Guid? nextCursor = hasMore && page.Count > 0 ? page[^1].Id : null;

        var devices = (await _db.PushSubscriptions.AsNoTracking()
                .Where(s => s.UserId == userId)
                .ToListAsync(ct))
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new PushDeviceDto(
                s.Id,
                s.DeviceLabel,
                s.CreatedAt,
                s.LastSuccessAt,
                s.IsActive,
                Stale: _options.IsConfigured && !string.Equals(s.VapidKeyId, _options.KeyId, StringComparison.Ordinal)))
            .ToList();

        var items = page.Select(n => new NotificationDto(
            n.Id,
            n.GroupId,
            n.Category.ToString(),
            n.Title,
            n.Body,
            n.ActionUrl,
            n.MatchId,
            n.CreatedAt,
            n.ReadAt)).ToList();

        return new MyNotificationsResponseDto(
            items,
            unreadCount,
            nextCursor,
            new NotificationPushInfoDto(_options.Mode.ToString(), _options.KeyId),
            devices);
    }

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var notif = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);
        if (notif is null)
            throw new NotFoundException("Notificação não encontrada.", "not_found");

        if (notif.ReadAt is null)
        {
            notif.ReadAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<ReadAllNotificationsResponseDto> MarkAllReadAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        if (groupId == Guid.Empty)
            throw new DomainException("groupId é obrigatório.", "validation_failed");
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct);

        var unread = await _db.UserNotifications
            .Where(n => n.UserId == userId && n.GroupId == groupId && n.ReadAt == null)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var n in unread)
            n.ReadAt = now;

        if (unread.Count > 0)
            await _db.SaveChangesAsync(ct);

        return new ReadAllNotificationsResponseDto(unread.Count);
    }

    // ---------- Admin ----------

    public async Task<AdminNotificationSummaryDto> GetAdminSummaryAsync(Guid userId, Guid groupId, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        var activeMemberUserIds = await _db.GroupMemberships.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        var activeSubs = await _db.PushSubscriptions.AsNoTracking()
            .Where(s => activeMemberUserIds.Contains(s.UserId) && s.IsActive)
            .ToListAsync(ct);

        if (_options.IsConfigured)
            activeSubs = activeSubs.Where(s => string.Equals(s.VapidKeyId, _options.KeyId, StringComparison.Ordinal)).ToList();

        var membersWithPush = activeSubs.Select(s => s.UserId).Distinct().Count();
        var cutoff30d = DateTimeOffset.UtcNow.AddDays(-30);

        var campaigns = await _db.NotificationCampaigns.AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .ToListAsync(ct);

        var sentLast30d = campaigns.Where(c => c.CreatedAt >= cutoff30d).Sum(c => c.RecipientCount);
        var lastCampaignAt = campaigns.Count > 0 ? campaigns.Max(c => c.CreatedAt) : (DateTimeOffset?)null;

        return new AdminNotificationSummaryDto(
            activeMemberUserIds.Count,
            membersWithPush,
            activeSubs.Count,
            sentLast30d,
            _options.Mode.ToString(),
            lastCampaignAt);
    }

    public async Task<AdminNotificationPreviewDto> PreviewAdminNotificationAsync(
        Guid userId, Guid groupId, AdminNotificationPreviewRequest request, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        NotificationAudience audience;
        bool requireOpenMatch = false;

        if (!string.IsNullOrWhiteSpace(request.Quick))
        {
            (audience, requireOpenMatch) = ResolveQuickAudience(request.Quick, request.Audience);
        }
        else
        {
            audience = ParseBroadcastAudience(request.Audience);
        }

        var (recipientUserIds, matchId) = await ResolveRecipientsAsync(groupId, audience, requireOpenMatch, ct);
        var pushDevices = await CountPushDevicesAsync(recipientUserIds, ct);
        return new AdminNotificationPreviewDto(recipientUserIds.Count, pushDevices, matchId);
    }

    public async Task<CampaignDispatchResult> SendQuickReminderAsync(
        Guid userId, Guid groupId, AdminQuickNotificationRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        var (category, audience, requireOpenMatch, title, body, actionUrl) = ResolveQuickSpec(request.Kind, request.Audience);
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var requestHash = ComputeRequestHash(new { kind = "quick", category = category.ToString(), audience = audience.ToString() });

        return await DispatchSerializedAsync(
            groupId, NotificationSource.AdminQuick, category, audience, requireOpenMatch,
            title, body, actionUrl, userId, normalizedKey, requestHash, applyCooldown: true,
            "notification.quick", "Nenhum destinatário elegível para este lembrete.",
            "Idempotency-Key já utilizada com parâmetros diferentes.", ct);
    }

    public async Task<CampaignDispatchResult> SendBroadcastAsync(
        Guid userId, Guid groupId, AdminBroadcastNotificationRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        var (title, body) = NotificationPolicies.ValidateTitleAndBody(request.Title, request.Body);
        var actionUrl = NotificationPolicies.ValidateActionUrl(request.ActionUrl);
        var audience = ParseBroadcastAudience(request.Audience);

        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var requestHash = ComputeRequestHash(new { kind = "broadcast", audience = audience.ToString(), title, body, actionUrl });

        return await DispatchSerializedAsync(
            groupId, NotificationSource.AdminBroadcast, NotificationCategory.AdminBroadcast, audience, requireOpenMatch: false,
            title, body, actionUrl, userId, normalizedKey, requestHash, applyCooldown: false,
            "notification.broadcast", "Nenhum destinatário elegível para este comunicado.",
            "Idempotency-Key já utilizada com outro conteúdo.", ct);
    }

    // Um disparo por vez por grupo + categoria neste processo: idempotência e intervalo de 30 min ficam
    // livres de corrida entre requisições simultâneas. Limite: com mais de uma instância da API, o intervalo
    // depende desta serialização local; a idempotência segue garantida pelo índice único (GroupId, chave).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DispatchLocks = new();

    private async Task<CampaignDispatchResult> DispatchSerializedAsync(
        Guid groupId, NotificationSource source, NotificationCategory category, NotificationAudience audience, bool requireOpenMatch,
        string title, string body, string actionUrl, Guid userId, string? normalizedKey, string requestHash, bool applyCooldown,
        string auditAction, string noRecipientsMessage, string mismatchMessage, CancellationToken ct)
    {
        var gate = DispatchLocks.GetOrAdd($"{groupId:N}:{category}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (normalizedKey is not null
                && await FindByIdempotencyKeyAsync(groupId, normalizedKey, requestHash, mismatchMessage, ct) is { } replay)
                return replay;

            if (applyCooldown)
            {
                // Leitura prévia para responder rápido; a garantia vem da linha de NotificationCooldowns
                // gravada na mesma transação da campanha (vale entre instâncias).
                var lastSent = await LastQuickSentAtAsync(groupId, category, ct);
                if (await CheckCooldownOrReplayAsync(groupId, normalizedKey, requestHash, mismatchMessage, lastSent, DateTimeOffset.UtcNow, ct) is { } cooldownReplay)
                    return cooldownReplay;
            }

            var (recipientUserIds, matchId) = await ResolveRecipientsAsync(groupId, audience, requireOpenMatch, ct);
            if (recipientUserIds.Count == 0)
                throw new UnprocessableEntityException(noRecipientsMessage, "no_recipients");

            NotificationCampaign campaign;
            try
            {
                campaign = await CreateCampaignAndNotificationsAsync(
                    groupId, source, category, audience, title, body, actionUrl, matchId, userId,
                    normalizedKey, requestHash, recipientUserIds, auditAction, applyCooldown, ct);
            }
            catch (ConflictException ex) when (ex.Code == "reminder_cooldown" && normalizedKey is not null)
            {
                // Outra instância pode ter gravado a campanha desta mesma chave entre a leitura prévia e a transação.
                _db.ChangeTracker.Clear();
                if (await FindByIdempotencyKeyAsync(groupId, normalizedKey, requestHash, mismatchMessage, ct) is { } winner)
                    return winner;
                throw;
            }
            catch (DbUpdateException)
            {
                // Outra requisição (outra categoria ou outra instância da API) gravou antes:
                // - mesma chave: devolve o resultado dela, ou 422 se o conteúdo for outro;
                // - lembrete rápido: a linha de intervalo mudou, então 409 reminder_cooldown. Nunca 500.
                _db.ChangeTracker.Clear();
                if (normalizedKey is not null
                    && await FindByIdempotencyKeyAsync(groupId, normalizedKey, requestHash, mismatchMessage, ct) is { } winner)
                    return winner;
                if (applyCooldown)
                {
                    var lastSent = await LastQuickSentAtAsync(groupId, category, ct);
                    if (await CheckCooldownOrReplayAsync(groupId, normalizedKey, requestHash, mismatchMessage, lastSent, DateTimeOffset.UtcNow, ct) is { } cooldownWinner)
                        return cooldownWinner;
                }
                throw new ConflictException("Não foi possível registrar o envio agora. Tente de novo.", "dispatch_conflict");
            }

            _wakeup.Signal();
            return new CampaignDispatchResult(await MapCampaignAsync(campaign, ct), Created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DateTimeOffset?> LastQuickSentAtAsync(Guid groupId, NotificationCategory category, CancellationToken ct)
    {
        var row = await _db.NotificationCooldowns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.Category == category, ct);
        // Campanhas anteriores à tabela de intervalo também contam.
        var lastCampaign = (await _db.NotificationCampaigns.AsNoTracking()
                .Where(c => c.GroupId == groupId && c.Source == NotificationSource.AdminQuick && c.Category == category)
                .Select(c => c.CreatedAt)
                .ToListAsync(ct))
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        var last = row is null || lastCampaign > row.LastSentAt ? lastCampaign : row.LastSentAt;
        return last == DateTimeOffset.MinValue ? null : last;
    }

    private async Task<CampaignDispatchResult?> CheckCooldownOrReplayAsync(
        Guid groupId,
        string? normalizedKey,
        string requestHash,
        string mismatchMessage,
        DateTimeOffset? lastSent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!TryGetCooldownRetryAfterSeconds(lastSent, now, out var retryAfterSeconds))
            return null;

        // Antes de devolver 409 reminder_cooldown, confirma se a campanha recém-gravada pertence à mesma chave.
        if (normalizedKey is not null
            && await FindByIdempotencyKeyAsync(groupId, normalizedKey, requestHash, mismatchMessage, ct) is { } replay)
            return replay;

        throw CreateCooldownException(retryAfterSeconds);
    }

    private static void ThrowIfInCooldown(DateTimeOffset? lastSent, DateTimeOffset now)
    {
        if (TryGetCooldownRetryAfterSeconds(lastSent, now, out var retryAfterSeconds))
            throw CreateCooldownException(retryAfterSeconds);
    }

    private static bool TryGetCooldownRetryAfterSeconds(DateTimeOffset? lastSent, DateTimeOffset now, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (lastSent is null) return false;
        var until = lastSent.Value.AddMinutes(NotificationPolicies.QuickReminderCooldownMinutes);
        if (until <= now) return false;
        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((until - now).TotalSeconds));
        return true;
    }

    private static ConflictException CreateCooldownException(int retryAfterSeconds) =>
        new(
            "Este lembrete rápido já foi enviado recentemente. Aguarde o intervalo mínimo de 30 minutos.",
            "reminder_cooldown",
            new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds });

    private async Task<CampaignDispatchResult?> FindByIdempotencyKeyAsync(
        Guid groupId, string key, string requestHash, string mismatchMessage, CancellationToken ct)
    {
        var existing = await _db.NotificationCampaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.GroupId == groupId && c.IdempotencyKey == key, ct);
        if (existing is null) return null;
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            throw new UnprocessableEntityException(mismatchMessage, "idempotency_key_mismatch");
        return new CampaignDispatchResult(await MapCampaignAsync(existing, ct), Created: false);
    }

    public async Task<CampaignListResponseDto> ListCampaignsAsync(Guid userId, Guid groupId, int page, int pageSize, CancellationToken ct = default)
    {
        await AccessHelper.RequireMemberAsync(_db, userId, groupId, ct, adminOnly: true);

        var p = Math.Max(1, page);
        var ps = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 50);

        var all = (await _db.NotificationCampaigns.AsNoTracking()
                .Where(c => c.GroupId == groupId)
                .ToListAsync(ct))
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        var total = all.Count;
        var slice = all.Skip((p - 1) * ps).Take(ps).ToList();

        var creatorIds = slice.Where(c => c.CreatedByUserId.HasValue).Select(c => c.CreatedByUserId!.Value).Distinct().ToList();
        var userNames = await LoadUserNamesAsync(creatorIds, ct);

        var items = slice.Select(c => new CampaignDto(
            c.Id,
            c.Source.ToString(),
            c.Category.ToString(),
            c.Audience.ToString(),
            c.Title,
            c.ActionUrl,
            c.CreatedAt,
            c.CreatedByUserId is { } cid ? userNames.GetValueOrDefault(cid, "Administrador") : null,
            c.RecipientCount,
            c.PushDeviceCount,
            c.PushSentCount,
            c.PushFailedCount)).ToList();

        return new CampaignListResponseDto(items, total);
    }

    // ---------- Gatilhos automáticos (Seção 3, gravados na transação do evento) ----------

    public void SignalDispatcher() => _wakeup.Signal();

    public async Task EnqueueMatchOpenAsync(Guid groupId, Guid matchId, Guid actorUserId, CancellationToken ct = default)
    {
        try
        {
            var recipientUserIds = await _db.GroupMemberships.AsNoTracking()
                .Where(m => m.GroupId == groupId && m.Status == MembershipStatus.Active && m.UserId != actorUserId)
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(ct);

            var dedupeKey = $"match-open:{matchId:D}";
            await EnqueueAutomaticNotificationsAsync(
                groupId, matchId, NotificationCategory.MatchOpen,
                "⚽ Lista da pelada liberada!",
                "⚽ Lista da pelada liberada! Confirme sua presença.",
                "/partida",
                recipientUserIds.Select(uid => (uid, "⚽ Lista da pelada liberada!", "⚽ Lista da pelada liberada! Confirme sua presença.", dedupeKey)).ToList(),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enfileirar notificação MatchOpen para partida {MatchId}", matchId);
        }
    }

    public async Task EnqueueWaitlistPromotedAsync(
        Guid groupId,
        Guid matchId,
        IReadOnlyList<(Guid RegistrationId, Guid PlayerProfileId, DateTimeOffset ConfirmedAt)> promoted,
        CancellationToken ct = default)
    {
        if (promoted.Count == 0) return;
        try
        {
            var profileIds = promoted.Select(p => p.PlayerProfileId).Distinct().ToList();
            var profileToUser = await _db.PlayerProfiles.AsNoTracking()
                .Where(p => profileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.UserId, ct);

            var activeUsers = await GetActiveGroupUserSetAsync(groupId, ct);
            var entries = new List<(Guid UserId, string Title, string Body, string DedupeKey)>();

            foreach (var item in promoted)
            {
                if (!profileToUser.TryGetValue(item.PlayerProfileId, out var uid) || !activeUsers.Contains(uid))
                    continue;
                var dedupe = $"waitlist-promoted:{item.RegistrationId:D}:{item.ConfirmedAt.UtcDateTime:O}";
                entries.Add((uid, "⚡ Vaga confirmada!", "⚡ Você subiu da lista de espera e está confirmado!", dedupe));
            }

            await EnqueueAutomaticNotificationsAsync(
                groupId, matchId, NotificationCategory.WaitlistPromoted,
                "⚡ Vaga confirmada!",
                "⚡ Você subiu da lista de espera e está confirmado!",
                "/partida",
                entries,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enfileirar notificação WaitlistPromoted para partida {MatchId}", matchId);
        }
    }

    public async Task EnqueueDrawPublishedAsync(
        Guid groupId,
        Guid matchId,
        Guid drawId,
        DateTimeOffset confirmedAt,
        IReadOnlyList<(Guid PlayerProfileId, string TeamColor)> assignments,
        CancellationToken ct = default)
    {
        if (assignments.Count == 0) return;
        try
        {
            var profileIds = assignments.Select(a => a.PlayerProfileId).Distinct().ToList();
            var profileToUser = await _db.PlayerProfiles.AsNoTracking()
                .Where(p => profileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.UserId, ct);

            var activeUsers = await GetActiveGroupUserSetAsync(groupId, ct);
            var dedupe = $"draw-confirmed:{drawId:D}:{confirmedAt.UtcDateTime:O}";
            var entries = new List<(Guid UserId, string Title, string Body, string DedupeKey)>();

            foreach (var (profileId, color) in assignments)
            {
                if (!profileToUser.TryGetValue(profileId, out var uid) || !activeUsers.Contains(uid))
                    continue;
                var body = $"🎨 Times sorteados! Você está no {color}.";
                entries.Add((uid, "🎨 Times sorteados!", body, dedupe));
            }

            await EnqueueAutomaticNotificationsAsync(
                groupId, matchId, NotificationCategory.DrawPublished,
                "🎨 Times sorteados!",
                "🎨 Times sorteados! Confira sua cor no app.",
                "/partida",
                entries,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enfileirar notificação DrawPublished para partida {MatchId}", matchId);
        }
    }

    public async Task EnqueueMatchCompletedAsync(Guid groupId, Guid matchId, IReadOnlyList<Guid> participantProfileIds, CancellationToken ct = default)
    {
        if (participantProfileIds.Count == 0) return;
        try
        {
            var profileIds = participantProfileIds.Distinct().ToList();
            var profileToUser = await _db.PlayerProfiles.AsNoTracking()
                .Where(p => profileIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.UserId, ct);

            var activeUsers = await GetActiveGroupUserSetAsync(groupId, ct);
            var dedupe = $"match-completed:{matchId:D}";
            var entries = profileToUser.Values
                .Where(activeUsers.Contains)
                .Distinct()
                .Select(uid => (uid, "🏆 Pelada encerrada!", "🏆 Pelada encerrada! Veja resultados e melhores do dia.", dedupe))
                .ToList();

            await EnqueueAutomaticNotificationsAsync(
                groupId, matchId, NotificationCategory.MatchCompleted,
                "🏆 Pelada encerrada!",
                "🏆 Pelada encerrada! Veja resultados e melhores do dia.",
                "/partida",
                entries,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enfileirar notificação MatchCompleted para partida {MatchId}", matchId);
        }
    }

    // ---------- Auxiliares ----------

    private async Task EnqueueAutomaticNotificationsAsync(
        Guid groupId,
        Guid matchId,
        NotificationCategory category,
        string campaignTitle,
        string campaignBody,
        string actionUrl,
        List<(Guid UserId, string Title, string Body, string DedupeKey)> entries,
        CancellationToken ct)
    {
        if (entries.Count == 0) return;

        var dedupeKeys = entries.Select(e => e.DedupeKey).Distinct().ToList();
        var userIds = entries.Select(e => e.UserId).Distinct().ToList();

        // Verifica duplicatas já persistidas ou já rastreadas no ChangeTracker
        var existingPairs = (await _db.UserNotifications.AsNoTracking()
                .Where(n => userIds.Contains(n.UserId) && n.DedupeKey != null && dedupeKeys.Contains(n.DedupeKey))
                .Select(n => new { n.UserId, n.DedupeKey })
                .ToListAsync(ct))
            .Select(x => (x.UserId, x.DedupeKey!))
            .ToHashSet();

        foreach (var tracked in _db.ChangeTracker.Entries<UserNotification>()
            .Where(e => e.State == EntityState.Added && e.Entity.DedupeKey is not null))
        {
            existingPairs.Add((tracked.Entity.UserId, tracked.Entity.DedupeKey!));
        }

        var fresh = new List<(Guid UserId, string Title, string Body, string DedupeKey)>();
        var seenInBatch = new HashSet<(Guid, string)>();
        foreach (var entry in entries)
        {
            var pair = (entry.UserId, entry.DedupeKey);
            if (existingPairs.Contains(pair) || !seenInBatch.Add(pair))
                continue;
            fresh.Add(entry);
        }

        if (fresh.Count == 0) return;

        var pushDeviceCount = await CountPushDevicesAsync(fresh.Select(f => f.UserId).Distinct().ToList(), ct);
        var now = DateTimeOffset.UtcNow;
        var initialPushState = _options.IsConfigured ? NotificationPushState.Pending : NotificationPushState.Skipped;

        var campaign = new NotificationCampaign
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Source = NotificationSource.Automatic,
            Category = category,
            Audience = NotificationAudience.Direct,
            Title = campaignTitle,
            Body = campaignBody,
            ActionUrl = actionUrl,
            MatchId = matchId,
            RecipientCount = fresh.Count,
            PushDeviceCount = pushDeviceCount,
            CreatedAt = now
        };
        _db.NotificationCampaigns.Add(campaign);

        foreach (var item in fresh)
        {
            _db.UserNotifications.Add(new UserNotification
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                UserId = item.UserId,
                CampaignId = campaign.Id,
                Category = category,
                Title = item.Title,
                Body = item.Body,
                ActionUrl = actionUrl,
                MatchId = matchId,
                DedupeKey = item.DedupeKey,
                CreatedAt = now,
                PushState = initialPushState
            });
        }
    }

    private async Task<NotificationCampaign> CreateCampaignAndNotificationsAsync(
        Guid groupId,
        NotificationSource source,
        NotificationCategory category,
        NotificationAudience audience,
        string title,
        string body,
        string actionUrl,
        Guid? matchId,
        Guid actorUserId,
        string? idempotencyKey,
        string requestHash,
        List<Guid> recipientUserIds,
        string auditAction,
        bool applyCooldown,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var pushDeviceCount = await CountPushDevicesAsync(recipientUserIds, ct);
        var now = DateTimeOffset.UtcNow;
        var initialPushState = _options.IsConfigured ? NotificationPushState.Pending : NotificationPushState.Skipped;

        if (applyCooldown)
        {
            // Compare-and-set na linha (GroupId, Category): duas instâncias que tentem ao mesmo tempo
            // colidem na chave primária ou no token Version, e só uma grava.
            var cooldown = await _db.NotificationCooldowns.FirstOrDefaultAsync(c => c.GroupId == groupId && c.Category == category, ct);
            if (cooldown is null)
            {
                _db.NotificationCooldowns.Add(new NotificationCooldown { GroupId = groupId, Category = category, LastSentAt = now, Version = 1 });
            }
            else
            {
                ThrowIfInCooldown(cooldown.LastSentAt, now);
                cooldown.LastSentAt = now;
                cooldown.Version++;
            }
        }

        var campaign = new NotificationCampaign
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Source = source,
            Category = category,
            Audience = audience,
            Title = title,
            Body = body,
            ActionUrl = actionUrl,
            MatchId = matchId,
            CreatedByUserId = actorUserId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            RecipientCount = recipientUserIds.Count,
            PushDeviceCount = pushDeviceCount,
            CreatedAt = now
        };
        _db.NotificationCampaigns.Add(campaign);

        foreach (var uid in recipientUserIds)
        {
            _db.UserNotifications.Add(new UserNotification
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                UserId = uid,
                CampaignId = campaign.Id,
                Category = category,
                Title = title,
                Body = body,
                ActionUrl = actionUrl,
                MatchId = matchId,
                CreatedAt = now,
                PushState = initialPushState
            });
        }

        // Auditoria sem corpo e sem dados de inscrição (Seção 2)
        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            ActorUserId = actorUserId,
            Action = auditAction,
            EntityType = nameof(NotificationCampaign),
            EntityId = campaign.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                category = category.ToString(),
                audience = audience.ToString(),
                recipientCount = recipientUserIds.Count
            })
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return campaign;
    }

    private async Task<(List<Guid> RecipientUserIds, Guid? MatchId)> ResolveRecipientsAsync(
        Guid groupId, NotificationAudience audience, bool requireOpenMatch, CancellationToken ct)
    {
        var activeMembers = await _db.GroupMemberships.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (audience == NotificationAudience.AllMembers)
            return (activeMembers, null);

        var activeSet = activeMembers.ToHashSet();

        if (audience == NotificationAudience.NotResponded)
        {
            var upcoming = await FindUpcomingMatchAsync(groupId, ct);
            if (requireOpenMatch && (upcoming is null || upcoming.Status != MatchStatus.RegistrationOpen))
                throw new ConflictException("Não há partida com lista de presença aberta no momento.", "no_open_match");
            if (upcoming is null)
                return ([], null);

            var regProfileIds = await _db.MatchRegistrations.AsNoTracking()
                .Where(r => r.MatchId == upcoming.Id)
                .Select(r => r.PlayerProfileId)
                .ToListAsync(ct);
            var regUserIds = (await _db.PlayerProfiles.AsNoTracking()
                    .Where(p => regProfileIds.Contains(p.Id))
                    .Select(p => p.UserId)
                    .ToListAsync(ct))
                .ToHashSet();

            return (activeMembers.Where(uid => !regUserIds.Contains(uid)).ToList(), upcoming.Id);
        }

        if (audience == NotificationAudience.MatchConfirmed)
        {
            var upcoming = await FindUpcomingMatchAsync(groupId, ct);
            if (upcoming is null)
                return ([], null);

            var confirmedProfileIds = await _db.MatchRegistrations.AsNoTracking()
                .Where(r => r.MatchId == upcoming.Id && r.Status == RegistrationStatus.Confirmed)
                .Select(r => r.PlayerProfileId)
                .ToListAsync(ct);
            var userIds = await _db.PlayerProfiles.AsNoTracking()
                .Where(p => confirmedProfileIds.Contains(p.Id))
                .Select(p => p.UserId)
                .Distinct()
                .ToListAsync(ct);

            return (userIds.Where(activeSet.Contains).ToList(), upcoming.Id);
        }

        if (audience == NotificationAudience.PaymentPending)
        {
            var group = await _db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == groupId, ct);
            var today = GroupClock.LocalDate(DateTimeOffset.UtcNow, group.TimeZoneId);

            var pendingProfileIds = new HashSet<Guid>();

            // 1. Mensalidade da competência atual (fuso do grupo) Pending/Overdue
            var period = await _db.MembershipBillingPeriods.AsNoTracking()
                .FirstOrDefaultAsync(p => p.GroupId == groupId && p.Year == today.Year && p.Month == today.Month, ct);
            if (period is not null)
            {
                var membershipPending = await _db.MembershipPayments.AsNoTracking()
                    .Include(p => p.MonthlyMembership)
                    .Where(p => p.BillingPeriodId == period.Id
                        && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Overdue)
                        && p.MonthlyMembership.Status == MonthlyMembershipStatus.Active)
                    .Select(p => p.MonthlyMembership.PlayerProfileId)
                    .ToListAsync(ct);
                foreach (var pid in membershipPending)
                    pendingProfileIds.Add(pid);
            }

            // 2. Diária: só existe depois do fechamento, então vale a última rodada fechada,
            //    buscada à parte da próxima partida (que pode já estar aberta).
            var groupMatches = await _db.Matches.AsNoTracking().Where(m => m.GroupId == groupId).ToListAsync(ct);
            var upcoming = MatchSelection.Upcoming(groupMatches, DateTimeOffset.UtcNow);
            var latest = MatchSelection.LatestClosed(groupMatches);

            var relevantMatchIds = new HashSet<Guid>();
            if (upcoming is not null) relevantMatchIds.Add(upcoming.Id);
            if (latest is not null) relevantMatchIds.Add(latest.Id);

            if (relevantMatchIds.Count > 0)
            {
                var matchFeePending = await _db.MatchFeePayments.AsNoTracking()
                    .Where(f => f.GroupId == groupId
                        && relevantMatchIds.Contains(f.MatchId)
                        && (f.Status == PaymentStatus.Pending || f.Status == PaymentStatus.Overdue))
                    .Select(f => f.PlayerProfileId)
                    .ToListAsync(ct);
                foreach (var pid in matchFeePending)
                    pendingProfileIds.Add(pid);
            }

            if (pendingProfileIds.Count == 0)
                return ([], upcoming?.Id ?? latest?.Id);

            var userIds = await _db.PlayerProfiles.AsNoTracking()
                .Where(p => pendingProfileIds.Contains(p.Id))
                .Select(p => p.UserId)
                .Distinct()
                .ToListAsync(ct);

            return (userIds.Where(activeSet.Contains).ToList(), upcoming?.Id ?? latest?.Id);
        }

        throw new DomainException("Público-alvo inválido.", "validation_failed");
    }

    private async Task<Match?> FindUpcomingMatchAsync(Guid groupId, CancellationToken ct)
    {
        var matches = await _db.Matches.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status != MatchStatus.Completed && m.Status != MatchStatus.Cancelled)
            .ToListAsync(ct);
        return MatchSelection.Upcoming(matches, DateTimeOffset.UtcNow);
    }

    private async Task<int> CountPushDevicesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0 || !_options.IsConfigured) return 0;
        return await _db.PushSubscriptions.AsNoTracking()
            .CountAsync(s => userIds.Contains(s.UserId) && s.IsActive && s.VapidKeyId == _options.KeyId, ct);
    }

    private async Task<HashSet<Guid>> GetActiveGroupUserSetAsync(Guid groupId, CancellationToken ct)
        => (await _db.GroupMemberships.AsNoTracking()
                .Where(m => m.GroupId == groupId && m.Status == MembershipStatus.Active)
                .Select(m => m.UserId)
                .ToListAsync(ct))
            .ToHashSet();

    private async Task<CampaignDto> MapCampaignAsync(NotificationCampaign c, CancellationToken ct)
    {
        string? creatorName = null;
        if (c.CreatedByUserId is { } cid)
        {
            var map = await LoadUserNamesAsync([cid], ct);
            creatorName = map.GetValueOrDefault(cid, "Administrador");
        }
        return new CampaignDto(
            c.Id,
            c.Source.ToString(),
            c.Category.ToString(),
            c.Audience.ToString(),
            c.Title,
            c.ActionUrl,
            c.CreatedAt,
            creatorName,
            c.RecipientCount,
            c.PushDeviceCount,
            c.PushSentCount,
            c.PushFailedCount);
    }

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Nickname })
            .ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.Nickname) ? u.DisplayName : u.Nickname);
    }

    private static (NotificationAudience Audience, bool RequireOpenMatch) ResolveQuickAudience(string kind, string? requestedAudience)
    {
        var ( _, audience, requireOpen, _, _, _) = ResolveQuickSpec(kind, requestedAudience);
        return (audience, requireOpen);
    }

    private static (NotificationCategory Category, NotificationAudience Audience, bool RequireOpenMatch, string Title, string Body, string ActionUrl)
        ResolveQuickSpec(string? kind, string? requestedAudience)
    {
        return (kind ?? string.Empty).Trim() switch
        {
            "RegistrationReminder" => (
                NotificationCategory.RegistrationReminder,
                string.Equals(requestedAudience?.Trim(), "AllMembers", StringComparison.OrdinalIgnoreCase)
                    ? NotificationAudience.AllMembers
                    : NotificationAudience.NotResponded,
                !string.Equals(requestedAudience?.Trim(), "AllMembers", StringComparison.OrdinalIgnoreCase),
                "📋 Lembrete de confirmação",
                "📋 A lista da pelada está aberta! Confirme sua presença no app.",
                "/partida"
            ),
            "PaymentReminder" => (
                NotificationCategory.PaymentReminder,
                NotificationAudience.PaymentPending,
                false,
                "💸 Lembrete de pagamento Pix",
                "💸 Você possui pagamento pendente no grupo. Confira na aba Pagamentos.",
                "/pagamentos"
            ),
            "MatchAvailable" => (
                NotificationCategory.MatchOpen,
                NotificationAudience.AllMembers,
                false,
                "⚽ Partida liberada!",
                "⚽ A partida e as informações da rodada estão disponíveis no app.",
                "/partida"
            ),
            _ => throw new DomainException("Tipo de lembrete rápido inválido.", "validation_failed")
        };
    }

    private static NotificationAudience ParseBroadcastAudience(string? raw)
    {
        return (raw ?? string.Empty).Trim() switch
        {
            "AllMembers" => NotificationAudience.AllMembers,
            "MatchConfirmed" => NotificationAudience.MatchConfirmed,
            "PaymentPending" => NotificationAudience.PaymentPending,
            "NotResponded" => NotificationAudience.NotResponded,
            _ => throw new DomainException("Público-alvo inválido.", "validation_failed")
        };
    }

    private static string? NormalizeIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var trimmed = key.Trim();
        if (trimmed.Length > 64)
            throw new DomainException("Idempotency-Key deve ter no máximo 64 caracteres.", "validation_failed");
        return trimmed;
    }

    // Hash sobre JSON estruturado: separadores dentro do texto não fazem conteúdos diferentes colidirem.
    private static string ComputeRequestHash(object payload)
    {
        var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? TrimMax(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
