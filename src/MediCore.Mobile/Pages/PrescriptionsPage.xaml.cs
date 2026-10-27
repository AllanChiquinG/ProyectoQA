using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class PrescriptionsPage : ContentPage
{
    private readonly PrescriptionsViewModel _vm;

    public PrescriptionsPage(PrescriptionsViewModel vm)
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
