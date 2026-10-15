using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediCore.Web.Pages;

public class AccesoDenegadoModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
