using System.Net.Mail;
using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;

namespace MediCore.Mobile.Core.ViewModels;

/// <summary>MOB-01: inicio de sesión. Valida el formulario antes de llamar a la API; la contraseña nunca se guarda ni se registra.</summary>
public sealed class LoginViewModel : ViewModelBase
{
    private readonly IPatientApi _api;
    private readonly INavigator _navigator;
    private string _email = "";
    private string _password = "";
    private string? _emailError;
    private string? _passwordError;
    private string? _formError;
    private bool _isBusy;

    public LoginViewModel(IPatientApi api, INavigator navigator)
    {
        _api = api;
        _navigator = navigator;
        LoginCommand = new AsyncCommand(LoginAsync, () => !IsBusy);
    }

    public string Email { get => _email; set => Set(ref _email, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string? EmailError { get => _emailError; private set => Set(ref _emailError, value); }
    public string? PasswordError { get => _passwordError; private set => Set(ref _passwordError, value); }

    /// <summary>Error general (credenciales, conexión) o aviso (sesión vencida) arriba del formulario.</summary>
    public string? FormError { get => _formError; set => Set(ref _formError, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) LoginCommand.RaiseCanExecuteChanged();
        }
    }

    public AsyncCommand LoginCommand { get; }

    /// <summary>Muestra un aviso al volver a esta pantalla (por ejemplo, la sesión venció) y limpia la contraseña.</summary>
    public void Prepare(string? notice)
    {
        Password = "";
        PasswordError = null;
        EmailError = null;
        FormError = notice;
    }

    public bool Validate()
    {
        EmailError = string.IsNullOrWhiteSpace(Email) ? "Ingresa tu correo electrónico."
            : !IsEmail(Email.Trim()) ? "Ingresa un correo electrónico válido." : null;
        PasswordError = string.IsNullOrEmpty(Password) ? "Ingresa tu contraseña." : null;
        return EmailError is null && PasswordError is null;
    }

    private static bool IsEmail(string text) => MailAddress.TryCreate(text, out var address) && address.Address == text && text.Contains('.');

    private async Task LoginAsync()
    {
        FormError = null;
        if (!Validate()) return;

        IsBusy = true;
        try
        {
            var result = await _api.LoginAsync(Email.Trim(), Password);
            Password = ""; // no se conserva en memoria más de lo necesario
            switch (result.Status)
            {
                case ApiStatus.Ok:
                    await _navigator.GoToHomeAsync();
                    break;
                case ApiStatus.InvalidCredentials:
                    FormError = "El correo o la contraseña no son correctos.";
                    break;
                default:
                    FormError = Messages.For(result, "No se pudo iniciar sesión. Inténtalo de nuevo.");
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>MOB-02: inicio con la próxima cita y accesos. Cierra sesión desde aquí.</summary>
public sealed class HomeViewModel : LoadableViewModel
{
    private readonly IPatientApi _api;
    private readonly INavigator _navigator;
    private readonly IClinicClock _clock;
    private string _greeting = "Hola";
    private AppointmentItem? _next;
    private int _unread;

    public HomeViewModel(IPatientApi api, INavigator navigator, IClinicClock clock) : base(navigator)
    {
        _api = api;
        _navigator = navigator;
        _clock = clock;
        LogoutCommand = new AsyncCommand(LogoutAsync);
        BookCommand = new AsyncCommand(() => _navigator.GoToDoctorsAsync());
        OpenNextCommand = new AsyncCommand(() => _next is null ? _navigator.GoToDoctorsAsync() : _navigator.GoToAppointmentAsync(_next.Id));
        AppointmentsCommand = new AsyncCommand(() => _navigator.GoToAppointmentsAsync());
        LabResultsCommand = new AsyncCommand(() => _navigator.GoToLabResultsAsync());
        PrescriptionsCommand = new AsyncCommand(() => _navigator.GoToPrescriptionsAsync());
        NotificationsCommand = new AsyncCommand(() => _navigator.GoToNotificationsAsync());
        RetryCommand = new AsyncCommand(LoadAsync);
    }

    public string Greeting { get => _greeting; private set => Set(ref _greeting, value); }
    public AppointmentItem? NextAppointment { get => _next; private set { if (Set(ref _next, value)) { Raise(nameof(HasNext)); Raise(nameof(NextTitle)); Raise(nameof(NextDetail)); Raise(nameof(NextButtonText)); } } }
    public bool HasNext => _next is not null;
    public int UnreadCount { get => _unread; private set { if (Set(ref _unread, value)) Raise(nameof(HasUnread)); } }
    public bool HasUnread => _unread > 0;

    // Textos de la tarjeta "Tu próxima cita" (como en el diseño; sin cita se invita a solicitar una).
    public string NextTitle => _next?.DoctorName ?? "Estamos para cuidarte";
    public string NextDetail => _next is null ? "Solicita una cita con tu médico." : $"{Formats.LongDateTime(_next.Date, _next.Time)}\n{_next.Specialty}";
    public string NextButtonText => _next is null ? "Solicitar cita →" : "Ver mi cita →";

    public AsyncCommand LogoutCommand { get; }
    public AsyncCommand BookCommand { get; }
    public AsyncCommand OpenNextCommand { get; }
    public AsyncCommand AppointmentsCommand { get; }
    public AsyncCommand LabResultsCommand { get; }
    public AsyncCommand PrescriptionsCommand { get; }
    public AsyncCommand NotificationsCommand { get; }
    public AsyncCommand RetryCommand { get; }

    public override async Task LoadAsync()
    {
        State = ViewState.Loading;
        var appointments = await _api.GetMyAppointmentsAsync();
        if (!appointments.IsOk)
        {
            await FailAsync(appointments, "No se pudo cargar tu información.");
            return;
        }

        var name = await _api.GetUserNameAsync();
        if (name is not null) Greeting = $"Hola, {Formats.FirstName(name)}";

        var now = _clock.Now;
        NextAppointment = appointments.Value!
            .Where(a => a.Status == "Confirmed" && AppointmentItem.StartsAt(a) >= now)
            .OrderBy(AppointmentItem.StartsAt)
            .Select(AppointmentItem.From)
            .FirstOrDefault();
        State = ViewState.Content;

        // Datos secundarios: si fallan no impiden mostrar el inicio.
        var unread = await _api.GetUnreadCountAsync();
        UnreadCount = unread.IsOk ? unread.Value!.Count : 0;
    }

    private async Task LogoutAsync()
    {
        await _api.LogoutAsync();
        await _navigator.GoToLoginAsync();
    }
}
