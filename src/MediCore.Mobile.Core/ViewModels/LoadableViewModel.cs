using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;

namespace MediCore.Mobile.Core.ViewModels;

public enum ViewState
{
    /// <summary>Cargando por primera vez o reintentando.</summary>
    Loading,
    Content,

    /// <summary>La consulta funcionó pero no hay elementos.</summary>
    Empty,

    /// <summary>Error de conexión o del servicio; se puede reintentar.</summary>
    Error,
}

/// <summary>Base de las pantallas que cargan datos: estados de carga, vacío y error, y salida a MOB-01 cuando la sesión venció.</summary>
public abstract class LoadableViewModel(INavigator navigator) : ViewModelBase
{
    private ViewState _state = ViewState.Loading;
    private string? _errorMessage;

    public ViewState State
    {
        get => _state;
        protected set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(IsLoading));
                Raise(nameof(IsContent));
                Raise(nameof(IsEmpty));
                Raise(nameof(IsError));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => Set(ref _errorMessage, value);
    }

    public bool IsLoading => State == ViewState.Loading;
    public bool IsContent => State == ViewState.Content;
    public bool IsEmpty => State == ViewState.Empty;
    public bool IsError => State == ViewState.Error;

    /// <summary>Texto de la lista vacía (según el diseño).</summary>
    public virtual string EmptyMessage => "No hay información para mostrar.";

    /// <summary>Aplica un fallo de la API: sesión vencida lleva a MOB-01; lo demás se muestra con opción de reintentar.</summary>
    protected async Task FailAsync(ApiResult result, string fallback)
    {
        if (result.Status == ApiStatus.SessionExpired)
        {
            await navigator.GoToLoginAsync(Messages.SessionExpired);
            return;
        }

        ErrorMessage = Messages.For(result, fallback);
        State = ViewState.Error;
    }

    public abstract Task LoadAsync();
}
