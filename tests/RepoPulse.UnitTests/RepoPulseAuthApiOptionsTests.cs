using RepoPulse.Core.Authentication;

namespace RepoPulse.UnitTests;

public class RepoPulseAuthApiOptionsTests
{
    [Fact]
    public void StagingBaseAddress_IsTheExpectedLiveAzureStagingUrl()
    {
        Assert.Equal(
            "https://ca-repopulse-authapi-staging.orangefield-f1a16f03.polandcentral.azurecontainerapps.io",
            RepoPulseAuthApiOptions.StagingBaseAddress);
    }

    [Fact]
    public void StagingBaseAddress_IsAWellFormedHttpsUri()
    {
        var uri = new Uri(RepoPulseAuthApiOptions.StagingBaseAddress, UriKind.Absolute);

        Assert.Equal("https", uri.Scheme);
    }

    [Fact]
    public void StagingBaseAddress_HostIsNotALocalDevelopmentHost()
    {
        var uri = new Uri(RepoPulseAuthApiOptions.StagingBaseAddress, UriKind.Absolute);

        // This is the exact check MauiProgram.CreateAuthApiHttpClient() uses
        // to decide whether to attach the custom development-certificate
        // validator. It must be false for the staging host, so staging
        // always gets ordinary platform TLS validation, never the
        // localhost/10.0.2.2-only dev-cert callback.
        Assert.False(DevelopmentCertificateValidator.IsLocalDevelopmentHost(uri.Host));
    }

    [Fact]
    public void DevelopmentBaseAddress_HostIsALocalDevelopmentHost()
    {
        var uri = new Uri(RepoPulseAuthApiOptions.DevelopmentBaseAddress, UriKind.Absolute);

        Assert.True(DevelopmentCertificateValidator.IsLocalDevelopmentHost(uri.Host));
    }
}
