using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

/// <summary>Notas separadas de linha e goleiro (SO-1). Um goleiro forte não compensa uma linha fraca.</summary>
public static class PlayerRatings
{
    public const decimal Min = 0m;
    public const decimal Max = 10m;

    public static void Apply(PlayerProfile profile, decimal lineRating, bool canPlayGoalkeeper, decimal? goalkeeperRating)
    {
        EnsureInRange(lineRating, "Nota de linha");
        if (goalkeeperRating is { } gk)
        {
            if (!canPlayGoalkeeper)
                throw new DomainException("Nota de goleiro exige marcar o jogador como apto para o gol.");
            EnsureInRange(gk, "Nota de goleiro");
        }

        profile.SkillRating = lineRating;
        profile.CanPlayGoalkeeper = canPlayGoalkeeper;
        profile.GoalkeeperRating = goalkeeperRating;
    }

    private static void EnsureInRange(decimal value, string field)
    {
        if (value is < Min or > Max)
            throw new DomainException($"{field} deve estar entre 0 e 10.");
        if (decimal.Round(value, 2) != value)
            throw new DomainException($"{field} aceita no máximo duas casas decimais.");
    }
}
