using MediCore.Contracts.Clinical;
using MediCore.Contracts.Laboratory;
using MediCore.Contracts.Notifications;
using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;

namespace MediCore.Mobile.Core.ViewModels;

public sealed record LabResultItem(Guid Id, string ExamType, string DateText);

/// <summary>MOB-08: mis resultados de laboratorio.</summary>
public sealed class LabResultsViewModel : LoadableViewModel
{
    private readonly IPatientApi _api;
    private IReadOnlyList<LabResultItem> _items = [];

    public LabResultsViewModel(IPatientApi api, INavigator navigator) : base(navigator)
    {
        _api = api;
        OpenCommand = new AsyncCommand<LabResultItem>(item => navigator.GoToLabResultAsync(item.Id));
        RetryCommand = new AsyncCommand(LoadAsync);
    }

    public IReadOnlyList<LabResultItem> Items { get => _items; private set => Set(ref _items, value); }
    public override string EmptyMessage => "Aún no tienes resultados de laboratorio.";
    public AsyncCommand<LabResultItem> OpenCommand { get; }
    public AsyncCommand RetryCommand { get; }

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await _api.GetMyLabResultsAsync();
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudieron cargar tus resultados.");
            return;
        }

        Items = result.Value!.Select(r => new LabResultItem(r.Id, r.ExamType, Formats.Date(r.Date))).ToList();
        State = Items.Count == 0 ? ViewState.Empty : ViewState.Content;
    }
}

public sealed record ParameterItem(string Name, string Value, string Unit, string Reference)
{
    public string ValueWithUnit => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";
    public bool HasReference => !string.IsNullOrWhiteSpace(Reference);
    public string ReferenceText => $"Rango de referencia: {Reference}";
}

/// <summary>MOB-09: detalle de un resultado.</summary>
public sealed class LabResultDetailViewModel(IPatientApi api, INavigator navigator) : LoadableViewModel(navigator)
{
    private LabResultDto? _result;

    public Guid ResultId { get; set; }
    public string ExamType => _result?.ExamType ?? "";
    public string Subtitle => _result is null ? "" : $"{Formats.Date(_result.Date)} · Resultado de laboratorio";
    public IReadOnlyList<ParameterItem> Parameters { get; private set; } = [];
    public string Notes => string.IsNullOrWhiteSpace(_result?.Notes) ? "Sin observaciones." : _result!.Notes;
    private AsyncCommand? _retry;
    public AsyncCommand RetryCommand => _retry ??= new(LoadAsync);

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await api.GetLabResultAsync(ResultId);
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudo cargar el resultado.");
            return;
        }

        _result = result.Value;
        Parameters = _result!.Parameters.Select(p => new ParameterItem(p.Name, p.Value, p.Unit, p.ReferenceRange)).ToList();
        foreach (var name in new[] { nameof(ExamType), nameof(Subtitle), nameof(Parameters), nameof(Notes) }) Raise(name);
        State = ViewState.Content;
    }
}

public sealed record PrescriptionItemView(Guid Id, string DoctorName, string DateText, string Summary);

/// <summary>MOB-10: mis recetas.</summary>
public sealed class PrescriptionsViewModel : LoadableViewModel
{
    private readonly IPatientApi _api;
    private IReadOnlyList<PrescriptionItemView> _items = [];

    public PrescriptionsViewModel(IPatientApi api, INavigator navigator) : base(navigator)
    {
        _api = api;
        OpenCommand = new AsyncCommand<PrescriptionItemView>(item => navigator.GoToPrescriptionAsync(item.Id));
        RetryCommand = new AsyncCommand(LoadAsync);
    }

    public IReadOnlyList<PrescriptionItemView> Items { get => _items; private set => Set(ref _items, value); }
    public override string EmptyMessage => "Aún no tienes recetas.";
    public AsyncCommand<PrescriptionItemView> OpenCommand { get; }
    public AsyncCommand RetryCommand { get; }

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await _api.GetMyPrescriptionsAsync();
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudieron cargar tus recetas.");
            return;
        }

        Items = result.Value!.Select(p => new PrescriptionItemView(p.Id, p.DoctorName, Formats.Date(p.Date),
            p.Items.Count > 1 ? $"{p.Items[0].Medication} y {p.Items.Count - 1} más" : p.Items[0].Medication)).ToList();
        State = Items.Count == 0 ? ViewState.Empty : ViewState.Content;
    }
}

public sealed record MedicationItem(string Name, string Dose, string Frequency, string Duration, string Instructions);

/// <summary>MOB-11: detalle de una receta.</summary>
public sealed class PrescriptionDetailViewModel(IPatientApi api, INavigator navigator) : LoadableViewModel(navigator)
{
    private PrescriptionDto? _prescription;

    public Guid PrescriptionId { get; set; }
    public string Subtitle => _prescription is null ? "" : $"{_prescription.DoctorName} · {Formats.Date(_prescription.Date)}";
    public IReadOnlyList<MedicationItem> Medications { get; private set; } = [];
    private AsyncCommand? _retry;
    public AsyncCommand RetryCommand => _retry ??= new(LoadAsync);

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await api.GetPrescriptionAsync(PrescriptionId);
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudo cargar la receta.");
            return;
        }

        _prescription = result.Value;
        Medications = _prescription!.Items.Select(i => new MedicationItem(i.Medication, i.Dose, i.Frequency, i.Duration, i.Instructions)).ToList();
        Raise(nameof(Subtitle));
        Raise(nameof(Medications));
        State = ViewState.Content;
    }
}

public sealed class NotificationItem(NotificationDto dto) : ViewModelBase
{
    private bool _isRead = dto.IsRead;

    public Guid Id { get; } = dto.Id;
    public string Title { get; } = dto.Title;
    public string Body { get; } = dto.Body;
    public string TargetType { get; } = dto.TargetType;
    public Guid TargetId { get; } = dto.TargetId;
    public DateTime CreatedAtUtc { get; } = dto.CreatedAtUtc;
    public bool IsRead { get => _isRead; set { if (Set(ref _isRead, value)) { Raise(nameof(TitleText)); Raise(nameof(StateText)); } } }

    /// <summary>"● Tu resultado está disponible" mientras no se lee, como en el diseño.</summary>
    public string TitleText => IsRead ? Title : $"● {Title}";
    public string StateText => $"{(IsRead ? "Leída" : "Sin leer")} · {CreatedAtUtc.ToLocalTime():dd/MM/yyyy}";

    /// <summary>Solo los resultados de laboratorio tienen contenido relacionado hoy; otros tipos se muestran sin enlace.</summary>
    public bool HasTarget => TargetType == "LabResult";
}

/// <summary>MOB-12: notificaciones internas, con acceso al contenido relacionado.</summary>
public sealed class NotificationsViewModel : LoadableViewModel
{
    private readonly IPatientApi _api;
    private readonly INavigator _navigator;
    private IReadOnlyList<NotificationItem> _items = [];

    public NotificationsViewModel(IPatientApi api, INavigator navigator) : base(navigator)
    {
        _api = api;
        _navigator = navigator;
        OpenCommand = new AsyncCommand<NotificationItem>(OpenAsync);
        RetryCommand = new AsyncCommand(LoadAsync);
    }

    public IReadOnlyList<NotificationItem> Items { get => _items; private set => Set(ref _items, value); }
    public override string EmptyMessage => "No tienes notificaciones por ahora.";
    public AsyncCommand<NotificationItem> OpenCommand { get; }
    public AsyncCommand RetryCommand { get; }

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await _api.GetNotificationsAsync();
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudieron cargar tus notificaciones.");
            return;
        }

        Items = result.Value!.Items.Select(n => new NotificationItem(n)).ToList();
        State = Items.Count == 0 ? ViewState.Empty : ViewState.Content;
    }

    private async Task OpenAsync(NotificationItem item)
    {
        if (!item.IsRead)
        {
            var read = await _api.MarkNotificationReadAsync(item.Id);
            if (read.Status == ApiStatus.SessionExpired)
            {
                await _navigator.GoToLoginAsync(Messages.SessionExpired);
                return;
            }

            if (read.IsOk) item.IsRead = true; // si falla, se sigue mostrando como sin leer y se puede reintentar
        }

        if (item.TargetType == "LabResult") await _navigator.GoToLabResultAsync(item.TargetId);
    }
}
