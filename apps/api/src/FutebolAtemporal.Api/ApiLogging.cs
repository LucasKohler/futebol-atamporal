using FutebolAtemporal.Infrastructure.Notifications;
using Serilog;
using Serilog.Events;

namespace FutebolAtemporal.Api;

public static class ApiLogging
{
    private const string WebPushHttpCategory = "System.Net.Http.HttpClient." + nameof(LiveWebPushSender);

    public static void Configure(LoggerConfiguration logger, IConfiguration configuration)
    {
        // Se os loggers da fábrica forem reativados, descarte seus eventos antes de qualquer sink.
        // O caminho da URL de push identifica o aparelho. Os avisos sanitizados do sender permanecem.
        logger.Filter.ByExcluding(IsWebPushHttpLog)
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console();
    }

    private static bool IsWebPushHttpLog(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var source)
        && source is ScalarValue { Value: string category }
        && (category == WebPushHttpCategory || category.StartsWith(WebPushHttpCategory + ".", StringComparison.Ordinal));
}
