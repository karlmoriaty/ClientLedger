using System.Security.Cryptography;
using ClientLedger.Licensing;

namespace ClientLedger.Tests;

public sealed class LicenseTests
{
    private static (string PrivatePem, string PublicPem) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    private static LicenseDocument Issue(LicensePayload payload, string privatePem)
    {
        var bytes = LicenseVerifier.PayloadBytes(payload);
        return new LicenseDocument(
            Convert.ToBase64String(bytes),
            LicenseVerifier.SignPayload(bytes, privatePem));
    }

    [Fact]
    public void SignedLicense_VerifiesAndRoundTripsPayload()
    {
        var (privatePem, publicPem) = CreateKeyPair();
        var document = Issue(
            new LicensePayload
            {
                LicenseId = "CL-TEST-0001",
                Buyer = "测试客户",
                Edition = "pro",
                IssuedUtc = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatesUntil = new DateTimeOffset(2027, 11, 1, 0, 0, 0, TimeSpan.Zero),
                Features = ["crm", "crm.quote.export"]
            },
            privatePem);

        Assert.True(LicenseVerifier.TryVerify(document, publicPem, out var payload, out var error), error);
        Assert.NotNull(payload);
        Assert.Equal("CL-TEST-0001", payload!.LicenseId);
        Assert.Equal("测试客户", payload.Buyer);
        Assert.Contains("crm.quote.export", payload.Features);
    }

    [Fact]
    public void TamperedPayload_FailsVerification()
    {
        var (privatePem, publicPem) = CreateKeyPair();
        var document = Issue(new LicensePayload { LicenseId = "CL-1", Buyer = "A" }, privatePem);

        var tampered = Convert.ToBase64String(
            LicenseVerifier.PayloadBytes(new LicensePayload { LicenseId = "CL-1", Buyer = "HACKED" }));

        var attack = document with { Payload = tampered };

        Assert.False(LicenseVerifier.TryVerify(attack, publicPem, out _, out var error));
        Assert.Contains("签名", error);
    }

    [Fact]
    public void SignatureFromAnotherKeyPair_FailsVerification()
    {
        var (privatePem, _) = CreateKeyPair();
        var (_, otherPublicPem) = CreateKeyPair();

        var document = Issue(new LicensePayload { LicenseId = "CL-2", Buyer = "B" }, privatePem);

        Assert.False(LicenseVerifier.TryVerify(document, otherPublicPem, out _, out _));
    }

    [Fact]
    public void WrongProduct_FailsVerification()
    {
        var (privatePem, publicPem) = CreateKeyPair();
        var document = Issue(new LicensePayload { Product = "other-app", LicenseId = "CL-3" }, privatePem);

        Assert.False(LicenseVerifier.TryVerify(document, publicPem, out _, out var error));
        Assert.Contains("其它产品", error);
    }

    [Fact]
    public void GarbageBase64_FailsWithoutThrowing()
    {
        var document = new LicenseDocument("not-base64", "also-not-base64");

        Assert.False(LicenseVerifier.TryVerify(document, out _, out var error));
        Assert.Contains("Base64", error);
    }
}