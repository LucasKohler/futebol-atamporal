namespace FutebolAtemporal.Domain.Services;

public static class PlayerDisplayName
{
    public static string Effective(string registeredName, string? nickname)
    {
        if (!string.IsNullOrWhiteSpace(nickname))
            return nickname.Trim();
        if (!string.IsNullOrWhiteSpace(registeredName))
            return registeredName.Trim();
        return "Jogador";
    }
}
