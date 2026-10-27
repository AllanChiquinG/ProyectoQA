using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class PrescriptionDetailPage : ContentPage, IQueryAttributable
{
    private readonly PrescriptionDetailViewModel _vm;

    public PrescriptionDetailPage(PrescriptionDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && Guid.TryParse(id?.ToString(), out var guid)) _vm.PrescriptionId = guid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
