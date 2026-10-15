using MediCore.Contracts;
using MediCore.Contracts.Auth;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediCore.Web.Pages;

[Authorize(Roles = Roles.Administrador)]
public class UsuariosModel(IMediCoreApi api) : PageModel
{
    public IReadOnlyList<UserDto> Users { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await api.GetUsersAsync(ct);
        if (result.Status == ApiStatus.Unauthorized) return await EndSessionAsync();
        if (!result.IsOk)
        {
            Error = "No se pudo cargar la lista de usuarios. Inténtalo de nuevo.";
            return Page();
        }

        Users = result.Value!;
        return Page();
    }

    public async Task<IActionResult> OnPostCambiarAsync(Guid id, string role, CancellationToken ct)
    {
        var result = await api.ChangeRoleAsync(id, role, ct);
        if (result.Status == ApiStatus.Unauthorized) return await EndSessionAsync();

        TempData["Toast"] = result.Status switch
        {
            ApiStatus.Ok => "Rol actualizado. Se cerraron las sesiones activas de esa persona.",
            ApiStatus.Conflict => "No puedes cambiar tu propio rol.",
            ApiStatus.NotFound => "El usuario ya no existe.",
            ApiStatus.BadRequest => "El rol seleccionado no es válido.",
            _ => "No se pudo actualizar el rol. Inténtalo de nuevo.",
        };
        return RedirectToPage();
    }

    // La API ya no reconoce la sesión (por ejemplo, otro administrador cambió el rol): se cierra también la cookie.
    private async Task<IActionResult> EndSessionAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}
