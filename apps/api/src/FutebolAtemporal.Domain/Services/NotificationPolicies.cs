using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

/// <summary>
/// Regras de segurança e validação de PWA/Web Push (D4, D6, D8).
/// </summary>
public static class NotificationPolicies
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 500;
    public const int MaxEndpointLength = 2048;
    public const int MaxDeviceLabelLength = 60;
    public const int MaxUserAgentLength = 256;
    public const int MaxPushPayloadBytes = 3072;
    public const int QuickReminderCooldownMinutes = 30;
    public const int MaxPushAttempts = 3;

    public static readonly IReadOnlySet<string> AllowedActionUrls = new HashSet<string>(StringComparer.Ordinal)
    {
        "/",
        "/partida",
        "/pagamentos",
        "/rankings",
        "/perfil"
    };

    public static readonly IReadOnlyList<string> AllowedPushHostSuffixes =
    [
        "fcm.googleapis.com",
        "push.services.mozilla.com",
        "push.apple.com",
        "notify.windows.com",
        "web.push.apple.com"
    ];

    public static string ValidateActionUrl(string? actionUrl)
    {
        var trimmed = (actionUrl ?? string.Empty).Trim();
        if (!AllowedActionUrls.Contains(trimmed))
            throw new DomainException("Destino de notificação não permitido.", "invalid_action_url");
        return trimmed;
    }

    public static (string Title, string Body) ValidateTitleAndBody(string? title, string? body)
    {
        var t = (title ?? string.Empty).Trim();
        var b = (body ?? string.Empty).Trim();
        if (t.Length is < 1 or > MaxTitleLength)
            throw new DomainException($"O título deve ter entre 1 e {MaxTitleLength} caracteres.", "validation_failed");
        if (b.Length is < 1 or > MaxBodyLength)
            throw new DomainException($"A mensagem deve ter entre 1 e {MaxBodyLength} caracteres.", "validation_failed");
        return (t, b);
    }

    public static string ValidateEndpoint(string? endpoint, bool allowLocalhostSimulator)
    {
        var trimmed = (endpoint ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxEndpointLength)
            throw new DomainException("Endereço de inscrição push inválido.", "invalid_subscription");

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new DomainException("Endereço de inscrição push inválido.", "invalid_subscription");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new DomainException("Endereço de inscrição push não pode conter credenciais.", "invalid_subscription");

        if (allowLocalhostSimulator
            && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Endereço de inscrição push deve usar HTTPS.", "invalid_subscription");

        // Só a porta HTTPS padrão e nunca IP literal: o destino tem de ser um dos serviços de push por nome.
        if (uri.Port != 443)
            throw new DomainException("Endereço de inscrição push deve usar a porta 443.", "invalid_subscription");
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            throw new DomainException("Serviço de push não autorizado.", "invalid_subscription");

        if (!IsAllowedPushHost(uri.Host))
            throw new DomainException("Serviço de push não autorizado.", "invalid_subscription");

        return trimmed;
    }

    private static readonly IPNetwork GlobalIpv6 = IPNetwork.Parse("2000::/3");

    // Política de saída de Web Push: prefixos de protocolo, transição e documentação
    // da IANA não são destinos de provedores, inclusive suas subalocações de uso especial.
    private static readonly IPNetwork[] SpecialIpv6 =
    [
        IPNetwork.Parse("2001::/23"),
        IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("2002::/16"),
        IPNetwork.Parse("2620:4f:8000::/48"),
        IPNetwork.Parse("3fff::/20")
    ];

    /// <summary>Filtra o IP usado na conexão, depois da resolução DNS.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // CGNAT
                || b[0] == 127
                || (b[0] == 169 && b[1] == 254)                  // link-local / metadados
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2))
                || (b[0] == 192 && b[1] == 88 && b[2] == 99)     // 6to4 anycast
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))   // benchmark
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // documentação
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                || b[0] >= 224);                                // multicast e reservados
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            && GlobalIpv6.Contains(address)
            && !SpecialIpv6.Any(prefix => prefix.Contains(address));
    }

    public static bool IsAllowedPushHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var suffix in AllowedPushHostSuffixes)
        {
            if (normalized == suffix || normalized.EndsWith("." + suffix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static (string P256dh, string Auth) ValidatePushKeys(string? p256dh, string? auth)
    {
        var p = (p256dh ?? string.Empty).Trim();
        var a = (auth ?? string.Empty).Trim();
        if (!TryDecodeBase64Url(p, out var pBytes) || pBytes.Length < 32 || pBytes.Length > 128)
            throw new DomainException("Chave p256dh inválida.", "invalid_subscription");
        if (!TryDecodeBase64Url(a, out var aBytes) || aBytes.Length < 12 || aBytes.Length > 64)
            throw new DomainException("Chave auth inválida.", "invalid_subscription");
        return (p, a);
    }

    public static bool TryDecodeBase64Url(string input, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(input)) return false;
        foreach (var c in input)
        {
            var valid = (c is >= 'A' and <= 'Z') || (c is >= 'a' and <= 'z') || (c is >= '0' and <= '9') || c is '-' or '_' or '=';
            if (!valid) return false;
        }
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: return false;
        }
        try
        {
            bytes = Convert.FromBase64String(padded);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string ToBase64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Primeiros 8 bytes (16 caracteres hex) do SHA-256 da chave pública VAPID (D4).</summary>
    public static string ComputeVapidKeyId(string publicKeyBase64Url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(publicKeyBase64Url));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static TimeSpan NextRetryDelay(int attempt) => attempt switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(15)
    };
}
