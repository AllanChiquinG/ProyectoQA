using System.Collections.Concurrent;
using MediCore.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.RabbitMq;

namespace MediCore.Api.Tests.Infrastructure;

/// <summary>
/// Como <see cref="StackFixture"/> pero con RabbitMQ real y con el publicador del outbox y el consumidor activos (intervalos cortos).
/// El puerto del broker es fijo para poder detenerlo y volver a levantarlo sin que cambie la dirección.
/// </summary>
public sealed class BrokerFixture : StackFixture
{
    private RabbitMqContainer _rabbit = null!;

    /// <summary>Fallos inyectados en el consumidor (solo para las pruebas; el código de producción no tiene ganchos de fallo).</summary>
    public HandlerFaults Faults { get; } = new();

    public string BrokerConnectionString => _rabbit.GetConnectionString();

    public override async Task InitializeAsync()
    {
        // Puerto fijo para poder detener y volver a levantar el broker con la misma dirección. Algunos puertos pueden estar reservados por
        // el sistema anfitrión (Windows excluye rangos dinámicos), así que se reintenta con otro si Docker no puede publicarlo.
        for (var attempt = 1; ; attempt++)
        {
            _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine")
                .WithUsername("test").WithPassword("test")
                .WithPortBinding(Random.Shared.Next(20000, 26000), 5672)
                .Build();
            try
            {
                await _rabbit.StartAsync();
                break;
            }
            catch (Exception ex) when (attempt < 10 && ex.ToString().Contains("ports are not available", StringComparison.OrdinalIgnoreCase))
            {
                await _rabbit.DisposeAsync();
            }
        }

        await base.InitializeAsync();
    }

    public Task StopBrokerAsync() => _rabbit.StopAsync();

    public Task StartBrokerAsync() => _rabbit.StartAsync();

    protected override void ConfigureMessaging(IWebHostBuilder builder)
    {
        builder.UseSetting("Messaging:Enabled", "true");
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbit.GetConnectionString());
        builder.UseSetting("Messaging:PublishPollInterval", "00:00:00.200");
        builder.UseSetting("Messaging:PublishMaxBackoff", "00:00:01");
        builder.UseSetting("Messaging:ConsumerMaxAttempts", "3");
        builder.UseSetting("Messaging:ConsumerRetryDelay", "00:00:00.300");
        builder.UseSetting("Messaging:ConsumerReconnectDelay", "00:00:00.500");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(Faults);
            services.RemoveAll<ILabResultReadyHandler>();
            services.AddScoped<ILabResultReadyHandler>(sp => new FaultInjectingHandler(
                ActivatorUtilities.CreateInstance<LabResultReadyHandler>(sp), sp.GetRequiredService<HandlerFaults>()));
        });
    }

    protected override async Task DisposeContainersAsync()
    {
        await _rabbit.DisposeAsync();
        await base.DisposeContainersAsync();
    }
}

/// <summary>Define, por paciente (se arma antes de registrar el resultado, sin carreras con el publicador), cuántas entregas deben fallar antes de que el consumidor funcione.</summary>
public sealed class HandlerFaults
{
    private readonly ConcurrentDictionary<Guid, int> _remainingFailures = new();
    private readonly ConcurrentDictionary<Guid, int> _calls = new();

    /// <summary>Hace fallar las próximas <paramref name="times"/> entregas del resultado (<see cref="int.MaxValue"/> = siempre).</summary>
    public void FailTimes(Guid patientId, int times) => _remainingFailures[patientId] = times;

    public void StopFailing(Guid patientId) => _remainingFailures.TryRemove(patientId, out _);

    public int Calls(Guid patientId) => _calls.GetValueOrDefault(patientId);

    internal bool ShouldFail(Guid patientId)
    {
        _calls.AddOrUpdate(patientId, 1, (_, n) => n + 1);
        while (_remainingFailures.TryGetValue(patientId, out var remaining) && remaining > 0)
        {
            var next = remaining == int.MaxValue ? remaining : remaining - 1;
            if (_remainingFailures.TryUpdate(patientId, next, remaining)) return true;
        }

        return false;
    }
}

internal sealed class FaultInjectingHandler(ILabResultReadyHandler inner, HandlerFaults faults) : ILabResultReadyHandler
{
    public Task<HandleOutcome> HandleAsync(Guid messageId, LabResultReadyEvent message, CancellationToken ct)
        => faults.ShouldFail(message.PatientId)
            ? throw new InvalidOperationException("Fallo inyectado por la prueba.")
            : inner.HandleAsync(messageId, message, ct);
}

[CollectionDefinition(Name)]
public class BrokerCollection : ICollectionFixture<BrokerFixture>
{
    public const string Name = "Broker";
}
