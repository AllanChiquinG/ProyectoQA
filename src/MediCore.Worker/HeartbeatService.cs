using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Worker;

/// <summary>
/// Latido del proceso para la comprobación de salud del contenedor (el worker no expone HTTP): escribe la hora actual en un archivo cada 10 segundos.
/// La comprobación de Docker verifica que el archivo exista y sea reciente. No contiene datos de negocio.
/// </summary>
public sealed class HeartbeatService(IConfiguration configuration, TimeProvider time, ILogger<HeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var path = configuration["Worker:HeartbeatFile"] ?? Path.Combine(Path.GetTempPath(), "medicore-worker.alive");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await File.WriteAllTextAsync(path, time.GetUtcNow().ToString("O"), stoppingToken);
            }
            catch (IOException ex)
            {
                logger.LogWarning("No se pudo escribir el latido ({ErrorType}).", ex.GetType().Name);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
