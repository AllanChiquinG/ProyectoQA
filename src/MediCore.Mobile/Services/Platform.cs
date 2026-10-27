using System.Reflection;
using System.Text.Json;
using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;

namespace MediCore.Mobile.Services;

/// <summary>Dirección de la API: se fija al compilar (<c>-p:ApiBaseUrl=...</c>) y queda como metadato del ensamblado. No contiene secretos.</summary>
public static class ApiSettings
{
    public static Uri BaseAddress { get; } = Resolve();

    private static Uri Resolve()
    {
        var value = typeof(ApiSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ApiBaseUrl")?.Value;
        return new Uri(string.IsNullOrWhiteSpace(value) ? "http://10.0.2.2:5011/" : value.TrimEnd('/') + "/");
    }
}

/// <summary>
/// Sesión en el almacenamiento seguro de Android (Keystore, mediante <see cref="SecureStorage"/>). Solo guarda los tokens y el nombre; jamás la contraseña.
/// Si el almacén no se puede leer (por ejemplo, tras restaurar el dispositivo), se trata como "sin sesión".
/// </summary>
public sealed class SecureSessionStore : ISessionStore
{
    private const string Key = "medicore.sesion";

    public async Task<StoredSession?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(Key);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<StoredSession>(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or Java.Lang.Exception)
        {
            SecureStorage.Default.Remove(Key);
            return null;
        }
    }

    public Task SaveAsync(StoredSession session) => SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(session));

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}

public sealed class ShellNavigator : INavigator
{
    private static Task Go(string route, IDictionary<string, object>? parameters = null)
        => MainThread.InvokeOnMainThreadAsync(() => parameters is null ? Shell.Current.GoToAsync(route) : Shell.Current.GoToAsync(route, parameters));

    public Task GoToLoginAsync(string? notice = null) => Go("//login", new Dictionary<string, object> { ["notice"] = notice ?? "" });
    public Task GoToHomeAsync() => Go("//main/home");
    public Task GoToAppointmentsAsync() => Go("//main/appointments");
    public Task GoToAppointmentAsync(Guid id) => Go("appointment", new Dictionary<string, object> { ["id"] = id.ToString() });
    public Task GoToDoctorsAsync() => Go("doctors");
    public Task GoToSlotsAsync() => Go("slots");
    public Task GoToConfirmAsync() => Go("confirm");
    public Task GoToLabResultsAsync() => Go("//main/results");
    public Task GoToLabResultAsync(Guid id) => Go("labresult", new Dictionary<string, object> { ["id"] = id.ToString() });
    public Task GoToPrescriptionsAsync() => Go("//main/prescriptions");
    public Task GoToPrescriptionAsync(Guid id) => Go("prescription", new Dictionary<string, object> { ["id"] = id.ToString() });
    public Task GoToNotificationsAsync() => Go("notifications");
    public Task GoBackAsync() => Go("..");
}

public sealed class MauiDialogs : IDialogs
{
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
        => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlertAsync(title, message, accept, cancel));

    public Task ToastAsync(string message)
        => MainThread.InvokeOnMainThreadAsync(() => CommunityToolkitFreeToast.ShowAsync(message));
}

/// <summary>Aviso breve sin dependencias externas: usa el Toast nativo de Android.</summary>
internal static class CommunityToolkitFreeToast
{
    public static Task ShowAsync(string message)
    {
        Android.Widget.Toast.MakeText(Android.App.Application.Context, message, Android.Widget.ToastLength.Short)?.Show();
        return Task.CompletedTask;
    }
}
