using MediCore.Mobile.Pages;

namespace MediCore.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pantallas de detalle y del recorrido de reserva: se apilan sobre la barra inferior.
        Routing.RegisterRoute("doctors", typeof(DoctorsPage));
        Routing.RegisterRoute("slots", typeof(SlotsPage));
        Routing.RegisterRoute("confirm", typeof(ConfirmPage));
        Routing.RegisterRoute("appointment", typeof(AppointmentDetailPage));
        Routing.RegisterRoute("labresult", typeof(LabResultDetailPage));
        Routing.RegisterRoute("prescription", typeof(PrescriptionDetailPage));
        Routing.RegisterRoute("notifications", typeof(NotificationsPage));
    }
}
