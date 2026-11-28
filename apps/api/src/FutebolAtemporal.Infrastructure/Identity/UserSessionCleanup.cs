using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Identity;

public sealed class UserSessionCleanup(AppDbContext db)
{
    private const int BatchSize = 250;

    public async Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Retém também as sessões revogadas até a expiração original do cookie. SQLite não traduz
        // comparações LINQ de DateTimeOffset. O limite estrito evita antecipar a expiração por arredondamento.
        var expired = db.Database.IsSqlite()
            ? await db.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM "UserSessions"
                WHERE julianday("ExpiresAt") < julianday({now})
                ORDER BY julianday("ExpiresAt"), "Id"
                LIMIT {BatchSize}
                """).ToListAsync(ct)
            : await db.UserSessions.Where(s => s.ExpiresAt < now)
                .OrderBy(s => s.ExpiresAt).ThenBy(s => s.Id).Take(BatchSize).Select(s => s.Id).ToListAsync(ct);
        if (expired.Count == 0) return 0;

        // Preserva o registro do aparelho e suas entregas. Só afeta inscrições ainda ligadas a estas
        // sessões. Uma inscrição já reatribuída a uma sessão válida não corresponde ao filtro.
        await db.PushSubscriptions.Where(s => s.SessionId.HasValue && expired.Contains(s.SessionId.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.SessionId, (Guid?)null)
                .SetProperty(s => s.DeactivatedAt, s => s.DeactivatedAt ?? now)
                .SetProperty(s => s.IsActive, false), ct);

        var deleted = await db.UserSessions.Where(s => expired.Contains(s.Id)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return deleted;
    }
}
