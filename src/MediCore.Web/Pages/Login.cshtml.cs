using System.ComponentModel.DataAnnotations;
using MediCore.Contracts;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediCore.Web.Pages;

public class LoginModel(IMediCoreApi api, TimeProvider clock) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet()
        => User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl()) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Error = "Ingresa un correo electrónico válido y tu contraseña.";
            return Page();
        }

        var result = await api.LoginAsync(Input.Email, Input.Password, ct);
        switch (result.Status)
        {
            case ApiStatus.Ok:
                var login = result.Value!;

                // La web es solo para el personal; el paciente usa la aplicación móvil.
                if (!Roles.Personal.Contains(login.User.Role))
                {
                    await api.LogoutAsync(login.AccessToken, ct);
                    Error = "Esta aplicación es para el personal. Los pacientes pueden ingresar desde la app móvil.";
                    return Page();
                }

                var (principal, properties) = SessionTokens.Create(login, clock);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
                return LocalRedirect(SafeReturnUrl());

            case ApiStatus.RateLimited:
                Error = "Demasiados intentos. Espera un minuto e inténtalo de nuevo.";
                return Page();

            case ApiStatus.Unavailable:
                Error = "El servicio no está disponible en este momento. Inténtalo de nuevo en unos minutos.";
                return Page();

            default:
                Error = "Correo o contraseña incorrectos, o la cuenta no está disponible temporalmente.";
                return Page();
        }
    }

    // Solo se aceptan destinos locales: evita redirecciones abiertas.
    private string SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

    public class LoginInput
    {
        [Required, EmailAddress, MaxLength(254)]
        public string Email { get; set; } = "";

        [Required, MaxLength(128)]
        public string Password { get; set; } = "";
    }
}
