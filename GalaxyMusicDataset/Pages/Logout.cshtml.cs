using GalaxyMusicDataset.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalaxyMusicDataset.Pages;

[AllowAnonymous]
public class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Dashboard");

    public async Task<IActionResult> OnPostAsync()
    {
        await AdminSignIn.SignOutAsync(HttpContext);
        return RedirectToPage("/Dashboard");
    }
}
