using MediCore.Contracts;
using MediCore.Web.Models;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages;

/// <summary>WEB-11: agenda del médico autenticado. La API solo devuelve las citas de su propia agenda.</summary>
[Authorize(Roles = Roles.Medico)]
public class MiAgendaModel(IMediCoreApi api, ClinicCalendar calendar) : StaffPageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    public AgendaDay? Day { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var me = await api.GetMyDoctorAsync(ct);
        if (me.Status == ApiStatus.NotFound)
        {
            Error = "Tu cuenta todavía no está vinculada a un médico. Pide al administrador que la configure.";
            return Page();
        }

        if (me.Status is ApiStatus.Unauthorized or ApiStatus.Forbidden && await HandleCommonAsync(me) is { } handled) return handled;
        if (!me.IsOk)
        {
            Error = "No se pudo cargar tu agenda. Inténtalo de nuevo.";
            return Page();
        }

        var (result, day) = await AgendaBuilder.BuildAsync(api, calendar, me.Value!, Date ?? calendar.Today, "/MiAgenda", isReception: false, [], ct);
        if (await HandleCommonAsync(result) is { } common) return common;
        if (day is null)
        {
            Error = "No se pudo cargar tu agenda. Inténtalo de nuevo.";
            return Page();
        }

        Day = day;
        return Page();
    }
}
