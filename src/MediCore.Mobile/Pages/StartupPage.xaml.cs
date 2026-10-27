using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;

namespace MediCore.Mobile.Pages;

public partial class StartupPage : ContentPage
{
    private readonly IPatientApi _api;
    private readonly INavigator _navigator;

    public StartupPage(IPatientApi api, INavigator navigator)
    {
        InitializeComponent();
        _api = api;
        _navigator = navigator;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Con sesión guardada se va al inicio (que la valida con la API y, si venció, vuelve al inicio de sesión); sin ella, a MOB-01.
        if (await _api.HasSessionAsync()) await _navigator.GoToHomeAsync();
        else await _navigator.GoToLoginAsync();
    }
}
