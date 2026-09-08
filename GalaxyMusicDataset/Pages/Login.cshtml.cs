using GalaxyMusicDataset.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalaxyMusicDataset.Pages;

[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class LoginModel(AdminSignIn adminSignIn) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? Error { get; private set; }

    public bool IsConfigured => adminSignIn.IsConfigured;

    public string? ReturnUrl { get; private set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(SafeReturn(returnUrl));
        }

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!adminSignIn.Verify(Username, Password))
        {
            Error = adminSignIn.IsConfigured
                ? "Invalid username or password."
                : "Admin login is not configured on this server.";
            return Page();
        }

        await adminSignIn.SignInAsync(HttpContext);
        return LocalRedirect(SafeReturn(returnUrl));
    }

    private string SafeReturn(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return Url.Page("/Index") ?? "/";
    }
}
