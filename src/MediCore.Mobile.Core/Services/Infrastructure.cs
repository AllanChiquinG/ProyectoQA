using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MediCore.Mobile.Core.Api;

namespace MediCore.Mobile.Core.Services;

/// <summary>Hora de la clínica (misma zona que la API: America/Guatemala). La agenda y las citas se expresan en esa hora, no en la del teléfono.</summary>
public interface IClinicClock
{
    DateTime Now { get; }
    DateOnly Today { get; }

    /// <summary>Último día en que se puede reservar (la API lo valida; la app solo evita ofrecer fechas imposibles).</summary>
    DateOnly LastBookableDate { get; }
}

public sealed class ClinicClock(TimeProvider time, string timeZoneId = "America/Guatemala", int maxDaysAhead = 90) : IClinicClock
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    public DateTime Now => TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime;

    public DateOnly Today => DateOnly.FromDateTime(Now);

    public DateOnly LastBookableDate => Today.AddDays(maxDaysAhead);
}

/// <summary>Navegación entre pantallas. La implementa la capa MAUI (Shell); los ViewModels solo piden destinos.</summary>
public interface INavigator
{
    /// <summary>Vuelve al inicio de sesión (MOB-01), opcionalmente con un aviso (por ejemplo, "tu sesión venció").</summary>
    Task GoToLoginAsync(string? notice = null);

    Task GoToHomeAsync();
    Task GoToAppointmentsAsync();
    Task GoToAppointmentAsync(Guid id);
    Task GoToDoctorsAsync();
    Task GoToSlotsAsync();
    Task GoToConfirmAsync();
    Task GoToLabResultsAsync();
    Task GoToLabResultAsync(Guid id);
    Task GoToPrescriptionsAsync();
    Task GoToPrescriptionAsync(Guid id);
    Task GoToNotificationsAsync();
    Task GoBackAsync();
}

/// <summary>Avisos y confirmaciones al usuario.</summary>
public interface IDialogs
{
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    Task ToastAsync(string message);
}

/// <summary>Textos de error en español para cada estado de la API.</summary>
public static class Messages
{
    public const string Offline = "No hay conexión con el servicio. Revisa tu conexión e inténtalo de nuevo.";
    public const string SessionExpired = "Tu sesión venció. Inicia sesión de nuevo para continuar.";
    public const string SlotTaken = "Este horario ya está ocupado. Selecciona otro para continuar.";
    public const string Unavailable = "El servicio no está disponible en este momento. Inténtalo de nuevo más tarde.";

    public static string For(ApiResult result, string fallback) => result.Status switch
    {
        ApiStatus.Offline => Offline,
        ApiStatus.SessionExpired => SessionExpired,
        ApiStatus.RateLimited => "Demasiados intentos. Espera un momento e inténtalo de nuevo.",
        ApiStatus.Forbidden => "No tienes permiso para ver esta información.",
        ApiStatus.NotFound => "La información ya no está disponible.",
        ApiStatus.Unavailable => Unavailable,
        _ => string.IsNullOrWhiteSpace(result.Message) ? fallback : result.Message!,
    };
}

/// <summary>Formatos de fecha y hora como en el diseño aprobado (por ejemplo, "14/10/2026 · 08:00 h").</summary>
public static class Formats
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string DateTime(DateOnly date, string time) => $"{Date(date)} · {time} h";

    /// <summary>"14 de octubre · 08:00 h", como en la tarjeta de la próxima cita.</summary>
    public static string LongDateTime(DateOnly date, string time) => $"{date.ToString("d 'de' MMMM", Spanish)} · {time} h";

    public static string Status(string status) => status == "Cancelled" ? "Cancelada" : "Confirmada";

    public static string FirstName(string displayName)
        => displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? displayName;

    public static string Initials(string name)
        => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w is not ("Dr." or "Dra.")).Take(2).Select(w => char.ToUpperInvariant(w[0])));
}

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Comando asíncrono que no se vuelve a ejecutar mientras la acción anterior sigue en curso: una segunda pulsación (o un reintento impaciente)
/// se ignora. Es la primera barrera contra reservas duplicadas; la segunda es la clave de idempotencia que envía la app a la API.
/// </summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private int _running;

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public bool CanExecute(object? parameter) => !IsRunning && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        finally
        {
            Volatile.Write(ref _running, 0);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Comando con parámetro (por ejemplo, abrir el elemento tocado de una lista). Tampoco se reentra mientras la acción anterior sigue en curso.</summary>
public sealed class AsyncCommand<T>(Func<T, Task> execute) : ICommand
{
    private int _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => Volatile.Read(ref _running) == 0;

    public async void Execute(object? parameter)
    {
        if (parameter is T value) await ExecuteAsync(value);
    }

    public async Task ExecuteAsync(T value)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute(value);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
