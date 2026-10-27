using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class AppointmentsPage : ContentPage
{
    private readonly AppointmentsViewModel _vm;

    public AppointmentsPage(AppointmentsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
