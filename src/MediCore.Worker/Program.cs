using Microsoft.Extensions.Hosting;
using MediCore.Infrastructure;
using MediCore.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Registra PostgreSQL, RabbitMQ y, porque Messaging:Enabled es true por defecto, el publicador del outbox y el consumidor.
// La configuración (cadenas de conexión) llega por variables de entorno: ConnectionStrings__Postgres y ConnectionStrings__RabbitMq.
builder.Services.AddMediCoreInfrastructure(builder.Configuration);
builder.Services.AddHostedService<HeartbeatService>();

await builder.Build().RunAsync();
