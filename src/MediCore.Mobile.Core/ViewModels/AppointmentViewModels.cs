using MediCore.Contracts.Doctors;
using MediCore.Contracts.Scheduling;
using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;

namespace MediCore.Mobile.Core.ViewModels;

public sealed record AppointmentItem(Guid Id, string DoctorName, string Specialty, DateOnly Date, string Time, bool IsCancelled)
{
    public string When => Formats.DateTime(Date, Time);
    public string StatusText => IsCancelled ? "Cancelada" : "Confirmada";

    public static DateTime StartsAt(AppointmentDto a) => a.Date.ToDateTime(TimeOnly.ParseExact(a.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    public static AppointmentItem From(AppointmentDto a) => new(a.Id, a.DoctorName, a.DoctorSpecialty, a.Date, a.Time, a.Status == AppointmentStatuses.Cancelled);
}

/// <summary>MOB-03: mis citas.</summary>
public sealed class AppointmentsViewModel : LoadableViewModel
{
    private readonly IPatientApi _api;
    private IReadOnlyList<AppointmentItem> _items = [];

    public AppointmentsViewModel(IPatientApi api, INavigator navigator) : base(navigator)
    {
        _api = api;
        OpenCommand = new AsyncCommand<AppointmentItem>(item => navigator.GoToAppointmentAsync(item.Id));
        BookCommand = new AsyncCommand(() => navigator.GoToDoctorsAsync());
        RetryCommand = new AsyncCommand(LoadAsync);
    }

    public IReadOnlyList<AppointmentItem> Items { get => _items; private set => Set(ref _items, value); }
    public override string EmptyMessage => "Aún no tienes citas. Solicita una con tu médico.";
    public AsyncCommand<AppointmentItem> OpenCommand { get; }
    public AsyncCommand BookCommand { get; }
    public AsyncCommand RetryCommand { get; }

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await _api.GetMyAppointmentsAsync();
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudieron cargar tus citas.");
            return;
        }

        Items = result.Value!.Select(AppointmentItem.From).ToList();
        State = Items.Count == 0 ? ViewState.Empty : ViewState.Content;
    }
}

/// <summary>MOB-07: detalle de una cita, con cancelación.</summary>
public sealed class AppointmentDetailViewModel : LoadableViewModel
{
    private readonly IPatientApi _api;
    private readonly IDialogs _dialogs;
    private readonly INavigator _navigator;
    private AppointmentDto? _appointment;
    private string? _actionError;

    public AppointmentDetailViewModel(IPatientApi api, INavigator navigator, IDialogs dialogs) : base(navigator)
    {
        _api = api;
        _dialogs = dialogs;
        _navigator = navigator;
        CancelCommand = new AsyncCommand(CancelAsync, () => CanCancel);
        RetryCommand = new AsyncCommand(LoadAsync);
    }

    public Guid AppointmentId { get; set; }
    public string DoctorName => _appointment?.DoctorName ?? "";
    public string Specialty => _appointment?.DoctorSpecialty ?? "";
    public string DateText => _appointment is null ? "" : Formats.Date(_appointment.Date);
    public string TimeText => _appointment is null ? "" : $"{_appointment.Time} h";
    public string StatusText => _appointment is null ? "" : Formats.Status(_appointment.Status);
    public bool CanCancel => _appointment is { Status: AppointmentStatuses.Confirmed };

    /// <summary>Error de la última acción (por ejemplo, la cancelación no se pudo completar).</summary>
    public string? ActionError { get => _actionError; private set => Set(ref _actionError, value); }

    public AsyncCommand CancelCommand { get; }
    public AsyncCommand RetryCommand { get; }

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await _api.GetAppointmentAsync(AppointmentId);
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudo cargar la cita.");
            return;
        }

        Apply(result.Value!);
        State = ViewState.Content;
    }

    private void Apply(AppointmentDto appointment)
    {
        _appointment = appointment;
        foreach (var name in new[] { nameof(DoctorName), nameof(Specialty), nameof(DateText), nameof(TimeText), nameof(StatusText), nameof(CanCancel) }) Raise(name);
        CancelCommand.RaiseCanExecuteChanged();
    }

    private async Task CancelAsync()
    {
        ActionError = null;
        if (!await _dialogs.ConfirmAsync("Cancelar cita", "¿Seguro que quieres cancelar esta cita? El horario quedará libre para otras personas.", "Sí, cancelar", "No, conservarla"))
            return;

        var result = await _api.CancelAppointmentAsync(AppointmentId);
        if (result.Status == ApiStatus.SessionExpired)
        {
            await _navigator.GoToLoginAsync(Messages.SessionExpired);
            return;
        }

        if (!result.IsOk)
        {
            ActionError = Messages.For(result, "No se pudo cancelar la cita. Inténtalo de nuevo.");
            return;
        }

        await _dialogs.ToastAsync("Cita cancelada");
        await LoadAsync(); // la cancelación es idempotente en la API; se vuelve a leer el estado real
    }
}

public sealed record DoctorItem(Guid Id, string FullName, string Specialty)
{
    public string Initials => Formats.Initials(FullName);
}

/// <summary>Un horario de la lista. <see cref="IsSelected"/> cambia cuando la persona lo elige.</summary>
public sealed class SlotItem(string time, bool available) : ViewModelBase
{
    private bool _isSelected;

    public string Time { get; } = time;
    public bool Available { get; } = available;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>
/// MOB-04, MOB-05 y MOB-06: solicitar una cita en tres pasos (médico → fecha y horario → confirmación). Es un solo objeto para todo el recorrido,
/// así que lo elegido se conserva al avanzar, al volver y cuando un horario se ocupa antes de confirmar.
/// </summary>
public sealed class BookingViewModel : LoadableViewModel
{
    private readonly IPatientApi _api;
    private readonly INavigator _navigator;
    private readonly IDialogs _dialogs;
    private readonly IClinicClock _clock;
    private IReadOnlyList<DoctorItem> _doctors = [];
    private DoctorItem? _doctor;
    private DateOnly _date;
    private IReadOnlyList<SlotItem> _slots = [];
    private string? _time;
    private string? _slotsMessage;
    private bool _slotsLoading;
    private string? _confirmError;
    private string? _timeError;
    private bool _isBusy;
    private string? _idempotencyKey;
    private string _patientName = "";

    public BookingViewModel(IPatientApi api, INavigator navigator, IDialogs dialogs, IClinicClock clock) : base(navigator)
    {
        _api = api;
        _navigator = navigator;
        _dialogs = dialogs;
        _clock = clock;
        _date = clock.Today;
        SelectDoctorCommand = new AsyncCommand<DoctorItem>(SelectDoctorAsync);
        SelectTimeCommand = new AsyncCommand<SlotItem>(item => { SelectTime(item); return Task.CompletedTask; });
        ContinueCommand = new AsyncCommand(ContinueAsync);
        ConfirmCommand = new AsyncCommand(ConfirmAsync, () => !IsBusy);
        RetryCommand = new AsyncCommand(LoadAsync);
    }

    // ---- Paso 1: médico ----
    public IReadOnlyList<DoctorItem> Doctors { get => _doctors; private set => Set(ref _doctors, value); }
    public override string EmptyMessage => "Por ahora no hay médicos disponibles.";
    public AsyncCommand<DoctorItem> SelectDoctorCommand { get; }
    public AsyncCommand RetryCommand { get; }

    // ---- Paso 2: fecha y horario ----
    public DoctorItem? Doctor { get => _doctor; private set { if (Set(ref _doctor, value)) { Raise(nameof(DoctorName)); Raise(nameof(DoctorSpecialty)); } } }
    public string DoctorName => _doctor?.FullName ?? "";
    public string DoctorSpecialty => _doctor?.Specialty ?? "";
    public DateOnly MinDate => _clock.Today;
    public DateOnly MaxDate => _clock.LastBookableDate;
    public DateOnly Date => _date;
    public IReadOnlyList<SlotItem> Slots { get => _slots; private set => Set(ref _slots, value); }
    public string? SelectedTime
    {
        get => _time;
        private set
        {
            if (!Set(ref _time, value)) return;
            foreach (var slot in _slots) slot.IsSelected = slot.Time == value;
            Raise(nameof(TimeText));
            Raise(nameof(HasSelectedTime));
        }
    }
    public bool HasSelectedTime => _time is not null;
    public string? TimeError { get => _timeError; private set => Set(ref _timeError, value); }
    public string? SlotsMessage { get => _slotsMessage; private set => Set(ref _slotsMessage, value); }
    public bool SlotsLoading { get => _slotsLoading; private set => Set(ref _slotsLoading, value); }
    public AsyncCommand<SlotItem> SelectTimeCommand { get; }
    public AsyncCommand ContinueCommand { get; }

    // ---- Paso 3: confirmación ----
    public string PatientName { get => _patientName; private set => Set(ref _patientName, value); }
    public string DateText => Formats.Date(_date);
    public string TimeText => _time is null ? "" : $"{_time} h";
    public string? ConfirmError { get => _confirmError; private set => Set(ref _confirmError, value); }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) ConfirmCommand.RaiseCanExecuteChanged(); } }
    public AsyncCommand ConfirmCommand { get; }

    /// <summary>Clave de idempotencia de la reserva en curso (visible para pruebas). Se conserva en los reintentos y se renueva al cambiar la solicitud.</summary>
    public string? IdempotencyKey => _idempotencyKey;

    /// <summary>Empieza una solicitud nueva (se llama al abrir MOB-04 desde fuera del recorrido).</summary>
    public void Reset()
    {
        Doctor = null;
        _date = _clock.Today;
        Raise(nameof(Date));
        Raise(nameof(DateText));
        Slots = [];
        SelectedTime = null;
        TimeError = null;
        SlotsMessage = null;
        ConfirmError = null;
        _idempotencyKey = null;
    }

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var result = await _api.GetDoctorsAsync();
        if (!result.IsOk)
        {
            await FailAsync(result, "No se pudo cargar la lista de médicos.");
            return;
        }

        PatientName = await _api.GetUserNameAsync() ?? "";
        Doctors = result.Value!.Select(d => new DoctorItem(d.Id, d.FullName, d.Specialty)).ToList();
        State = Doctors.Count == 0 ? ViewState.Empty : ViewState.Content;
    }

    private async Task SelectDoctorAsync(DoctorItem doctor)
    {
        if (_doctor?.Id != doctor.Id)
        {
            Doctor = doctor;
            SelectedTime = null;
            Slots = [];
            _idempotencyKey = null;
        }

        await _navigator.GoToSlotsAsync();
    }

    /// <summary>Cambia la fecha (desde el selector) y recarga los horarios sin perder al médico elegido.</summary>
    public async Task SetDateAsync(DateOnly date)
    {
        if (date < MinDate) date = MinDate;
        if (date > MaxDate) date = MaxDate;
        _date = date;
        Raise(nameof(Date));
        Raise(nameof(DateText));
        SelectedTime = null;
        _idempotencyKey = null;
        await LoadSlotsAsync();
    }

    public async Task LoadSlotsAsync()
    {
        if (_doctor is null) return;
        SlotsLoading = true;
        SlotsMessage = null;
        TimeError = null;
        try
        {
            var result = await _api.GetAvailabilityAsync(_doctor.Id, _date);
            if (result.Status == ApiStatus.SessionExpired)
            {
                await _navigator.GoToLoginAsync(Messages.SessionExpired);
                return;
            }

            if (!result.IsOk)
            {
                Slots = [];
                SlotsMessage = Messages.For(result, "No se pudieron cargar los horarios.");
                return;
            }

            Slots = result.Value!.Slots.Select(s => new SlotItem(s.Time, s.Available) { IsSelected = s.Time == _time }).ToList();
            if (!result.Value.IsWorkingDay) SlotsMessage = "El médico no atiende ese día. Elige otra fecha.";
            else if (!Slots.Any(s => s.Available)) SlotsMessage = "No quedan horarios disponibles ese día. Elige otra fecha.";

            // Si el horario elegido ya no está libre, se descarta sin tocar lo demás.
            if (_time is not null && !Slots.Any(s => s.Time == _time && s.Available)) SelectedTime = null;
        }
        finally
        {
            SlotsLoading = false;
        }
    }

    private void SelectTime(SlotItem slot)
    {
        if (!slot.Available) return;
        SelectedTime = slot.Time;
        TimeError = null;
        _idempotencyKey = null; // otra hora = otra solicitud
    }

    private async Task ContinueAsync()
    {
        if (_doctor is null)
        {
            await _navigator.GoToDoctorsAsync();
            return;
        }

        if (_time is null)
        {
            TimeError = "Selecciona un horario para continuar.";
            return;
        }

        ConfirmError = null;
        await _navigator.GoToConfirmAsync();
    }

    private async Task ConfirmAsync()
    {
        if (_doctor is null || _time is null) return;
        ConfirmError = null;
        IsBusy = true;
        try
        {
            // La misma clave en cada reintento de ESTA solicitud: si la primera llegó pero la respuesta se perdió, la API devuelve la cita ya creada.
            _idempotencyKey ??= Guid.NewGuid().ToString("N");
            var request = new CreateAppointmentRequest(null, _doctor.Id, _date, _time);
            var result = await _api.CreateAppointmentAsync(request, _idempotencyKey);

            switch (result.Status)
            {
                case ApiStatus.Ok:
                    var created = result.Value!;
                    Reset();
                    await _dialogs.ToastAsync("Cita confirmada");
                    await _navigator.GoToAppointmentAsync(created.Id);
                    break;
                case ApiStatus.SessionExpired:
                    await _navigator.GoToLoginAsync(Messages.SessionExpired);
                    break;
                case ApiStatus.Conflict:
                    // Otra persona ocupó el horario: se conserva médico y fecha, se descarta solo la hora y se vuelve a elegir.
                    _idempotencyKey = null;
                    SelectedTime = null;
                    await LoadSlotsAsync();
                    SlotsMessage = Messages.SlotTaken;
                    ConfirmError = Messages.SlotTaken;
                    await _navigator.GoBackAsync();
                    break;
                case ApiStatus.Offline:
                    // La clave se conserva: reintentar no duplica la cita.
                    ConfirmError = "No se pudo confirmar por falta de conexión. Puedes reintentar: tu cita no se duplicará.";
                    break;
                case ApiStatus.Invalid:
                    _idempotencyKey = null;
                    ConfirmError = Messages.For(result, "No se pudo confirmar la cita. Revisa los datos.");
                    break;
                default:
                    ConfirmError = Messages.For(result, "No se pudo confirmar la cita. Inténtalo de nuevo.");
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
