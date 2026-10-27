using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class AppointmentDetailPage : ContentPage, IQueryAttributable
{
    private readonly AppointmentDetailViewModel _vm;

    public AppointmentDetailPage(AppointmentDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && Guid.TryParse(id?.ToString(), out var guid)) _vm.AppointmentId = guid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
