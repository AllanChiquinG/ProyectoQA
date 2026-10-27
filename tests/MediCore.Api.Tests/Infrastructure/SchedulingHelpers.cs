using System.Net.Http.Json;
using MediCore.Contracts.Scheduling;

namespace MediCore.Api.Tests.Infrastructure;

public static class SchedulingHelpers
{
    public static CreateAppointmentRequest Request(Guid? patientId, Guid doctorId, DateOnly date, string time = "09:00", string? reason = null)
        => new(patientId, doctorId, date, time, reason);

    public static async Task<HttpResponseMessage> BookAsync(this HttpClient client, CreateAppointmentRequest request, string? idempotencyKey = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/appointments") { Content = JsonContent.Create(request) };
        if (idempotencyKey is not null) message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    public static async Task<AppointmentDto> ReadAppointmentAsync(this HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<AppointmentDto>())!;

    public static string NewKey() => $"key-{Guid.NewGuid():N}";
}
