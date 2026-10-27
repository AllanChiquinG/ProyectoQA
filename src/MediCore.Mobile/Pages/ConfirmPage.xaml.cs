using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class ConfirmPage : ContentPage
{
    public ConfirmPage(BookingViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
