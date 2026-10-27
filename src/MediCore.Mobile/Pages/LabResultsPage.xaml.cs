using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class LabResultsPage : ContentPage
{
    private readonly LabResultsViewModel _vm;

    public LabResultsPage(LabResultsViewModel vm)
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
