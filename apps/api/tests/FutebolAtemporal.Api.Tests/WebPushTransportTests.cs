using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;

namespace FutebolAtemporal.Api.Tests;

public class WebPushTransportTests
{
    [Fact]
    public async Task Dns_resolution_skips_special_addresses_and_returns_only_a_validated_destination()
    {
        var publicAddress = IPAddress.Parse("2607:f8b0:4004:800::200a");
        var target = await WebPushTransport.ResolveDestinationAsync(new("fcm.googleapis.com", 443), (host, _) =>
        {
            Assert.Equal("fcm.googleapis.com", host);
            return Task.FromResult(new[] { IPAddress.Parse("2001:db8::1"), IPAddress.Parse("::ffff:10.0.0.1"), publicAddress });
        }, default);

        Assert.Equal(publicAddress, target);
    }

    [Fact]
    public async Task Public_mapped_address_uses_an_ipv4_socket_destination()
    {
        var target = await WebPushTransport.ResolveDestinationAsync(new("fcm.googleapis.com", 443),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("::ffff:142.250.79.10") }), default);
        Assert.Equal(IPAddress.Parse("142.250.79.10"), target);
    }

    [Fact]
    public async Task Private_dns_result_blocks_the_http_path_without_creating_a_url_activity()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var url = "https://fcm.googleapis.com/fcm/send/" + suffix;
        var taggedUrls = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("url.full") is string value && value.Contains(suffix, StringComparison.Ordinal))
                    taggedUrls.Enqueue(value);
            }
        };
        ActivitySource.AddActivityListener(listener);

        Task<IPAddress[]> Resolve(string host, CancellationToken _)
        {
            Assert.Equal("fcm.googleapis.com", host);
            return Task.FromResult(new[] { IPAddress.Parse("64:ff9b::a00:1"), IPAddress.Parse("::ffff:10.0.0.1") });
        }

        using var handler = WebPushTransport.CreateHandler(Resolve);
        using var client = new HttpClient(handler);
        var blocked = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));
        Assert.Contains("endereço não público", blocked.ToString());
        Assert.Empty(taggedUrls);

        // Controle positivo: o pipeline HTTP padrão emite o caminho de uma requisição idêntica.
        using var instrumentedHandler = WebPushTransport.CreateHandler(Resolve);
        instrumentedHandler.ActivityHeadersPropagator = DistributedContextPropagator.CreateDefaultPropagator();
        using var instrumented = new HttpClient(instrumentedHandler);
        await Assert.ThrowsAsync<HttpRequestException>(() => instrumented.GetAsync(url));
        Assert.Equal(url, Assert.Single(taggedUrls));
    }

    [Fact]
    public async Task Network_exception_details_never_enter_push_logs_or_the_result()
    {
        const string endpoint = "https://fcm.googleapis.com/fcm/send/synthetic-device-token?key=synthetic-secret";
        using var handler = new FailingHandler();
        using var client = new HttpClient(handler);
        var (publicKey, privateKey) = WebPushOptions.GenerateP256KeyPair();
        var options = new WebPushOptions { Mode = WebPushMode.Live, PublicKey = publicKey, PrivateKey = privateKey, KeyId = "test" };
        var logger = new WarningCapture();
        var sender = new LiveWebPushSender(client, options, logger);
        var (deviceKey, _) = WebPushOptions.GenerateP256KeyPair();
        var auth = NotificationPolicies.ToBase64Url(RandomNumberGenerator.GetBytes(16));
        var subscriptionId = Guid.NewGuid();

        var result = await sender.SendAsync(new(subscriptionId, endpoint, deviceKey, auth),
            new(Guid.NewGuid(), "Aviso", "Teste", "/", "tag", false));

        Assert.Equal(WebPushSendOutcome.TransientError, result.Outcome);
        var log = Assert.Single(logger.Logs);
        Assert.Null(log.Exception);
        Assert.Contains(nameof(HttpRequestException), log.Message);
        Assert.Contains(subscriptionId.ToString(), log.Message);
        Assert.DoesNotContain("synthetic-device-token", log.Message);
        Assert.DoesNotContain("synthetic-secret", result.Reason);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Outer " + request.RequestUri, new InvalidOperationException("Inner " + request.RequestUri)));
    }

    private sealed class WarningCapture : ILogger<LiveWebPushSender>
    {
        public List<(string Message, Exception? Exception)> Logs { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Logs.Add((formatter(state, exception), exception));
    }
}
