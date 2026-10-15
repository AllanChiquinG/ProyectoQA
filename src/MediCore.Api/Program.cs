using MediCore.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMediCoreInfrastructure(builder.Configuration);

// Respuestas de error uniformes (RFC 9457) sin filtrar detalles internos al cliente.
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Necesario para WebApplicationFactory<Program> en las pruebas de integración.
public partial class Program;
