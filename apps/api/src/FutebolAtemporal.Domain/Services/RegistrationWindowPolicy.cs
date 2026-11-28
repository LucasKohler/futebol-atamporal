using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

public static class RegistrationWindowPolicy
{
    public static void Validate(DateTimeOffset? monthly, DateTimeOffset? casual, DateTimeOffset startsAt)
    {
        if (monthly is null && casual is null)
            return;

        if (monthly is null || casual is null || monthly >= casual || casual > startsAt)
            throw new DomainException("Informe as duas aberturas em ordem, até o início da partida.", "invalid_registration_windows");
    }

    public static DateTimeOffset? OpensAt(Match match, bool monthly) =>
        monthly ? match.MonthlyRegistrationOpensAt : match.CasualRegistrationOpensAt;

    public static bool IsEarly(DateTimeOffset? opensAt, DateTimeOffset now) => opensAt > now;
}
