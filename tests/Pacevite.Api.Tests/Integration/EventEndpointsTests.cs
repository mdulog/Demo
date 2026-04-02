using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration;

public sealed class EventEndpointsTests : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres;
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public EventEndpointsTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithDatabase("pacevite_events_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor is not null)
                        services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.Migrate();
                });

                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
            });

        _client = _factory.CreateClient();
    }

    private async Task<string> GetTokenAsync(string email = "events-user@example.com")
    {
        var regResponse = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "P@ssword1!"));

        if (!regResponse.IsSuccessStatusCode)
        {
            var login = await _client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(email, "P@ssword1!"));
            var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
            return loginBody!.Token;
        }

        var body = await regResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }

    private static MultipartFormDataContent BuildCsvUpload(string csv)
    {
        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "events.csv");
        return content;
    }

    private static MultipartFormDataContent BuildJsonUpload(string json)
    {
        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(fileContent, "file", "events.json");
        return content;
    }

    [Test]
    public async Task Upload_CsvFile_Returns200WithCreatedEvents()
    {
        var token = await GetTokenAsync("csv-upload@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string csv = "MARATHON,Berlin Marathon,2024-09-29,FINISHED,14400";
        var response = await _client.PostAsync("/api/events/upload", BuildCsvUpload(csv));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var events = await response.Content.ReadFromJsonAsync<List<EventResponse>>();
        await Assert.That(events).IsNotNull();
        await Assert.That(events!.Count).IsEqualTo(1);
        await Assert.That(events[0].EventName).IsEqualTo("Berlin Marathon");
    }

    [Test]
    public async Task Upload_JsonFile_Returns200WithCreatedEvents()
    {
        var token = await GetTokenAsync("json-upload@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string json = """
            [{"event_type":"HYROX","event_name":"HYROX Berlin","event_date":"2024-11-10","completion":"FINISHED","elapsed_secs":5400}]
            """;

        var response = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var events = await response.Content.ReadFromJsonAsync<List<EventResponse>>();
        await Assert.That(events!.Count).IsEqualTo(1);
        await Assert.That(events[0].EventType).IsEqualTo("HYROX");
    }

    [Test]
    public async Task Upload_DuplicateEvent_SkipsSilently()
    {
        var token = await GetTokenAsync("dup-upload@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string csv = "MARATHON,Berlin Marathon,2024-09-29,FINISHED,14400";

        await _client.PostAsync("/api/events/upload", BuildCsvUpload(csv));
        var response = await _client.PostAsync("/api/events/upload", BuildCsvUpload(csv));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var events = await response.Content.ReadFromJsonAsync<List<EventResponse>>();
        await Assert.That(events!.Count).IsEqualTo(0); // duplicate skipped
    }

    [Test]
    public async Task GetEvents_WithEventTypeFilter_ReturnsFilteredResults()
    {
        var token = await GetTokenAsync("filter-user@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string csv = """
            MARATHON,Berlin Marathon,2024-09-29,FINISHED,14400
            HYROX,HYROX Berlin,2024-11-10,FINISHED,5400
            """;

        await _client.PostAsync("/api/events/upload", BuildCsvUpload(csv));
        var response = await _client.GetAsync("/api/events?eventType=MARATHON");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var events = await response.Content.ReadFromJsonAsync<List<EventResponse>>();
        await Assert.That(events!.Count).IsEqualTo(1);
        await Assert.That(events[0].EventType).IsEqualTo("MARATHON");
    }

    [Test]
    public async Task GetPersonalBests_ReturnsMinElapsedSecsPerEventType()
    {
        var token = await GetTokenAsync("pb-user@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Two marathons — PB should be the faster one (12000s)
        const string csv = """
            MARATHON,Marathon A,2023-04-23,FINISHED,14400
            MARATHON,Marathon B,2024-04-21,FINISHED,12000
            HYROX,HYROX Berlin,2024-11-10,FINISHED,5400
            """;

        await _client.PostAsync("/api/events/upload", BuildCsvUpload(csv));
        var response = await _client.GetAsync("/api/events/personal-bests");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var pbs = await response.Content.ReadFromJsonAsync<List<PersonalBestResponse>>();
        await Assert.That(pbs!.Count).IsEqualTo(2); // MARATHON + HYROX

        var marathonPb = pbs.Single(p => p.EventType == "MARATHON");
        await Assert.That(marathonPb.ElapsedSecs).IsEqualTo(12000);
    }

    [Test]
    public async Task DeleteEvent_OwnedByUser_Returns204()
    {
        var token = await GetTokenAsync("delete-user@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string csv = "MARATHON,Delete Me,2024-09-29,FINISHED,14400";
        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildCsvUpload(csv));
        var events = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        var eventId = events![0].Id;

        var deleteResponse = await _client.DeleteAsync($"/api/events/{eventId}");

        await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var afterDelete = await _client.GetAsync("/api/events");
        var remaining = await afterDelete.Content.ReadFromJsonAsync<List<EventResponse>>();
        await Assert.That(remaining!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DeleteEvent_BelongingToAnotherUser_Returns204ButDoesNotDelete()
    {
        // User A uploads an event
        var tokenA = await GetTokenAsync("owner-user@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        const string csv = "MARATHON,Protected Event,2024-09-29,FINISHED,14400";
        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildCsvUpload(csv));
        var events = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        var eventId = events![0].Id;

        // User B tries to delete User A's event
        var tokenB = await GetTokenAsync("attacker-user@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var deleteResponse = await _client.DeleteAsync($"/api/events/{eventId}");

        // 204 either way — no ownership info leaked (OWASP A01)
        await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        // Verify event still exists for User A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var afterDelete = await _client.GetAsync("/api/events");
        var remaining = await afterDelete.Content.ReadFromJsonAsync<List<EventResponse>>();
        await Assert.That(remaining!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetEvents_WithoutToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/events");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
