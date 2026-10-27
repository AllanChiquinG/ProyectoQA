using MediCore.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    // Seguro por defecto: todo requiere sesión salvo el inicio de sesión y el error.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Estado");
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
{
    // Mensajes de validación del enlace de datos en español.
    var messages = options.ModelBindingMessageProvider;
    messages.SetValueMustNotBeNullAccessor(_ => "Este campo es obligatorio.");
    messages.SetMissingBindRequiredValueAccessor(_ => "Este campo es obligatorio.");
    messages.SetMissingKeyOrValueAccessor(() => "Este campo es obligatorio.");
    messages.SetAttemptedValueIsInvalidAccessor((_, _) => "El valor ingresado no es válido.");
    messages.SetUnknownValueIsInvalidAccessor(_ => "El valor ingresado no es válido.");
    messages.SetValueIsInvalidAccessor(_ => "El valor ingresado no es válido.");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "El valor ingresado no es válido.");
    messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor ingresado no es válido.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "El valor debe ser un número.");
    messages.SetValueMustBeANumberAccessor(_ => "El valor debe ser un número.");
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ClinicCalendar>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IMediCoreApi, MediCoreApi>(client =>
{
    var baseUrl = builder.Configuration["Api:BaseUrl"]
        ?? throw new InvalidOperationException("Falta la configuración 'Api:BaseUrl'.");
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "medicore.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // En desarrollo se usa http://localhost; fuera de él la cookie solo viaja por HTTPS.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = SessionTokens.CookieLifetime;
        options.SlidingExpiration = false;
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccesoDenegado";
        options.Events.OnValidatePrincipal = SessionTokens.ValidateAsync;
    });

builder.Services.AddAuthorizationBuilder();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Cabeceras de seguridad. La tipografía del diseño se carga desde Google Fonts.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; " +
        "script-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

app.UseHttpsRedirection();
// Respuestas vacías de error (404, 400, etc.) se muestran con una página en español y conservan el código HTTP.
app.UseStatusCodePagesWithReExecute("/Estado", "?codigo={0}");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
