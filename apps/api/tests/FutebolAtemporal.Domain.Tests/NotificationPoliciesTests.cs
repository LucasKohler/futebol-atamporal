using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class NotificationPoliciesTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/partida")]
    [InlineData("/pagamentos")]
    [InlineData("/rankings")]
    [InlineData("/perfil")]
    [InlineData(" /partida ")]
    public void Allowed_action_urls_pass(string url)
    {
        var normalized = NotificationPolicies.ValidateActionUrl(url);
        Assert.Equal(url.Trim(), normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.com")]
    [InlineData("https://evil.com/partida")]
    [InlineData("/admin")]
    [InlineData("/partida?x=1")]
    public void Disallowed_action_urls_throw_invalid_action_url(string url)
    {
        var ex = Assert.Throws<DomainException>(() => NotificationPolicies.ValidateActionUrl(url));
        Assert.Equal("invalid_action_url", ex.Code);
    }

    [Fact]
    public void Title_and_body_lengths_are_enforced()
    {
        var (t, b) = NotificationPolicies.ValidateTitleAndBody("  Aviso  ", "  Corpo  ");
        Assert.Equal("Aviso", t);
        Assert.Equal("Corpo", b);

        Assert.Equal("validation_failed", Assert.Throws<DomainException>(() =>
            NotificationPolicies.ValidateTitleAndBody("", "ok")).Code);
        Assert.Equal("validation_failed", Assert.Throws<DomainException>(() =>
            NotificationPolicies.ValidateTitleAndBody(new string('a', 121), "ok")).Code);
        Assert.Equal("validation_failed", Assert.Throws<DomainException>(() =>
            NotificationPolicies.ValidateTitleAndBody("ok", new string('b', 501))).Code);
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/xyz")]
    [InlineData("https://web.push.apple.com/Q123")]
    [InlineData("https://wns2-bl2p.notify.windows.com/w/?token=123")]
    public void Allowed_push_endpoints_pass(string endpoint)
    {
        Assert.Equal(endpoint, NotificationPolicies.ValidateEndpoint(endpoint, allowLocalhostSimulator: false));
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://evilfcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://user:pass@fcm.googleapis.com/fcm/send/abc")]
    [InlineData("http://localhost:5000/push")]
    [InlineData("not-a-url")]
    public void Unsafe_or_unknown_push_endpoints_are_rejected(string endpoint)
    {
        var ex = Assert.Throws<DomainException>(() =>
            NotificationPolicies.ValidateEndpoint(endpoint, allowLocalhostSimulator: false));
        Assert.Equal("invalid_subscription", ex.Code);
    }

    [Fact]
    public void Localhost_http_endpoint_is_allowed_only_in_simulator()
    {
        var ep = "http://localhost:9999/sim-push";
        Assert.Equal(ep, NotificationPolicies.ValidateEndpoint(ep, allowLocalhostSimulator: true));
        Assert.Throws<DomainException>(() => NotificationPolicies.ValidateEndpoint(ep, allowLocalhostSimulator: false));
    }

    [Fact]
    public void KeyId_and_retry_schedule_match_contract()
    {
        var id1 = NotificationPolicies.ComputeVapidKeyId("BTestKey123");
        var id2 = NotificationPolicies.ComputeVapidKeyId("BTestKey123");
        Assert.Equal(16, id1.Length);
        Assert.Equal(id1, id2);
        Assert.Equal(TimeSpan.FromMinutes(1), NotificationPolicies.NextRetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(5), NotificationPolicies.NextRetryDelay(2));
        Assert.Equal(TimeSpan.FromMinutes(15), NotificationPolicies.NextRetryDelay(3));
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com:80/fcm/send/abc")]
    [InlineData("https://142.250.0.1/fcm/send/abc")]
    [InlineData("https://[2001:4860::1]/fcm/send/abc")]
    public void Endpoint_requires_port_443_and_a_push_hostname(string endpoint)
        => Assert.Throws<DomainException>(() => NotificationPolicies.ValidateEndpoint(endpoint, allowLocalhostSimulator: false));

    [Fact]
    public void Endpoint_with_explicit_default_port_is_accepted()
    {
        const string ep = "https://fcm.googleapis.com:443/fcm/send/abc";
        Assert.Equal(ep, NotificationPolicies.ValidateEndpoint(ep, allowLocalhostSimulator: false));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("142.250.79.10", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2607:f8b0:4004:800::200a", true)]
    public void Only_public_addresses_are_valid_push_destinations(string ip, bool expected)
        => Assert.Equal(expected, NotificationPolicies.IsPublicAddress(System.Net.IPAddress.Parse(ip)));
}
