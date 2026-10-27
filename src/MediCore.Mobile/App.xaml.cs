namespace MediCore.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent(); // los recursos (colores y estilos) deben cargarse antes de crear el Shell
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(_services.GetRequiredService<AppShell>());
}
