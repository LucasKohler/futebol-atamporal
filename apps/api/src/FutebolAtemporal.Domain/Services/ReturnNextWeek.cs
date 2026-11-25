using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Domain.Services;

public static class ReturnNextWeek
{
    public static HashSet<Guid> PlayerIds(
        IReadOnlyList<(Guid MatchId, DateTimeOffset StartsAt, MatchStatus Status)> matches,
        IReadOnlyList<(Guid MatchId, Guid PlayerProfileId, RegistrationStatus Status)> registrations)
    {
        var ordered = matches
            .Where(m => m.Status is not MatchStatus.Draft and not MatchStatus.Cancelled)
            .OrderBy(m => m.StartsAt)
            .ThenBy(m => m.MatchId)
            .ToList();
        var result = new HashSet<Guid>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var current = ordered[i];
            var next = ordered[i + 1];
            var inCurrent = registrations
                .Where(r => r.MatchId == current.MatchId && Retains(r.Status))
                .Select(r => r.PlayerProfileId)
                .ToHashSet();
            var inNext = registrations
                .Where(r => r.MatchId == next.MatchId && Retains(r.Status))
                .Select(r => r.PlayerProfileId)
                .ToHashSet();
            inCurrent.IntersectWith(inNext);
            result.UnionWith(inCurrent);
        }

        return result;
    }

    public static bool Retains(RegistrationStatus registrationStatus) =>
        registrationStatus is RegistrationStatus.Confirmed or RegistrationStatus.Participated;
}
