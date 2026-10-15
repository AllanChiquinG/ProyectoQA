using System.Net;
using MediCore.Domain.Messaging;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Api.Tests;

[Trait("Category", "Integration")]
public class PostgresIntegrationTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Ready_devuelve_200_con_postgres_disponible()
    {
        var response = await factory.CreateClient().GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"postgres\":\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Outbox_guarda_y_recupera_eventos_pendientes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var message = new OutboxMessage { Type = "AppointmentCreated", Payload = "{\"appointmentId\":1}" };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var pending = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.ProcessedAtUtc == null && x.Id == message.Id)
            .SingleAsync();

        Assert.Equal("AppointmentCreated", pending.Type);
        Assert.Equal(0, pending.Attempts);
    }
}
