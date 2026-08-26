using System.Globalization;
using RepoPulse.Core.Repositories;

namespace RepoPulse.UnitTests;

public class FavoriteRepositoryIdentifierTests
{
    [Fact]
    public void TryNormalize_TrimsWhitespaceAndLowercasesFullName()
    {
        var succeeded = FavoriteRepositoryIdentifier.TryNormalize("  MustafaNazli  ", "  RepoPulse  ", out var identity);

        Assert.True(succeeded);
        Assert.Equal("MustafaNazli", identity.Owner);
        Assert.Equal("RepoPulse", identity.Name);
        Assert.Equal("mustafanazli/repopulse", identity.NormalizedFullName);
    }

    [Theory]
    [InlineData(null, "RepoPulse")]
    [InlineData("", "RepoPulse")]
    [InlineData("   ", "RepoPulse")]
    [InlineData("mustafanazli", null)]
    [InlineData("mustafanazli", "")]
    [InlineData("mustafanazli", "   ")]
    public void TryNormalize_EmptyOrWhitespaceOwnerOrName_Fails(string? owner, string? name)
    {
        var succeeded = FavoriteRepositoryIdentifier.TryNormalize(owner, name, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void TryNormalize_DifferentCasingOfSameRepository_ProducesIdenticalNormalizedFullName()
    {
        FavoriteRepositoryIdentifier.TryNormalize("mustafanazli", "RepoPulse", out var first);
        FavoriteRepositoryIdentifier.TryNormalize("MustafaNazli", "repopulse", out var second);

        Assert.Equal(first.NormalizedFullName, second.NormalizedFullName);
    }

    // Turkish culture famously lowercases 'I' to a dotless 'ı', not 'i' — a
    // real, well-known pitfall for ToLower()/ToLowerInvariant look-alike
    // bugs. Running this under Turkish culture and asserting the exact
    // ASCII-lowercase result proves normalization does not depend on
    // CultureInfo.CurrentCulture.
    [Fact]
    public void NormalizeFullName_IsUnaffectedByCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            var normalized = FavoriteRepositoryIdentifier.NormalizeFullName("MustafaNazli/RepoPulse");

            Assert.Equal("mustafanazli/repopulse", normalized);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void TryNormalize_BuildsOwnerSlashNameNormalizedFullName()
    {
        FavoriteRepositoryIdentifier.TryNormalize("owner", "name", out var identity);

        Assert.Equal("owner/name", identity.NormalizedFullName);
    }

    // ---- Account login normalization (cross-account isolation fix) ----

    [Fact]
    public void TryNormalizeAccountLogin_TrimsWhitespaceAndLowercases()
    {
        var succeeded = FavoriteRepositoryIdentifier.TryNormalizeAccountLogin("  MustafaNazli  ", out var normalized);

        Assert.True(succeeded);
        Assert.Equal("mustafanazli", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalizeAccountLogin_EmptyOrWhitespace_Fails(string? accountLogin)
    {
        var succeeded = FavoriteRepositoryIdentifier.TryNormalizeAccountLogin(accountLogin, out _);

        Assert.False(succeeded);
    }

    [Fact]
    public void TryNormalizeAccountLogin_DifferentCasingOfSameLogin_ProducesIdenticalResult()
    {
        FavoriteRepositoryIdentifier.TryNormalizeAccountLogin("mustafanazli", out var first);
        FavoriteRepositoryIdentifier.TryNormalizeAccountLogin("MustafaNazli", out var second);

        Assert.Equal(first, second);
    }

    // Same Turkish-culture pitfall as NormalizeFullName above, now for
    // account logins.
    [Fact]
    public void TryNormalizeAccountLogin_IsUnaffectedByCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            FavoriteRepositoryIdentifier.TryNormalizeAccountLogin("MustafaNazli", out var normalized);

            Assert.Equal("mustafanazli", normalized);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void TryNormalizeAccountLogin_TreatsInputAsOpaqueText_NoSpecialCasingBeyondTrimAndLowercase()
    {
        // Not a security check per se — just documents that this function
        // has exactly one job (trim + lowercase) and never inspects/derives
        // meaning from its input; any opaque string normalizes the same
        // mechanical way, regardless of shape.
        var succeeded = FavoriteRepositoryIdentifier.TryNormalizeAccountLogin("Some-OPAQUE.Value123", out var normalized);

        Assert.True(succeeded);
        Assert.Equal("some-opaque.value123", normalized);
    }
}
