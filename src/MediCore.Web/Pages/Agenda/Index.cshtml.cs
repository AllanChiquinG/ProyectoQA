using MediCore.Contracts;
using MediCore.Web.Models;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Agenda;

[Authorize(Roles = Roles.Recepcion)]
public class IndexModel(IMediCoreApi api, ClinicCalendar calendar) : StaffPageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? DoctorId { get; set; }

    public AgendaDay? Day { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var doctors = await api.GetDoctorsAsync(null, ct);
        if (await HandleCommonAsync(doctors) is { } handled) return handled;
        if (!doctors.IsOk)
        {
            Error = "No se pudo cargar la agenda. Inténtalo de nuevo.";
            return Page();
        }

        var doctor = doctors.Value!.FirstOrDefault(d => d.Id == DoctorId) ?? doctors.Value!.FirstOrDefault();
        if (doctor is null)
        {
            Error = "Todavía no hay médicos registrados.";
            return Page();
        }

        var date = Date ?? calendar.Today;
        var (result, day) = await AgendaBuilder.BuildAsync(api, calendar, doctor, date, "/Agenda/Index", isReception: true, doctors.Value!, ct);
        if (await HandleCommonAsync(result) is { } common) return common;
        if (day is null)
        {
            Error = "No se pudo cargar la agenda. Inténtalo de nuevo.";
            return Page();
        }

        Day = day;
        return Page();
    }
}
