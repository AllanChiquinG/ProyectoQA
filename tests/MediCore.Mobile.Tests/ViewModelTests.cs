using MediCore.Contracts;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Notifications;
using MediCore.Contracts.Scheduling;
using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;
using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Tests;

/// <summary>Lógica de las pantallas (validación, estados, reservas sin duplicados, sesión vencida) con una API simulada solo para aislar el ViewModel.</summary>
public class ViewModelTests
{
    private static readonly Guid DoctorId = Guid.NewGuid();

    private static AppointmentDto Appointment(string time = "08:00", int daysFromNow = 1, string status = "Confirmed", Guid? id = null)
        => new(id ?? Guid.NewGuid(), Guid.NewGuid(), "Ana Sofía García", DoctorId, "Dra. Lucía Méndez", "Medicina general", new DateOnly(2026, 10, 14).AddDays(daysFromNow), time, 30, status, null);

    // ---- MOB-01 -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Login_valida_el_formulario_y_no_llama_a_la_API_si_esta_incompleto()
    {
        var api = new FakeApi { Login = (_, _) => throw new InvalidOperationException("No debe llamarse") };
        var vm = new LoginViewModel(api, new FakeNavigator());

        await vm.LoginCommand.ExecuteAsync();

        Assert.Equal("Ingresa tu correo electrónico.", vm.EmailError);
        Assert.Equal("Ingresa tu contraseña.", vm.PasswordError);
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ana@")]
    [InlineData("ana@example")]
    public void Login_rechaza_correos_con_formato_invalido(string email)
    {
        var vm = new LoginViewModel(new FakeApi(), new FakeNavigator()) { Email = email, Password = "x" };

        Assert.False(vm.Validate());
        Assert.Equal("Ingresa un correo electrónico válido.", vm.EmailError);
    }

    [Fact]
    public async Task Login_correcto_va_al_inicio_y_limpia_la_contrasena()
    {
        var nav = new FakeNavigator();
        var vm = new LoginViewModel(new FakeApi(), nav) { Email = " ana.garcia@example.com ", Password = "secreta" };

        await vm.LoginCommand.ExecuteAsync();

        Assert.Equal(["home"], nav.Log);
        Assert.Equal("", vm.Password);
    }

    [Fact]
    public async Task Login_incorrecto_muestra_un_mensaje_unico_y_se_queda_en_la_pantalla()
    {
        var nav = new FakeNavigator();
        var api = new FakeApi { Login = (_, _) => new(ApiStatus.InvalidCredentials) };
        var vm = new LoginViewModel(api, nav) { Email = "ana@example.com", Password = "mala" };

        await vm.LoginCommand.ExecuteAsync();

        Assert.Equal("El correo o la contraseña no son correctos.", vm.FormError);
        Assert.Empty(nav.Log);
    }

    [Fact]
    public async Task Login_sin_conexion_muestra_el_error_de_conexion()
    {
        var api = new FakeApi { Login = (_, _) => new(ApiStatus.Offline) };
        var vm = new LoginViewModel(api, new FakeNavigator()) { Email = "ana@example.com", Password = "x" };

        await vm.LoginCommand.ExecuteAsync();

        Assert.Equal(Messages.Offline, vm.FormError);
    }

    [Fact]
    public void Login_puede_mostrar_el_aviso_de_sesion_vencida()
    {
        var vm = new LoginViewModel(new FakeApi(), new FakeNavigator()) { Password = "x" };

        vm.Prepare(Messages.SessionExpired);

        Assert.Equal(Messages.SessionExpired, vm.FormError);
        Assert.Equal("", vm.Password);
    }

    // ---- MOB-02 -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Inicio_muestra_la_proxima_cita_confirmada_y_futura_e_ignora_canceladas_y_pasadas()
    {
        var api = new FakeApi
        {
            MyAppointments = () => new(ApiStatus.Ok, [Appointment("08:00", -3), Appointment("09:00", 5, "Cancelled"), Appointment("10:00", 9), Appointment("08:30", 2)]),
        };
        var vm = new HomeViewModel(api, new FakeNavigator(), new FixedClock());

        await vm.LoadAsync();

        Assert.Equal(ViewState.Content, vm.State);
        Assert.Equal("Hola, Ana", vm.Greeting);
        Assert.Equal("16/10/2026 · 08:30 h", vm.NextAppointment!.When);
        Assert.Equal("Dra. Lucía Méndez", vm.NextTitle);
        Assert.Equal("Ver mi cita →", vm.NextButtonText);
        Assert.Equal(2, vm.UnreadCount);
    }

    [Fact]
    public async Task Inicio_sin_citas_invita_a_solicitar_una()
    {
        var vm = new HomeViewModel(new FakeApi(), new FakeNavigator(), new FixedClock());

        await vm.LoadAsync();

        Assert.False(vm.HasNext);
        Assert.Equal("Estamos para cuidarte", vm.NextTitle);
        Assert.Equal("Solicitar cita →", vm.NextButtonText);
    }

    [Fact]
    public async Task Inicio_con_error_de_conexion_permite_reintentar()
    {
        var calls = 0;
        var api = new FakeApi { MyAppointments = () => ++calls == 1 ? new(ApiStatus.Offline) : new(ApiStatus.Ok, []) };
        var vm = new HomeViewModel(api, new FakeNavigator(), new FixedClock());

        await vm.LoadAsync();
        Assert.Equal(ViewState.Error, vm.State);
        Assert.Equal(Messages.Offline, vm.ErrorMessage);

        await vm.RetryCommand.ExecuteAsync();
        Assert.Equal(ViewState.Content, vm.State);
    }

    [Fact]
    public async Task Sesion_vencida_en_cualquier_pantalla_lleva_a_MOB_01_con_aviso()
    {
        var nav = new FakeNavigator();
        var api = new FakeApi
        {
            MyAppointments = () => new(ApiStatus.SessionExpired),
            Labs = () => new(ApiStatus.SessionExpired),
            Prescriptions = () => new(ApiStatus.SessionExpired),
            Notifications = () => new(ApiStatus.SessionExpired),
        };

        await new HomeViewModel(api, nav, new FixedClock()).LoadAsync();
        await new AppointmentsViewModel(api, nav).LoadAsync();
        await new LabResultsViewModel(api, nav).LoadAsync();
        await new PrescriptionsViewModel(api, nav).LoadAsync();
        await new NotificationsViewModel(api, nav).LoadAsync();

        Assert.Equal(["login", "login", "login", "login", "login"], nav.Log);
        Assert.Equal(Messages.SessionExpired, nav.LoginNotice);
    }

    [Fact]
    public async Task Cerrar_sesion_llama_a_la_API_y_vuelve_al_inicio_de_sesion()
    {
        var nav = new FakeNavigator();
        var api = new FakeApi();
        var vm = new HomeViewModel(api, nav, new FixedClock());

        await vm.LogoutCommand.ExecuteAsync();

        Assert.Equal(1, api.LogoutCalls);
        Assert.Equal(["login"], nav.Log);
    }

    // ---- MOB-03 / MOB-07 --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Mis_citas_distingue_lista_vacia_y_con_datos()
    {
        var api = new FakeApi();
        var vm = new AppointmentsViewModel(api, new FakeNavigator());

        await vm.LoadAsync();
        Assert.Equal(ViewState.Empty, vm.State);
        Assert.Equal("Aún no tienes citas. Solicita una con tu médico.", vm.EmptyMessage);

        api.MyAppointments = () => new(ApiStatus.Ok, [Appointment(), Appointment(status: "Cancelled")]);
        await vm.LoadAsync();
        Assert.Equal(ViewState.Content, vm.State);
        Assert.Equal(["Confirmada", "Cancelada"], vm.Items.Select(i => i.StatusText));
    }

    [Fact]
    public async Task Cancelar_pide_confirmacion_y_si_el_usuario_dice_no_no_llama_a_la_API()
    {
        var id = Guid.NewGuid();
        var cancels = 0;
        var api = new FakeApi { Appointment = _ => new(ApiStatus.Ok, Appointment(id: id)), Cancel = _ => { cancels++; return new(ApiStatus.Ok); } };
        var dialogs = new FakeDialogs { Answer = false };
        var vm = new AppointmentDetailViewModel(api, new FakeNavigator(), dialogs) { AppointmentId = id };
        await vm.LoadAsync();

        await vm.CancelCommand.ExecuteAsync();

        Assert.Equal(1, dialogs.Confirms);
        Assert.Equal(0, cancels);
        Assert.Equal("Confirmada", vm.StatusText);
    }

    [Fact]
    public async Task Cancelar_confirmado_cancela_y_recarga_el_estado_real()
    {
        var id = Guid.NewGuid();
        var status = "Confirmed";
        var api = new FakeApi { Appointment = _ => new(ApiStatus.Ok, Appointment(id: id, status: status)), Cancel = _ => { status = "Cancelled"; return new(ApiStatus.Ok); } };
        var dialogs = new FakeDialogs();
        var vm = new AppointmentDetailViewModel(api, new FakeNavigator(), dialogs) { AppointmentId = id };
        await vm.LoadAsync();
        Assert.True(vm.CanCancel);

        await vm.CancelCommand.ExecuteAsync();

        Assert.Equal("Cancelada", vm.StatusText);
        Assert.False(vm.CanCancel);
        Assert.Contains("Cita cancelada", dialogs.Toasts);
    }

    [Fact]
    public async Task Cancelar_con_error_muestra_el_mensaje_y_conserva_la_cita()
    {
        var id = Guid.NewGuid();
        var api = new FakeApi { Appointment = _ => new(ApiStatus.Ok, Appointment(id: id)), Cancel = _ => new(ApiStatus.Offline) };
        var vm = new AppointmentDetailViewModel(api, new FakeNavigator(), new FakeDialogs()) { AppointmentId = id };
        await vm.LoadAsync();

        await vm.CancelCommand.ExecuteAsync();

        Assert.Equal(Messages.Offline, vm.ActionError);
        Assert.True(vm.CanCancel);
    }

    // ---- MOB-04 / 05 / 06: reserva ----------------------------------------------------------------------------------

    private static (BookingViewModel Vm, FakeApi Api, FakeNavigator Nav, FakeDialogs Dialogs) NewBooking()
    {
        var api = new FakeApi { Doctors = () => [new DoctorDto(DoctorId, "Dra. Lucía Méndez", "Medicina general", null, null)] };
        var nav = new FakeNavigator();
        var dialogs = new FakeDialogs();
        return (new BookingViewModel(api, nav, dialogs, new FixedClock()), api, nav, dialogs);
    }

    private static async Task ChooseAsync(BookingViewModel vm, string time = "08:00")
    {
        await vm.LoadAsync();
        await vm.SelectDoctorCommand.ExecuteAsync(vm.Doctors[0]);
        await vm.LoadSlotsAsync();
        await vm.SelectTimeCommand.ExecuteAsync(vm.Slots.First(s => s.Time == time));
    }

    [Fact]
    public async Task Reserva_lista_de_medicos_con_estado_vacio()
    {
        var (vm, api, _, _) = NewBooking();
        api.Doctors = () => [];

        await vm.LoadAsync();

        Assert.Equal(ViewState.Empty, vm.State);
    }

    [Fact]
    public async Task Reserva_no_permite_continuar_sin_horario_y_pide_elegirlo()
    {
        var (vm, _, nav, _) = NewBooking();
        await vm.LoadAsync();
        await vm.SelectDoctorCommand.ExecuteAsync(vm.Doctors[0]);
        await vm.LoadSlotsAsync();

        await vm.ContinueCommand.ExecuteAsync();

        Assert.Equal("Selecciona un horario para continuar.", vm.TimeError);
        Assert.DoesNotContain("confirm", nav.Log);
    }

    [Fact]
    public async Task Reserva_avisa_si_el_medico_no_atiende_o_no_quedan_horarios()
    {
        var (vm, api, _, _) = NewBooking();
        await vm.LoadAsync();
        await vm.SelectDoctorCommand.ExecuteAsync(vm.Doctors[0]);

        api.Availability = (d, date) => new(ApiStatus.Ok, new AvailabilityDto(d, date, false, []));
        await vm.LoadSlotsAsync();
        Assert.Equal("El médico no atiende ese día. Elige otra fecha.", vm.SlotsMessage);

        api.Availability = (d, date) => new(ApiStatus.Ok, new AvailabilityDto(d, date, true, [new SlotDto("08:00", false)]));
        await vm.LoadSlotsAsync();
        Assert.Equal("No quedan horarios disponibles ese día. Elige otra fecha.", vm.SlotsMessage);
    }

    [Fact]
    public async Task Reserva_exitosa_confirma_va_al_detalle_y_reinicia_el_recorrido()
    {
        var (vm, api, nav, dialogs) = NewBooking();
        var created = Appointment("08:00", 3);
        api.Create = (_, _) => new(ApiStatus.Ok, created);
        await ChooseAsync(vm);

        await vm.ConfirmCommand.ExecuteAsync();

        Assert.Equal(1, api.CreateCalls);
        Assert.Contains($"appointment:{created.Id}", nav.Log);
        Assert.Contains("Cita confirmada", dialogs.Toasts);
        Assert.Null(vm.Doctor);
        Assert.Null(vm.SelectedTime);
    }

    [Fact]
    public async Task Pulsar_dos_veces_Confirmar_envia_una_sola_reserva()
    {
        var (vm, api, _, _) = NewBooking();
        api.CreateGate = new TaskCompletionSource();
        api.Create = (_, _) => new(ApiStatus.Ok, Appointment());
        await ChooseAsync(vm);

        var first = vm.ConfirmCommand.ExecuteAsync();
        var second = vm.ConfirmCommand.ExecuteAsync(); // segunda pulsación mientras la primera sigue en curso
        api.CreateGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, api.CreateCalls);
    }

    [Fact]
    public async Task Reintentar_tras_perder_la_conexion_reutiliza_la_misma_clave_de_idempotencia()
    {
        var (vm, api, _, _) = NewBooking();
        var attempt = 0;
        api.Create = (_, _) => ++attempt == 1 ? new(ApiStatus.Offline) : new(ApiStatus.Ok, Appointment());
        await ChooseAsync(vm);

        await vm.ConfirmCommand.ExecuteAsync();
        Assert.Contains("no se duplicará", vm.ConfirmError);
        await vm.ConfirmCommand.ExecuteAsync();

        Assert.Equal(2, api.Keys.Count);
        Assert.Equal(api.Keys[0], api.Keys[1]);
        Assert.False(string.IsNullOrWhiteSpace(api.Keys[0]));
    }

    [Fact]
    public async Task Cambiar_de_horario_genera_una_clave_nueva()
    {
        var (vm, api, _, _) = NewBooking();
        api.Create = (_, _) => new(ApiStatus.Offline);
        await ChooseAsync(vm, "08:00");
        await vm.ConfirmCommand.ExecuteAsync();

        await vm.SelectTimeCommand.ExecuteAsync(vm.Slots.First(s => s.Time == "08:30"));
        await vm.ConfirmCommand.ExecuteAsync();

        Assert.NotEqual(api.Keys[0], api.Keys[1]);
    }

    [Fact]
    public async Task Horario_ocupado_antes_de_confirmar_muestra_aviso_conserva_medico_y_fecha_y_permite_elegir_otro()
    {
        var (vm, api, nav, _) = NewBooking();
        api.Create = (_, _) => new(ApiStatus.Conflict, null, "Este horario ya está ocupado.");
        await ChooseAsync(vm, "08:00");
        var doctor = vm.Doctor;
        var date = vm.Date;
        // El horario de las 08:00 ya no está libre cuando se vuelve a consultar.
        api.Availability = (d, day) => new(ApiStatus.Ok, new AvailabilityDto(d, day, true, [new SlotDto("08:00", false), new SlotDto("08:30", true)]));

        await vm.ConfirmCommand.ExecuteAsync();

        Assert.Equal(Messages.SlotTaken, vm.ConfirmError);
        Assert.Equal(Messages.SlotTaken, vm.SlotsMessage);
        Assert.Same(doctor, vm.Doctor);
        Assert.Equal(date, vm.Date);
        Assert.Null(vm.SelectedTime);
        Assert.Equal("back", nav.Log[^1]);
        Assert.False(vm.Slots.First(s => s.Time == "08:00").Available);

        // Elige otro horario y confirma: se envía una solicitud nueva con otra clave.
        api.Create = (_, _) => new(ApiStatus.Ok, Appointment("08:30"));
        await vm.SelectTimeCommand.ExecuteAsync(vm.Slots.First(s => s.Time == "08:30"));
        await vm.ConfirmCommand.ExecuteAsync();
        Assert.Equal(2, api.CreateCalls);
        Assert.NotEqual(api.Keys[0], api.Keys[1]);
    }

    [Fact]
    public async Task Los_horarios_ocupados_no_se_pueden_seleccionar()
    {
        var (vm, api, _, _) = NewBooking();
        api.Availability = (d, day) => new(ApiStatus.Ok, new AvailabilityDto(d, day, true, [new SlotDto("08:00", false), new SlotDto("08:30", true)]));
        await vm.LoadAsync();
        await vm.SelectDoctorCommand.ExecuteAsync(vm.Doctors[0]);
        await vm.LoadSlotsAsync();

        await vm.SelectTimeCommand.ExecuteAsync(vm.Slots[0]);

        Assert.Null(vm.SelectedTime);
    }

    [Fact]
    public async Task La_fecha_se_limita_a_hoy_y_al_maximo_reservable()
    {
        var (vm, _, _, _) = NewBooking();
        await vm.LoadAsync();
        await vm.SelectDoctorCommand.ExecuteAsync(vm.Doctors[0]);

        await vm.SetDateAsync(new DateOnly(2020, 1, 1));
        Assert.Equal(new DateOnly(2026, 10, 14), vm.Date);
        await vm.SetDateAsync(new DateOnly(2030, 1, 1));
        Assert.Equal(new DateOnly(2027, 1, 12), vm.Date);
    }

    // ---- MOB-08 a MOB-12 --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Listas_vacias_de_resultados_y_recetas_muestran_su_mensaje()
    {
        var nav = new FakeNavigator();
        var labs = new LabResultsViewModel(new FakeApi(), nav);
        var rx = new PrescriptionsViewModel(new FakeApi(), nav);

        await labs.LoadAsync();
        await rx.LoadAsync();

        Assert.Equal(ViewState.Empty, labs.State);
        Assert.Equal("Aún no tienes resultados de laboratorio.", labs.EmptyMessage);
        Assert.Equal(ViewState.Empty, rx.State);
        Assert.Equal("Aún no tienes recetas.", rx.EmptyMessage);
    }

    [Fact]
    public async Task Abrir_una_notificacion_de_resultado_la_marca_como_leida_y_abre_el_resultado()
    {
        var nav = new FakeNavigator();
        var target = Guid.NewGuid();
        var dto = new NotificationDto(Guid.NewGuid(), "LabResultReady", "Tu resultado está disponible", "Ya puedes consultar tu resultado.", DateTime.UtcNow, false, "LabResult", target);
        var read = new List<Guid>();
        var api = new FakeApi
        {
            Notifications = () => new(ApiStatus.Ok, new PagedResult<NotificationDto>([dto], 1, 1, 50)),
            MarkRead = id => { read.Add(id); return new(ApiStatus.Ok); },
        };
        var vm = new NotificationsViewModel(api, nav);
        await vm.LoadAsync();
        var item = vm.Items[0];
        Assert.StartsWith("● ", item.TitleText);

        await vm.OpenCommand.ExecuteAsync(item);

        Assert.Equal([dto.Id], read);
        Assert.True(item.IsRead);
        Assert.Equal("Tu resultado está disponible", item.TitleText);
        Assert.Equal([$"lab:{target}"], nav.Log);
    }

    [Fact]
    public async Task Si_marcar_como_leida_falla_la_notificacion_sigue_sin_leer_pero_el_contenido_se_abre()
    {
        var nav = new FakeNavigator();
        var dto = new NotificationDto(Guid.NewGuid(), "LabResultReady", "t", "b", DateTime.UtcNow, false, "LabResult", Guid.NewGuid());
        var api = new FakeApi { Notifications = () => new(ApiStatus.Ok, new PagedResult<NotificationDto>([dto], 1, 1, 50)), MarkRead = _ => new(ApiStatus.Offline) };
        var vm = new NotificationsViewModel(api, nav);
        await vm.LoadAsync();

        await vm.OpenCommand.ExecuteAsync(vm.Items[0]);

        Assert.False(vm.Items[0].IsRead);
        Assert.Single(nav.Log);
    }

    [Fact]
    public async Task Una_notificacion_de_tipo_desconocido_no_navega()
    {
        var nav = new FakeNavigator();
        var dto = new NotificationDto(Guid.NewGuid(), "Otro", "t", "b", DateTime.UtcNow, true, "Otro", Guid.NewGuid());
        var api = new FakeApi { Notifications = () => new(ApiStatus.Ok, new PagedResult<NotificationDto>([dto], 1, 1, 50)) };
        var vm = new NotificationsViewModel(api, nav);
        await vm.LoadAsync();

        await vm.OpenCommand.ExecuteAsync(vm.Items[0]);

        Assert.Empty(nav.Log);
        Assert.False(vm.Items[0].HasTarget);
    }
}
