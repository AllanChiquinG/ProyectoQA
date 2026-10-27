namespace MediCore.Mobile.Views;

public partial class HeaderBar : ContentView
{
    public static readonly BindableProperty ShowBackProperty = BindableProperty.Create(nameof(ShowBack), typeof(bool), typeof(HeaderBar), false, propertyChanged: (b, _, _) => ((HeaderBar)b).Raise());
    public static readonly BindableProperty ShowBellProperty = BindableProperty.Create(nameof(ShowBell), typeof(bool), typeof(HeaderBar), true);

    public HeaderBar() => InitializeComponent();

    public bool ShowBack { get => (bool)GetValue(ShowBackProperty); set => SetValue(ShowBackProperty, value); }
    public bool ShowBell { get => (bool)GetValue(ShowBellProperty); set => SetValue(ShowBellProperty, value); }

    /// <summary>La marca se muestra cuando no hay botón "Volver", como en el diseño.</summary>
    public bool ShowBrand => !ShowBack;

    private void Raise() => OnPropertyChanged(nameof(ShowBrand));

    private async void OnBack(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private async void OnBell(object? sender, EventArgs e) => await Shell.Current.GoToAsync("notifications");
}
