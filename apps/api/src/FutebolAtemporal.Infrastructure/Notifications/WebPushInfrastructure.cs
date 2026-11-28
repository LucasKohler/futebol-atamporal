using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FutebolAtemporal.Infrastructure.Notifications;

public enum WebPushMode
{
    Disabled = 0,
    Simulator = 1,
    Live = 2
}

public sealed class WebPushOptions
{
    public WebPushMode Mode { get; init; }
    public string? PublicKey { get; init; }
    public string? PrivateKey { get; init; }
    public string Subject { get; init; } = "mailto:admin@futebolatemporal.local";
    public string? KeyId { get; init; }

    public bool IsConfigured => Mode != WebPushMode.Disabled
        && !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey)
        && !string.IsNullOrWhiteSpace(KeyId);

    private static readonly object DevKeyLock = new();
    private sealed record DevVapidFile(string PublicKey, string PrivateKey, string Subject);

    public static WebPushOptions Resolve(IConfiguration config, IHostEnvironment env, ILogger? logger = null)
    {
        var rawMode = config["WebPush:Mode"];
        var publicKey = config["WebPush:PublicKey"]?.Trim();
        var privateKey = config["WebPush:PrivateKey"]?.Trim();
        var subject = config["WebPush:Subject"]?.Trim();
        if (string.IsNullOrWhiteSpace(subject))
            subject = "mailto:admin@futebolatemporal.local";

        var mode = ParseMode(rawMode, env);
        var hasKeys = !string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey);

        if (mode == WebPushMode.Disabled)
        {
            return new WebPushOptions { Mode = WebPushMode.Disabled, Subject = subject };
        }

        if (hasKeys)
        {
            return new WebPushOptions
            {
                Mode = mode,
                PublicKey = publicKey,
                PrivateKey = privateKey,
                Subject = subject,
                KeyId = NotificationPolicies.ComputeVapidKeyId(publicKey!)
            };
        }

        // Sem chaves configuradas (D4):
        if (mode == WebPushMode.Simulator || env.IsEnvironment("Testing"))
        {
            var (pub, priv) = GenerateP256KeyPair();
            return new WebPushOptions
            {
                Mode = WebPushMode.Simulator,
                PublicKey = pub,
                PrivateKey = priv,
                Subject = subject,
                KeyId = NotificationPolicies.ComputeVapidKeyId(pub)
            };
        }

        if (env.IsDevelopment())
        {
            var dataFile = config["WebPush:DevKeyFile"];
            if (string.IsNullOrWhiteSpace(dataFile))
                dataFile = Path.Combine(env.ContentRootPath, ".data", "vapid.dev.json");

            var (pub, priv, subj) = LoadOrCreateDevKeys(dataFile, subject);
            return new WebPushOptions
            {
                Mode = mode,
                PublicKey = pub,
                PrivateKey = priv,
                Subject = subj,
                KeyId = NotificationPolicies.ComputeVapidKeyId(pub)
            };
        }

        // Production sem chaves: nunca gerar automaticamente; fica Disabled com aviso.
        logger?.LogWarning("WebPush: chaves VAPID ausentes em ambiente {Environment}; push desativado (in-app continua ativo).", env.EnvironmentName);
        return new WebPushOptions { Mode = WebPushMode.Disabled, Subject = subject };
    }

    private static WebPushMode ParseMode(string? rawMode, IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(rawMode) && Enum.TryParse<WebPushMode>(rawMode, ignoreCase: true, out var parsed))
            return parsed;

        if (env.IsEnvironment("Testing"))
            return WebPushMode.Simulator;

        return WebPushMode.Live;
    }

    private static (string PublicKey, string PrivateKey, string Subject) LoadOrCreateDevKeys(string filePath, string defaultSubject)
    {
        lock (DevKeyLock)
        {
            if (TryReadDevKeys(filePath, defaultSubject, out var loaded))
                return loaded;

            var (pub, priv) = GenerateP256KeyPair();
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new DevVapidFile(pub, priv, defaultSubject), new JsonSerializerOptions { WriteIndented = true });
            var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, json, Encoding.UTF8);
                File.Move(tmp, filePath, overwrite: false);
                return (pub, priv, defaultSubject);
            }
            catch (IOException)
            {
                if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
                if (TryReadDevKeys(filePath, defaultSubject, out loaded))
                    return loaded;
                return (pub, priv, defaultSubject);
            }
        }
    }

    private static bool TryReadDevKeys(string filePath, string defaultSubject, out (string PublicKey, string PrivateKey, string Subject) keys)
    {
        keys = default;
        if (!File.Exists(filePath)) return false;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var existing = JsonSerializer.Deserialize<DevVapidFile>(reader.ReadToEnd());
            if (existing is not null
                && !string.IsNullOrWhiteSpace(existing.PublicKey)
                && !string.IsNullOrWhiteSpace(existing.PrivateKey))
            {
                keys = (existing.PublicKey, existing.PrivateKey, string.IsNullOrWhiteSpace(existing.Subject) ? defaultSubject : existing.Subject);
                return true;
            }
        }
        catch
        {
            // Arquivo ausente ou corrompido
        }
        return false;
    }

    /// <summary>Gera um par ECDSA P-256 (prime256v1) válido para VAPID: chave pública de 65 bytes (0x04 || X || Y) e privada de 32 bytes.</summary>
    public static (string PublicKey, string PrivateKey) GenerateP256KeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: true);
        var x = PadLeft32(p.Q.X!);
        var y = PadLeft32(p.Q.Y!);
        var d = PadLeft32(p.D!);

        var uncompressed = new byte[65];
        uncompressed[0] = 0x04;
        Buffer.BlockCopy(x, 0, uncompressed, 1, 32);
        Buffer.BlockCopy(y, 0, uncompressed, 33, 32);

        return (NotificationPolicies.ToBase64Url(uncompressed), NotificationPolicies.ToBase64Url(d));
    }

    private static byte[] PadLeft32(byte[] src)
    {
        if (src.Length == 32) return src;
        if (src.Length > 32) return src[^32..];
        var dst = new byte[32];
        Buffer.BlockCopy(src, 0, dst, 32 - src.Length, src.Length);
        return dst;
    }
}

public sealed record SimulatedPushRecord(
    DateTimeOffset SentAt,
    Guid SubscriptionId,
    string Endpoint,
    WebPushPayload Payload,
    WebPushSendResult Result);

public sealed class SimulatedWebPushStore
{
    public ConcurrentQueue<SimulatedPushRecord> Sent { get; } = new();
    public ConcurrentDictionary<string, Func<WebPushTarget, WebPushPayload, WebPushSendResult>> Overrides { get; } = new();
    public Func<WebPushTarget, WebPushPayload, WebPushSendResult>? GlobalOverride { get; set; }
}

public sealed class SimulatedWebPushSender : IWebPushSender
{
    private readonly SimulatedWebPushStore _store;

    public SimulatedWebPushSender(SimulatedWebPushStore store) => _store = store;

    public Task<WebPushSendResult> SendAsync(WebPushTarget target, WebPushPayload payload, CancellationToken ct = default)
    {
        WebPushSendResult result;
        if (_store.Overrides.TryGetValue(target.Endpoint, out var endpointHandler))
            result = endpointHandler(target, payload);
        else if (_store.GlobalOverride is not null)
            result = _store.GlobalOverride(target, payload);
        else
            result = new WebPushSendResult(WebPushSendOutcome.Sent, StatusCodes.Status201Created);

        _store.Sent.Enqueue(new SimulatedPushRecord(DateTimeOffset.UtcNow, target.SubscriptionId, target.Endpoint, payload, result));
        return Task.FromResult(result);
    }
}

public sealed class LiveWebPushSender : IWebPushSender
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly WebPushOptions _options;
    private readonly ILogger<LiveWebPushSender> _logger;

    public LiveWebPushSender(HttpClient http, WebPushOptions options, ILogger<LiveWebPushSender> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<WebPushSendResult> SendAsync(WebPushTarget target, WebPushPayload payload, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return new WebPushSendResult(WebPushSendOutcome.TransientError, null, "WebPush não configurado.");

        // Defesa em profundidade contra SSRF (D6)
        try
        {
            NotificationPolicies.ValidateEndpoint(target.Endpoint, allowLocalhostSimulator: false);
        }
        catch
        {
            return new WebPushSendResult(WebPushSendOutcome.ClientError, StatusCodes.Status400BadRequest, "Endpoint não permitido.");
        }

        var json = JsonSerializer.Serialize(new
        {
            id = payload.Id,
            title = payload.Title,
            body = payload.Body,
            url = payload.Url,
            tag = payload.Tag
        }, PayloadJson);

        if (Encoding.UTF8.GetByteCount(json) > NotificationPolicies.MaxPushPayloadBytes)
            return new WebPushSendResult(WebPushSendOutcome.ClientError, StatusCodes.Status413PayloadTooLarge, "Payload excede 3 KB.");

        var subscription = new Lib.Net.Http.WebPush.PushSubscription
        {
            Endpoint = target.Endpoint,
            Keys = new Dictionary<string, string>
            {
                ["p256dh"] = target.P256dh,
                ["auth"] = target.Auth
            }
        };

        var message = new PushMessage(json)
        {
            TimeToLive = 86400,
            Urgency = payload.HighUrgency ? PushMessageUrgency.High : PushMessageUrgency.Normal,
            Topic = SanitizeTopic(payload.Tag)
        };

        var auth = new VapidAuthentication(_options.PublicKey!, _options.PrivateKey!)
        {
            Subject = _options.Subject
        };

        var client = new PushServiceClient(_http) { DefaultAuthentication = auth };
        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, ct);
            return new WebPushSendResult(WebPushSendOutcome.Sent, StatusCodes.Status201Created);
        }
        catch (PushServiceClientException ex)
        {
            var status = (int)ex.StatusCode;
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return new WebPushSendResult(WebPushSendOutcome.Gone, status, $"HTTP {status}");
            if (ex.StatusCode == HttpStatusCode.Forbidden)
                return new WebPushSendResult(WebPushSendOutcome.KeyMismatch, status, $"HTTP {status}");
            if (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
                return new WebPushSendResult(WebPushSendOutcome.ClientError, status, $"HTTP {status}");
            return new WebPushSendResult(WebPushSendOutcome.TransientError, status, $"HTTP {status}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Falha transitória ao enviar Web Push para inscrição {SubscriptionId} ({ErrorType}).",
                target.SubscriptionId, ex.GetType().Name);
            return new WebPushSendResult(WebPushSendOutcome.TransientError, null, "Timeout ou erro de rede.");
        }
    }

    private static string SanitizeTopic(string tag)
    {
        var sb = new StringBuilder(32);
        foreach (var c in tag)
        {
            if ((c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') || c is '-' or '_')
            {
                sb.Append(c);
                if (sb.Length == 32) break;
            }
        }
        return sb.Length > 0 ? sb.ToString() : "notif";
    }
}

public sealed class NotificationWakeupSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Signal() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.WaitToReadAsync(cts.Token);
            while (_channel.Reader.TryRead(out _)) { }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout periódico de segurança (30s)
        }
    }
}

public sealed class NotificationDispatchRunner : INotificationDispatchRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WebPushOptions _options;
    private readonly ILogger<NotificationDispatchRunner> _logger;

    public NotificationDispatchRunner(
        IServiceScopeFactory scopeFactory,
        WebPushOptions options,
        ILogger<NotificationDispatchRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<int> ProcessPendingAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IWebPushSender>();

        var now = DateTimeOffset.UtcNow;
        // ADR-011: comparação de DateTimeOffset em memória no SQLite.
        var candidates = await db.UserNotifications
            .Where(n => n.PushState == NotificationPushState.Pending)
            .ToListAsync(ct);

        var due = candidates
            .Where(n => (n.NextPushAt is null || n.NextPushAt <= now)
                && (n.ClaimedUntil is null || n.ClaimedUntil <= now))
            .OrderBy(n => n.CreatedAt)
            .Take(100)
            .ToList();

        if (due.Count == 0) return 0;

        // Reserva atômica por aviso (compare-and-set no DispatchVersion): se outro despachante reservou antes,
        // a gravação falha e este aviso é pulado. Reserva vencida (processo caído) volta a ficar disponível.
        var claimed = new List<UserNotification>();
        foreach (var notif in due)
        {
            notif.ClaimedUntil = now.Add(ClaimDuration);
            notif.DispatchVersion++;
            try
            {
                await db.SaveChangesAsync(ct);
                claimed.Add(notif);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.Entry(notif).State = EntityState.Detached;
            }
        }
        if (claimed.Count == 0) return 0;

        var campaignIds = claimed.Select(n => n.CampaignId).Distinct().ToList();
        var notificationIds = claimed.Select(n => n.Id).ToList();

        var delivered = (await db.NotificationDeliveries.AsNoTracking()
                .Where(d => notificationIds.Contains(d.NotificationId))
                .Select(d => new { d.NotificationId, d.SubscriptionId })
                .ToListAsync(ct))
            .Select(d => (d.NotificationId, d.SubscriptionId))
            .ToHashSet();

        var campaigns = await db.NotificationCampaigns
            .Where(c => campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var processed = 0;
        foreach (var notif in claimed)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            // Revalida vínculo e inscrições no banco a cada aviso (sem snapshot de início de lote).
            if (!_options.IsConfigured || !await HasActiveMembershipAsync(db, notif.GroupId, notif.UserId, ct))
            {
                notif.PushState = notif.PushSentCount > 0 ? NotificationPushState.PartiallySent : NotificationPushState.Skipped;
                notif.ClaimedUntil = null;
                await db.SaveChangesAsync(ct);
                continue;
            }

            var userSubs = await db.PushSubscriptions.AsNoTracking()
                .Where(s => s.UserId == notif.UserId && s.IsActive && s.VapidKeyId == _options.KeyId && s.SessionId != null)
                .ToListAsync(ct);
            // Só aparelhos cuja sessão de login ainda vale (logout, expiração, reset de senha ou novo login encerram).
            var liveSessions = await SessionCookieEvents.ActiveSessionIdsAsync(db, notif.UserId, userSubs.Select(s => s.SessionId!.Value), ct);
            userSubs = userSubs.Where(s => liveSessions.Contains(s.SessionId!.Value)).ToList();
            if (userSubs.Count == 0)
            {
                notif.PushState = notif.PushSentCount > 0 ? NotificationPushState.PartiallySent : NotificationPushState.Skipped;
                notif.ClaimedUntil = null;
                await db.SaveChangesAsync(ct);
                continue;
            }

            // Só aparelhos que ainda não receberam este aviso.
            var pendingSubs = userSubs.Where(s => !delivered.Contains((notif.Id, s.Id))).ToList();

            var tag = $"{notif.Category.ToString().ToLowerInvariant()}-{(notif.MatchId ?? notif.CampaignId):N}";
            // Tela bloqueada recebe só texto genérico; título e corpo reais ficam na área autenticada do app.
            var payload = new WebPushPayload(
                notif.Id,
                LockscreenTitle,
                LockscreenBody,
                notif.ActionUrl,
                tag,
                HighUrgency: notif.Category == NotificationCategory.WaitlistPromoted);

            int sentHere = 0;
            int permanentFail = 0;
            int transientFail = 0;
            var lostClaim = false;
            var membershipLost = false;
            var revokedSubIds = new HashSet<Guid>();

            foreach (var sub in pendingSubs)
            {
                // Renova a reserva antes de cada envio. Como cada envio tem limite (SendTimeout) bem menor
                // que a reserva (ClaimDuration), a reserva nunca vence no meio de um envio: nenhum outro
                // despachante pode pegar este aviso enquanto este envia, mesmo que o total passe de 2 min.
                notif.ClaimedUntil = DateTimeOffset.UtcNow.Add(ClaimDuration);
                notif.DispatchVersion++;
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    lostClaim = true; // outro despachante assumiu: para aqui sem enviar
                    break;
                }

                // Revalida vínculo, inscrição e sessão antes de cada nova chamada. Uma revogação já
                // observada impede esse envio. Uma chamada já iniciada, inclusive o push aceito pelo
                // provedor, pode terminar ou ser entregue após a revogação.
                if (!await HasActiveMembershipAsync(db, notif.GroupId, notif.UserId, ct))
                {
                    membershipLost = true;
                    break;
                }

                var freshSub = await db.PushSubscriptions.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == sub.Id && s.UserId == notif.UserId && s.IsActive && s.VapidKeyId == _options.KeyId, ct);
                if (freshSub?.SessionId is { } freshSession
                    && !(await SessionCookieEvents.ActiveSessionIdsAsync(db, notif.UserId, [freshSession], ct)).Contains(freshSession))
                    freshSub = null; // sessão encerrada durante o lote
                if (freshSub is null || freshSub.SessionId is null)
                {
                    revokedSubIds.Add(sub.Id);
                    continue;
                }

                WebPushSendResult result;
                try
                {
                    using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    sendCts.CancelAfter(SendTimeout);
                    result = await sender.SendAsync(new WebPushTarget(freshSub.Id, freshSub.Endpoint, freshSub.P256dh, freshSub.Auth), payload, sendCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    result = new WebPushSendResult(WebPushSendOutcome.TransientError, null, "Tempo de envio esgotado.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Erro inesperado no sender de push para inscrição {SubscriptionId} ({ErrorType}).",
                        sub.Id, ex.GetType().Name);
                    result = new WebPushSendResult(WebPushSendOutcome.TransientError, null, "Exceção no sender.");
                }

                var trackedSub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Id == sub.Id, ct);

                switch (result.Outcome)
                {
                    case WebPushSendOutcome.Sent:
                        if (trackedSub is null)
                        {
                            // Inscrição removida enquanto o envio estava em voo: não insere NotificationDelivery sem FK
                            // nem aborta os avisos restantes do lote.
                            revokedSubIds.Add(sub.Id);
                            break;
                        }
                        trackedSub.LastSuccessAt = now;
                        trackedSub.FailureCount = 0;
                        trackedSub.FailureReason = null;
                        // Persiste a entrega deste aparelho na hora: queda depois daqui não reenvia para ele.
                        var delivery = new NotificationDelivery
                        {
                            Id = Guid.NewGuid(),
                            NotificationId = notif.Id,
                            SubscriptionId = sub.Id,
                            SentAt = now
                        };
                        db.NotificationDeliveries.Add(delivery);
                        try
                        {
                            await db.SaveChangesAsync(ct);
                            sentHere++;
                            delivered.Add((notif.Id, sub.Id));
                        }
                        catch (DbUpdateException ex)
                        {
                            db.Entry(delivery).State = EntityState.Detached;
                            // Só trata como duplicata após confirmar no banco que a entrega foi persistida por outra instância.
                            var alreadyPersisted = await db.NotificationDeliveries.AsNoTracking()
                                .AnyAsync(d => d.NotificationId == notif.Id && d.SubscriptionId == sub.Id, ct);
                            if (alreadyPersisted)
                            {
                                delivered.Add((notif.Id, sub.Id));
                                await db.Entry(trackedSub).ReloadAsync(ct);
                            }
                            else if (!await db.PushSubscriptions.AsNoTracking().AnyAsync(s => s.Id == sub.Id, ct))
                            {
                                // Inscrição removida concorrentemente entre a consulta e SaveChanges: segue o lote com segurança.
                                db.Entry(trackedSub).State = EntityState.Detached;
                                revokedSubIds.Add(sub.Id);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Falha ao persistir entrega da notificação {NotificationId} para inscrição {SubscriptionId} ({ErrorType}).",
                                    notif.Id, sub.Id, ex.GetType().Name);
                                throw;
                            }
                        }
                        break;

                    case WebPushSendOutcome.Gone:
                        permanentFail++;
                        if (trackedSub is not null)
                        {
                            trackedSub.IsActive = false;
                            trackedSub.DeactivatedAt = now;
                            trackedSub.FailureCount++;
                            trackedSub.FailureReason = Truncate(result.Reason ?? "HTTP 410", 200);
                        }
                        break;

                    case WebPushSendOutcome.KeyMismatch:
                        permanentFail++;
                        if (trackedSub is not null)
                        {
                            trackedSub.IsActive = false;
                            trackedSub.DeactivatedAt = now;
                            trackedSub.FailureCount++;
                            trackedSub.FailureReason = Truncate(result.Reason ?? "HTTP 403 VAPID mismatch", 200);
                        }
                        break;

                    case WebPushSendOutcome.ClientError:
                        permanentFail++;
                        if (trackedSub is not null)
                        {
                            trackedSub.FailureCount++;
                            trackedSub.FailureReason = Truncate(result.Reason ?? "HTTP 4xx", 200);
                            if (trackedSub.FailureCount >= 3)
                            {
                                trackedSub.IsActive = false;
                                trackedSub.DeactivatedAt = now;
                            }
                        }
                        break;

                    default:
                        transientFail++;
                        if (trackedSub is not null)
                            trackedSub.FailureReason = Truncate(result.Reason ?? "Erro transitório", 200);
                        break;
                }
            }

            if (lostClaim)
            {
                // Quem assumiu a reserva segue com o aviso; as entregas já gravadas impedem reenvio.
                db.Entry(notif).State = EntityState.Detached;
                continue;
            }

            notif.PushSentCount += sentHere;
            if (campaigns.TryGetValue(notif.CampaignId, out var campaign))
            {
                campaign.PushSentCount += sentHere;
                if (permanentFail > 0)
                    campaign.PushFailedCount += permanentFail;
            }

            var activeSubsRemaining = userSubs.Where(s => !revokedSubIds.Contains(s.Id)).ToList();

            if (membershipLost)
            {
                notif.PushState = notif.PushSentCount > 0
                    ? NotificationPushState.PartiallySent
                    : NotificationPushState.Skipped;
            }
            else if (transientFail > 0)
            {
                notif.PushAttempts++;
                if (notif.PushAttempts < NotificationPolicies.MaxPushAttempts)
                {
                    notif.NextPushAt = now.Add(NotificationPolicies.NextRetryDelay(notif.PushAttempts));
                    notif.PushState = NotificationPushState.Pending;
                }
                else
                {
                    if (campaigns.TryGetValue(notif.CampaignId, out var camp))
                        camp.PushFailedCount += transientFail;
                    notif.PushState = notif.PushSentCount > 0
                        ? NotificationPushState.PartiallySent
                        : NotificationPushState.Failed;
                }
            }
            else if (activeSubsRemaining.Count == 0)
            {
                notif.PushState = notif.PushSentCount > 0
                    ? NotificationPushState.PartiallySent
                    : NotificationPushState.Skipped;
            }
            else if (activeSubsRemaining.All(s => delivered.Contains((notif.Id, s.Id))))
            {
                notif.PushState = NotificationPushState.Sent;
            }
            else if (notif.PushSentCount > 0)
            {
                notif.PushState = NotificationPushState.PartiallySent;
            }
            else
            {
                notif.PushState = NotificationPushState.Failed;
            }

            notif.ClaimedUntil = null;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A reserva venceu e outro despachante assumiu: as entregas já gravadas impedem reenvio.
                db.ChangeTracker.Clear();
                return processed;
            }
        }

        return processed;
    }

    /// <summary>Tempo de reserva de um aviso por um despachante (reenvios só depois disso, se o processo cair).</summary>
    internal static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(2);

    /// <summary>Limite de cada envio; precisa ficar bem abaixo de <see cref="ClaimDuration"/>.</summary>
    internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Texto mostrado na tela bloqueada: genérico, sem nome, valor ou assunto do aviso.</summary>
    public const string LockscreenTitle = "Futebol Atemporal";
    public const string LockscreenBody = "Você tem um novo aviso. Abra o app para ver.";

    private static Task<bool> HasActiveMembershipAsync(AppDbContext db, Guid groupId, Guid userId, CancellationToken ct) =>
        db.GroupMemberships.AsNoTracking()
            .AnyAsync(m => m.GroupId == groupId && m.UserId == userId && m.Status == MembershipStatus.Active, ct);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed class NotificationDispatcher : BackgroundService
{
    private readonly NotificationWakeupSignal _signal;
    private readonly INotificationDispatchRunner _runner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        NotificationWakeupSignal signal,
        INotificationDispatchRunner runner,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDispatcher> logger)
    {
        _signal = signal;
        _runner = runner;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanupAt = DateTimeOffset.MinValue;
        // Ao reiniciar, retoma imediatamente o que ficou pendente no banco (D1).
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _runner.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro no ciclo do NotificationDispatcher ({ErrorType}).", ex.GetType().Name);
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= nextCleanupAt)
            {
                nextCleanupAt = now.AddHours(1);
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<UserSessionCleanup>().PurgeExpiredAsync(now, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Falha na limpeza de sessões ({ErrorType}).", ex.GetType().Name);
                }
            }

            await _signal.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
