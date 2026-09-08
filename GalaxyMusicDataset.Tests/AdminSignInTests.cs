using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Services.Auth;

namespace GalaxyMusicDataset.Tests;

public class AdminSignInTests
{
    [Fact]
    public void Verify_succeeds_with_matching_credentials()
    {
        var signIn = new AdminSignIn(new AuthOptions { Username = "admin", Password = "s3cret" });
        Assert.True(signIn.IsConfigured);
        Assert.True(signIn.Verify("admin", "s3cret"));
    }

    [Fact]
    public void Verify_trims_submitted_username()
    {
        var signIn = new AdminSignIn(new AuthOptions { Username = "admin", Password = "s3cret" });
        Assert.True(signIn.Verify("  admin  ", "s3cret"));
    }

    [Fact]
    public void Verify_fails_with_wrong_password()
    {
        var signIn = new AdminSignIn(new AuthOptions { Username = "admin", Password = "s3cret" });
        Assert.False(signIn.Verify("admin", "wrong"));
        Assert.False(signIn.Verify("admin", ""));
        Assert.False(signIn.Verify("admin", null));
    }

    [Fact]
    public void Verify_fails_with_wrong_username()
    {
        var signIn = new AdminSignIn(new AuthOptions { Username = "admin", Password = "s3cret" });
        Assert.False(signIn.Verify("someone", "s3cret"));
        Assert.False(signIn.Verify("Admin", "s3cret"));
        Assert.False(signIn.Verify(null, "s3cret"));
    }

    [Fact]
    public void Unset_password_is_not_configured_and_never_verifies()
    {
        var empty = new AdminSignIn(new AuthOptions { Username = "admin", Password = "" });
        var missing = new AdminSignIn(new AuthOptions { Username = "admin", Password = null });
        var whitespace = new AdminSignIn(new AuthOptions { Username = "admin", Password = "   " });

        Assert.False(empty.IsConfigured);
        Assert.False(missing.IsConfigured);
        Assert.False(whitespace.IsConfigured);
        Assert.False(empty.Verify("admin", "anything"));
        Assert.False(missing.Verify("admin", "anything"));
        Assert.False(whitespace.Verify("admin", "anything"));
    }

    [Fact]
    public void Blank_username_defaults_to_admin()
    {
        var signIn = new AdminSignIn(new AuthOptions { Username = "  ", Password = "s3cret" });
        Assert.Equal("admin", signIn.Username);
        Assert.True(signIn.Verify("admin", "s3cret"));
    }
}

public class AnonymousHomeRedirectTests
{
    [Theory]
    [InlineData("GET", "/", false, true)]
    [InlineData("GET", "/Index", false, true)]
    [InlineData("GET", "/index", false, true)]
    [InlineData("GET", "/", true, false)]
    [InlineData("GET", "/Dashboard", false, false)]
    [InlineData("POST", "/", false, false)]
    public void Redirects_anonymous_home_get_to_dashboard(string method, string path, bool authenticated, bool expected)
    {
        Assert.Equal(expected, AnonymousHomeRedirect.ShouldRedirectToDashboard(method, path, authenticated));
    }
}
