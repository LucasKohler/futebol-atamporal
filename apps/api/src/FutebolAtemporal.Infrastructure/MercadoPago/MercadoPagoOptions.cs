using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FutebolAtemporal.Infrastructure.MercadoPago;

public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    public string? AccessToken { get; set; }
    public string? WebhookSecret { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.mercadopago.com";
    public string? NotificationUrl { get; set; }
    public bool? UseSandboxSimulator { get; set; }
    public int DefaultExpirationMinutes { get; set; } = 60;

    public static MercadoPagoOptions Resolve(IConfiguration config, IHostEnvironment env)
    {
        var opts = new MercadoPagoOptions();
        config.GetSection(SectionName).Bind(opts);

        if (string.IsNullOrWhiteSpace(opts.AccessToken))
            opts.AccessToken = FirstNonEmpty(config["MP_ACCESS_TOKEN"], Environment.GetEnvironmentVariable("MP_ACCESS_TOKEN"));

        if (string.IsNullOrWhiteSpace(opts.WebhookSecret))
            opts.WebhookSecret = FirstNonEmpty(config["MP_WEBHOOK_SECRET"], Environment.GetEnvironmentVariable("MP_WEBHOOK_SECRET"));

        if (string.IsNullOrWhiteSpace(opts.ApiBaseUrl))
            opts.ApiBaseUrl = "https://api.mercadopago.com";

        if (opts.UseSandboxSimulator is null)
            opts.UseSandboxSimulator = string.IsNullOrWhiteSpace(opts.AccessToken) && !env.IsProduction();

        return opts;
    }

    public bool ShouldUseSimulator => UseSandboxSimulator == true;
    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);
    public bool HasWebhookSecret => !string.IsNullOrWhiteSpace(WebhookSecret);

    public string EffectiveMode => ShouldUseSimulator
        ? "Simulator"
        : HasAccessToken ? "Live" : "Unconfigured";

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}