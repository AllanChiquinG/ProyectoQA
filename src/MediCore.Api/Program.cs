using System.Threading.RateLimiting;
using MediCore.Api.Security;
using MediCore.Infrastructure;
using MediCore.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMediCoreInfrastructure(builder.Configuration);

// Respuestas de error uniformes (RFC 9457) sin filtrar detalles internos al cliente.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DependencyUnavailableExceptionHandler>();

// --- Autenticación: JWT de vida corta + sesión en Redis (permite revocar al cerrar sesión o cambiar el rol).
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = JwtTokenService.SigningKey(jwt),
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimNames.Name,
            RoleClaimType = ClaimNames.Role,
        };
        bearer.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sessionId = context.Principal?.SessionId();
                if (sessionId is null)
                {
                    context.Fail("El token no tiene sesión.");
                    return;
                }

                try
                {
                    var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionStore>();
                    if (!await sessions.ExistsAsync(sessionId, context.HttpContext.RequestAborted))
                        context.Fail("La sesión ya no es válida.");
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    // Sin poder verificar la sesión no se autentica: falla cerrado.
                    context.Fail("No se pudo verificar la sesión.");
                }
            },
        };
    });

// Autorización por recurso (¿puede ESTE usuario ver ESTE paciente?).
builder.Services.AddScoped<IAuthorizationHandler, PatientAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AppointmentAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, PrescriptionAuthorizationHandler>();

// Seguro por defecto: todo requiere usuario autenticado salvo lo marcado con [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// --- Límite de intentos en login y refresco (por IP).
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    await DevelopmentBootstrap.RunAsync(app);
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Necesario para WebApplicationFactory<Program> en las pruebas de integración.
public partial class Program;
