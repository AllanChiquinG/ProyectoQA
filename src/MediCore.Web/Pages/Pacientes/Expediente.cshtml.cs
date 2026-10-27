using MediCore.Contracts;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Laboratory;
using MediCore.Contracts.Patients;
using MediCore.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Web.Pages.Pacientes;

/// <summary>WEB-12: expediente del paciente. Solo el médico con relación de atención; es un expediente compartido entre los médicos que atienden al paciente.</summary>
[Authorize(Roles = Roles.Medico)]
public class ExpedienteModel(IMediCoreApi api) : StaffPageModel
{
    public const string TabConsultas = "Consultas";
    public const string TabRecetas = "Recetas";
    public const string TabLaboratorio = "Laboratorio";
    public static readonly string[] Tabs = [TabConsultas, TabRecetas, TabLaboratorio];

    [FromRoute]
    public Guid Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    /// <summary>Cita desde la que se abrió el expediente (si vino de la agenda); se propone al registrar la consulta.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Cita { get; set; }

    public PatientDto? Patient { get; private set; }
    public IReadOnlyList<ConsultationDto> Consultations { get; private set; } = [];
    public IReadOnlyList<PrescriptionDto> Prescriptions { get; private set; } = [];
    public IReadOnlyList<LabResultSummaryDto> LabResults { get; private set; } = [];
    public string? Error { get; private set; }
    public string ActiveTab => Tabs.Contains(Tab) ? Tab! : TabConsultas;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var patient = await api.GetPatientAsync(Id, ct);
        if (await HandleCommonAsync(patient) is { } handled) return handled;
        if (!patient.IsOk)
        {
            Error = "No se pudo cargar el expediente. Inténtalo de nuevo.";
            return Page();
        }

        Patient = patient.Value;

        if (ActiveTab == TabConsultas)
        {
            var consultations = await api.GetPatientConsultationsAsync(Id, ct);
            if (await HandleCommonAsync(consultations) is { } c) return c;
            if (consultations.IsOk) Consultations = consultations.Value!; else Error = "No se pudieron cargar las consultas.";
        }
        else if (ActiveTab == TabRecetas)
        {
            var prescriptions = await api.GetPatientPrescriptionsAsync(Id, ct);
            if (await HandleCommonAsync(prescriptions) is { } p) return p;
            if (prescriptions.IsOk) Prescriptions = prescriptions.Value!; else Error = "No se pudieron cargar las recetas.";
        }
        else
        {
            var labs = await api.GetPatientLabResultsAsync(Id, ct);
            if (await HandleCommonAsync(labs) is { } l) return l;
            if (labs.IsOk) LabResults = labs.Value!; else Error = "No se pudieron cargar los resultados de laboratorio.";
        }

        return Page();
    }
}
