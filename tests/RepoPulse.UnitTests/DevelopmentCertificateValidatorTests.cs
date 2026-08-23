using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class DevelopmentCertificateValidatorTests
{
    private static X509Certificate2 CreateSelfSignedCert(string subjectName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static X509Certificate2 ValidLocalhostCert() =>
        CreateSelfSignedCert("localhost", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

    [Theory]
    [InlineData("10.0.2.2")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void ShouldAccept_ValidCertKnownHost_ReturnsTrue(string host)
    {
        using var cert = ValidLocalhostCert();

        Assert.True(DevelopmentCertificateValidator.ShouldAccept(host, cert));
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("api.github.com")]
    [InlineData("192.168.1.1")]
    public void ShouldAccept_UnexpectedHost_ReturnsFalse(string host)
    {
        using var cert = ValidLocalhostCert();

        Assert.False(DevelopmentCertificateValidator.ShouldAccept(host, cert));
    }

    [Fact]
    public void ShouldAccept_NullHost_ReturnsFalse()
    {
        using var cert = ValidLocalhostCert();

        Assert.False(DevelopmentCertificateValidator.ShouldAccept(null, cert));
    }

    [Fact]
    public void ShouldAccept_NullCertificate_ReturnsFalse()
    {
        Assert.False(DevelopmentCertificateValidator.ShouldAccept("10.0.2.2", null));
    }

    [Fact]
    public void ShouldAccept_WrongSubject_ReturnsFalse()
    {
        using var cert = CreateSelfSignedCert("attacker.example.com", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        Assert.False(DevelopmentCertificateValidator.ShouldAccept("10.0.2.2", cert));
    }

    [Fact]
    public void ShouldAccept_ExpiredCertificate_ReturnsFalse()
    {
        using var cert = CreateSelfSignedCert("localhost", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

        Assert.False(DevelopmentCertificateValidator.ShouldAccept("10.0.2.2", cert));
    }

    [Fact]
    public void ShouldAccept_NotYetValidCertificate_ReturnsFalse()
    {
        using var cert = CreateSelfSignedCert("localhost", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(365));

        Assert.False(DevelopmentCertificateValidator.ShouldAccept("10.0.2.2", cert));
    }

    [Fact]
    public void ShouldAccept_ViaHttpRequestMessageOverload_ReadsHostFromRequestUri()
    {
        using var cert = ValidLocalhostCert();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://10.0.2.2:7082/oauth/github/exchange");

        var accepted = DevelopmentCertificateValidator.ShouldAccept(request, cert, chain: null, sslPolicyErrors: System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(accepted);
    }

    [Fact]
    public void ShouldAccept_ViaHttpRequestMessageOverload_WrongHost_ReturnsFalse()
    {
        using var cert = ValidLocalhostCert();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");

        var accepted = DevelopmentCertificateValidator.ShouldAccept(request, cert, chain: null, sslPolicyErrors: System.Net.Security.SslPolicyErrors.None);

        Assert.False(accepted);
    }

    [Theory]
    [InlineData("10.0.2.2")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("LOCALHOST")]
    public void IsLocalDevelopmentHost_KnownLocalHost_ReturnsTrue(string host)
    {
        Assert.True(DevelopmentCertificateValidator.IsLocalDevelopmentHost(host));
    }

    [Theory]
    [InlineData("ca-repopulse-authapi-staging.orangefield-f1a16f03.polandcentral.azurecontainerapps.io")]
    [InlineData("api.github.com")]
    [InlineData("evil.example.com")]
    [InlineData(null)]
    public void IsLocalDevelopmentHost_NonLocalOrNullHost_ReturnsFalse(string? host)
    {
        Assert.False(DevelopmentCertificateValidator.IsLocalDevelopmentHost(host));
    }
}
