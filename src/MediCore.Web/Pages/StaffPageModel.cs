using MediCore.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediCore.Web.Pages;

/// <summary>Base de las páginas del personal que consumen la API.</summary>
public abstract class StaffPageModel : PageModel
{
    /// <summary>La API ya no reconoce la sesión (revocada, vencida o cambio de rol): se cierra también la cookie.</summary>
    protected async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }

    /// <summary>Resultados comunes de la API que no son errores de la pantalla: sesión caída, sin permiso o no encontrado.</summary>
    protected async Task<IActionResult?> HandleCommonAsync(ApiResult result) => result.Status switch
    {
        ApiStatus.Unauthorized => await EndSessionAsync(),
        ApiStatus.Forbidden => RedirectToPage("/AccesoDenegado"),
        ApiStatus.NotFound => NotFound(),
        _ => null,
    };
}

public static class Display
{
    /// <summary>Iniciales para el avatar, como en el diseño: sin "Dr./Dra." y con las dos primeras palabras.</summary>
    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("Dr." or "Dra.")).Take(2);
        return string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
    }
}
