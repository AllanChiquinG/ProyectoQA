using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediCore.Web.Pages;

/// <summary>Página para respuestas vacías de error (404, 400, etc.); conserva el código HTTP original.</summary>
[IgnoreAntiforgeryToken]
public class EstadoModel : PageModel
{
    public int Codigo { get; private set; } = 404;
    public string Titulo { get; private set; } = "No encontramos esa página";
    public string Mensaje { get; private set; } = "";

    public void OnGet(int codigo = 404)
    {
        Codigo = codigo is >= 400 and <= 599 ? codigo : 404;
        (Titulo, Mensaje) = Codigo switch
        {
            404 => ("No encontramos lo que buscas", "El registro no existe o no tienes acceso a él."),
            400 => ("La solicitud no es válida", "Vuelve a la pantalla anterior e inténtalo de nuevo."),
            403 => ("Acceso restringido", "Tu rol no tiene permiso para ver esta sección."),
            429 => ("Demasiados intentos", "Espera un momento e inténtalo de nuevo."),
            _ => ("Algo salió mal", "No pudimos completar la solicitud. Inténtalo de nuevo en unos minutos."),
        };
        Response.StatusCode = Codigo;
    }
}
