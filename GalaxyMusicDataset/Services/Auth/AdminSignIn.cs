using System.Security.Claims;
using GalaxyMusicDataset.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services.Auth;

public sealed class AdminSignIn
{
    private readonly string _username;
    private readonly string? _passwordHash;
    private readonly PasswordHasher<string> _hasher = new();

    public AdminSignIn(IOptions<AuthOptions> options)
        : this(options.Value)
    {
    }

    public AdminSignIn(AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _username = string.IsNullOrWhiteSpace(options.Username) ? "admin" : options.Username.Trim();
        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            _passwordHash = _hasher.HashPassword(_username, options.Password);
            IsConfigured = true;
        }
    }

    public bool IsConfigured { get; }

    public string Username => _username;

    public bool Verify(string? username, string? password)
    {
        if (!IsConfigured || _passwordHash is null || string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (!string.Equals(username?.Trim(), _username, StringComparison.Ordinal))
        {
            return false;
        }

        return _hasher.VerifyHashedPassword(_username, _passwordHash, password)
            != PasswordVerificationResult.Failed;
    }

    public Task SignInAsync(HttpContext http)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, _username)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }

    public static Task SignOutAsync(HttpContext http) =>
        http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
