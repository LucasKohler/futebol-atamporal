using System.Net;
using System.Security.Cryptography;
using FutebolAtemporal.Api;
using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure;
using FutebolAtemporal.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FutebolAtemporal.Api.Tests;

[CollectionDefinition("Web Push Console", DisableParallelization = true)]
public sealed class WebPushConsoleCollection;

[Collection("Web Push Console")]
public class WebPushConsoleLoggingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Configured_console_keeps_safe_warnings_and_drops_push_request_details(
        bool development, bool restoreFactoryLoggers)
    {
        const string deviceToken = "test-only-device-token";
        const string queryToken = "test-only-query-token";
        const string controlPath = "public-http-probe";
        var endpoint = $"https://fcm.googleapis.com/send/{deviceToken}?token={queryToken}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var capture = new StringWriter();
        var previousConsole = Console.Out;
        var transport = new FailingPushHandler();

        try
        {
            Console.SetOut(capture);
            var settings = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json");
            if (development) settings.AddJsonFile("appsettings.Development.json");
            // Exercise even verbose request diagnostics if a collector raises the factory's log level.
            settings.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:MinimumLevel:Override:System.Net.Http.HttpClient"] = "Verbose"
            });
            var configuration = settings.Build();
            var loggerConfiguration = new LoggerConfiguration();
            ApiLogging.Configure(loggerConfiguration, configuration);
            using var logger = loggerConfiguration.CreateLogger();

            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddSerilog(logger));
            services.AddInfrastructure(configuration);
            var (publicKey, privateKey) = WebPushOptions.GenerateP256KeyPair();
            services.AddSingleton(new WebPushOptions
            {
                Mode = WebPushMode.Live, PublicKey = publicKey, PrivateKey = privateKey, KeyId = "test"
            });
            var push = services.AddHttpClient(nameof(LiveWebPushSender))
                .ConfigurePrimaryHttpMessageHandler(() => transport);
            if (restoreFactoryLoggers) push.AddDefaultLogger();
            services.AddHttpClient("PublicLoggingProbe").ConfigurePrimaryHttpMessageHandler(() => new SuccessfulHandler());

            using var provider = services.BuildServiceProvider();
            var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
            using var control = clientFactory.CreateClient("PublicLoggingProbe");
            using var controlResponse = await control.GetAsync($"https://example.invalid/{controlPath}", timeout.Token);

            var sender = provider.GetRequiredService<LiveWebPushSender>();
            var (deviceKey, _) = WebPushOptions.GenerateP256KeyPair();
            var auth = NotificationPolicies.ToBase64Url(RandomNumberGenerator.GetBytes(16));
            var subscriptionId = Guid.NewGuid();
            var result = await sender.SendAsync(new(subscriptionId, endpoint, deviceKey, auth),
                new(Guid.NewGuid(), "Aviso", "Teste", "/", "tag", false), timeout.Token);

            Assert.NotNull(transport.Authorization);
            Assert.NotEmpty(transport.Authorization);
            Assert.Equal(endpoint, transport.Endpoint);
            var output = capture.ToString();
            Assert.Contains(controlPath, output);
            Assert.Contains(subscriptionId.ToString(), output);
            Assert.Contains(nameof(HttpRequestException), output);
            Assert.DoesNotContain(endpoint, output);
            Assert.DoesNotContain(deviceToken, output);
            Assert.DoesNotContain(queryToken, output);
            Assert.DoesNotContain(transport.Authorization, output);
            Assert.DoesNotContain(auth, output);
            Assert.DoesNotContain(deviceKey, output);
            Assert.DoesNotContain("Authorization", output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(WebPushSendOutcome.TransientError, result.Outcome);
        }
        finally
        {
            Console.SetOut(previousConsole);
        }
    }

    private sealed class FailingPushHandler : HttpMessageHandler
    {
        public string? Endpoint { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Endpoint = request.RequestUri?.AbsoluteUri;
            Authorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(" ", values) : null;
            return Task.FromException<HttpResponseMessage>(new HttpRequestException(
                "Synthetic transport failure for " + Endpoint,
                new InvalidOperationException("Authorization: " + Authorization)));
        }
    }

    private sealed class SuccessfulHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
