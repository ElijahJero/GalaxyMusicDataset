using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalaxyMusicDataset.Pages;

[AllowAnonymous]
public class ApiModel : PageModel
{
    public void OnGet()
    {
    }
}
