using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class DoctorsPage : ContentPage
{
    private readonly BookingViewModel _vm;

    public DoctorsPage(BookingViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.Reset(); // cada solicitud nueva empieza desde el paso 1
        await _vm.LoadAsync();
    }
}
