using System.Text;

namespace FutebolAtemporal.Domain.Services;

public static class PhoneNormalizer
{
    public static bool TryNormalize(string? raw, out string phone, out string error)
    {
        phone = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Informe o celular.";
            return false;
        }

        var digits = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsDigit(ch))
                digits.Append(ch);
        }

        var value = digits.ToString();
        if (value.StartsWith("55", StringComparison.Ordinal) && value.Length is 12 or 13)
            value = value[2..];

        if (value.Length is < 10 or > 11)
        {
            error = "Celular inválido. Use DDD + número.";
            return false;
        }

        phone = value;
        return true;
    }

    public static string FormatBr(string digits)
    {
        if (digits.Length == 11)
            return $"({digits[..2]}) {digits[2..7]}-{digits[7..]}";
        if (digits.Length == 10)
            return $"({digits[..2]}) {digits[2..6]}-{digits[6..]}";
        return digits;
    }
}
