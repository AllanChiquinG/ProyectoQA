using MediCore.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediCore.Web.Pages;

public class LogoutModel(IMediCoreApi api) : PageModel
{
    // Cerrar sesión cambia estado: solo por POST (con token antifalsificación) y nunca por GET.
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (accessToken is not null)
            await api.LogoutAsync(accessToken, ct);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}
