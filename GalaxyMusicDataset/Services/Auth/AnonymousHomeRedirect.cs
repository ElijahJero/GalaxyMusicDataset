namespace GalaxyMusicDataset.Services.Auth;

public static class AnonymousHomeRedirect
{
    public static bool ShouldRedirectToDashboard(string method, string? path, bool isAuthenticated)
    {
        if (isAuthenticated || !HttpMethods.IsGet(method))
        {
            return false;
        }

        return path is "/" or "/Index" or "/index";
    }
}
