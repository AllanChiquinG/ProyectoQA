using MediCore.Contracts;
using MediCore.Contracts.Scheduling;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Agenda;

[Authorize(Roles = Roles.Recepcion)]
public class CitaModel(IMediCoreApi api, ClinicCalendar calendar) : StaffPageModel
{
    [FromRoute]
    public Guid Id { get; set; }

    public AppointmentDto? Appointment { get; private set; }

    public bool CanCancel => Appointment is { Status: AppointmentStatuses.Confirmed } a
                             && a.Date.ToDateTime(TimeOnly.Parse(a.Time)) > calendar.Now;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await api.GetAppointmentAsync(Id, ct);
        if (await HandleCommonAsync(result) is { } handled) return handled;
        if (!result.IsOk) return RedirectToPage("/Agenda/Index");

        Appointment = result.Value;
        return Page();
    }

    public async Task<IActionResult> OnPostCancelarAsync(CancellationToken ct)
    {
        var result = await api.CancelAppointmentAsync(Id, ct);
        if (result.Status == ApiStatus.Unauthorized) return await EndSessionAsync();

        TempData["Toast"] = result.Status switch
        {
            ApiStatus.Ok => "La cita fue cancelada",
            ApiStatus.Conflict => result.Message ?? "No se puede cancelar esta cita.",
            ApiStatus.NotFound => "La cita ya no existe.",
            _ => "No se pudo cancelar la cita. Inténtalo de nuevo.",
        };
        return RedirectToPage(new { id = Id });
    }
}
