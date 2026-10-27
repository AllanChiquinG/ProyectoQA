using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;
using MediCore.Mobile.Core.ViewModels;
using MediCore.Mobile.Pages;
using MediCore.Mobile.Services;

namespace MediCore.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Cliente de la API real. No hay respuestas simuladas en la app.
        builder.Services.AddSingleton(_ => new HttpClient { BaseAddress = ApiSettings.BaseAddress, Timeout = TimeSpan.FromSeconds(15) });
        builder.Services.AddSingleton<ISessionStore, SecureSessionStore>();
        builder.Services.AddSingleton<SessionEvents>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IClinicClock>(sp => new ClinicClock(sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<IPatientApi, MobileApiClient>();
        builder.Services.AddSingleton<INavigator, ShellNavigator>();
        builder.Services.AddSingleton<IDialogs, MauiDialogs>();

        // ViewModels: uno por pantalla; el de la reserva es único para todo el recorrido (MOB-04 a MOB-06) y conserva lo elegido.
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<AppointmentsViewModel>();
        builder.Services.AddTransient<AppointmentDetailViewModel>();
        builder.Services.AddSingleton<BookingViewModel>();
        builder.Services.AddTransient<LabResultsViewModel>();
        builder.Services.AddTransient<LabResultDetailViewModel>();
        builder.Services.AddTransient<PrescriptionsViewModel>();
        builder.Services.AddTransient<PrescriptionDetailViewModel>();
        builder.Services.AddTransient<NotificationsViewModel>();

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<AppointmentsPage>();
        builder.Services.AddTransient<AppointmentDetailPage>();
        builder.Services.AddTransient<DoctorsPage>();
        builder.Services.AddTransient<SlotsPage>();
        builder.Services.AddTransient<ConfirmPage>();
        builder.Services.AddTransient<LabResultsPage>();
        builder.Services.AddTransient<LabResultDetailPage>();
        builder.Services.AddTransient<PrescriptionsPage>();
        builder.Services.AddTransient<PrescriptionDetailPage>();
        builder.Services.AddTransient<NotificationsPage>();

        return builder.Build();
    }
}
