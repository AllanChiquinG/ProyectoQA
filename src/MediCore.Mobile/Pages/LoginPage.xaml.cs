using MediCore.Mobile.Core.ViewModels;

namespace MediCore.Mobile.Pages;

public partial class LoginPage : ContentPage, IQueryAttributable
{
    private readonly LoginViewModel _vm;

    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    /// <summary>Recibe el aviso al volver desde otra pantalla (por ejemplo, "Tu sesión venció...").</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _vm.Prepare(query.TryGetValue("notice", out var notice) && notice is string text && text.Length > 0 ? text : null);
    }
}
