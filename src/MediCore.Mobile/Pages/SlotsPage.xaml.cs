using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class SlotsPage : ContentPage
{
    private readonly BookingViewModel _vm;
    private bool _syncing;

    public SlotsPage(BookingViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _syncing = true;
        DatePick.MinimumDate = _vm.MinDate.ToDateTime(TimeOnly.MinValue);
        DatePick.MaximumDate = _vm.MaxDate.ToDateTime(TimeOnly.MinValue);
        DatePick.Date = _vm.Date.ToDateTime(TimeOnly.MinValue);
        _syncing = false;

        // Al volver desde la confirmación (por ejemplo, tras un horario ocupado) se conservan los horarios y el aviso ya cargados.
        if (_vm.Slots.Count == 0) await _vm.LoadSlotsAsync();
    }

    private async void OnDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (_syncing || e.NewDate is not { } date) return;
        await _vm.SetDateAsync(DateOnly.FromDateTime(date));
    }
}
