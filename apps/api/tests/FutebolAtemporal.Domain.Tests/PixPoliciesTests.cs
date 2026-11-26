using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class PixPoliciesTests
{
    [Fact]
    public void SignatureValidator_accepts_valid_hmac_with_and_without_request_id()
    {
        const string secret = "whsec_test_123456";
        const string dataId = "1234567890";
        const string reqId = "req-abc-999";
        const string ts = "1732600000";

        var manifest = MercadoPagoSignatureValidator.BuildManifest(dataId, reqId, ts);
        Assert.Equal("id:1234567890;request-id:req-abc-999;ts:1732600000;", manifest);

        var sig = MercadoPagoSignatureValidator.ComputeHmacSha256Hex(manifest, secret);
        Assert.True(MercadoPagoSignatureValidator.Validate($"ts={ts},v1={sig}", reqId, dataId, secret));
        Assert.True(MercadoPagoSignatureValidator.Validate($"v1={sig}, ts={ts}", reqId, dataId.ToUpperInvariant(), secret));

        var manifestNoReq = MercadoPagoSignatureValidator.BuildManifest(dataId, null, ts);
        Assert.Equal("id:1234567890;ts:1732600000;", manifestNoReq);
        var sigNoReq = MercadoPagoSignatureValidator.ComputeHmacSha256Hex(manifestNoReq, secret);
        Assert.True(MercadoPagoSignatureValidator.Validate($"ts={ts},v1={sigNoReq}", null, dataId, secret));
    }

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("", "secret")]
    [InlineData("ts=123,v1=deadbeef", "secret")]
    [InlineData("ts=123,v1=not-hex-zzz", "secret")]
    [InlineData("ts=123", "secret")]
    [InlineData("ts=123,v1=00", "")]
    public void SignatureValidator_rejects_missing_tampered_or_malformed_headers(string? header, string secret)
    {
        Assert.False(MercadoPagoSignatureValidator.Validate(header, "req-1", "999", secret));
    }

    [Fact]
    public void PixChargePolicy_checks_reusability_and_expiration()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var active = new PixCharge { Status = PixChargeStatus.Pending, ExpiresAt = now.AddMinutes(10) };
        var expired = new PixCharge { Status = PixChargeStatus.Pending, ExpiresAt = now.AddMinutes(-1) };
        var approved = new PixCharge { Status = PixChargeStatus.Approved, ExpiresAt = now.AddMinutes(10) };

        Assert.True(PixChargePolicy.IsReusable(active, now));
        Assert.False(PixChargePolicy.IsExpired(active, now));

        Assert.False(PixChargePolicy.IsReusable(expired, now));
        Assert.True(PixChargePolicy.IsExpired(expired, now));

        Assert.False(PixChargePolicy.IsReusable(approved, now));
        Assert.False(PixChargePolicy.IsExpired(approved, now));
    }

    [Theory]
    [InlineData("approved", PixChargeStatus.Approved)]
    [InlineData("APPROVED", PixChargeStatus.Approved)]
    [InlineData("cancelled", PixChargeStatus.Cancelled)]
    [InlineData("rejected", PixChargeStatus.Cancelled)]
    [InlineData("refunded", PixChargeStatus.Cancelled)]
    [InlineData("pending", PixChargeStatus.Pending)]
    [InlineData("in_process", PixChargeStatus.Pending)]
    public void MapProviderStatus_maps_mercado_pago_statuses(string raw, PixChargeStatus expected)
    {
        Assert.Equal(expected, PixChargePolicy.MapProviderStatus(raw));
    }

    [Fact]
    public void ValidateSettings_and_amount_enforce_domain_bounds()
    {
        PixChargePolicy.ValidateSettings(150m, 25m, 30);
        PixChargePolicy.ValidateSettings(0m, 0m, 1440);
        PixChargePolicy.ValidateChargeAmount(0.01m);

        Assert.Throws<DomainException>(() => PixChargePolicy.ValidateSettings(-1m, 25m, 60));
        Assert.Throws<DomainException>(() => PixChargePolicy.ValidateSettings(150m, -5m, 60));
        Assert.Throws<DomainException>(() => PixChargePolicy.ValidateSettings(150m, 25m, 29));
        Assert.Throws<DomainException>(() => PixChargePolicy.ValidateSettings(150m, 25m, 1441));
        Assert.Throws<DomainException>(() => PixChargePolicy.ValidateChargeAmount(0m));
    }
}
