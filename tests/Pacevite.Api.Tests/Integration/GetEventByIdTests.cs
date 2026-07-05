using System.Linq;
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

[Category("Integration")]
public sealed class GetEventByIdTests : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres;
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public GetEventByIdTests()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_getbyid_test")
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

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "P@ssword1!"));

        if (reg.IsSuccessStatusCode)
        {
            var body = await reg.Content.ReadFromJsonAsync<AuthResponse>();
            return body!.Token;
        }

        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "P@ssword1!"));
        var loginBody = await login.Content.ReadFromJsonAsync<AuthResponse>();
        return loginBody!.Token;
    }

    private static MultipartFormDataContent BuildJsonUpload(string json)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(json));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        content.Add(fileContent, "file", "events.json");
        return content;
    }

    [Test]
    public async Task GetEventById_Returns200WithSplits_WhenEventExists()
    {
        // Arrange
        var token = await GetTokenAsync("getbyid-found@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string json = """
            [{
              "event_type": "MARATHON",
              "event_name": "Berlin Marathon",
              "event_date": "2024-09-29",
              "completion": "FINISHED",
              "elapsed_secs": 14400,
              "splits": [
                { "split_type": "RUN", "split_label": "21km", "split_secs": 3180, "cumulative_secs": 6120 },
                { "split_type": "RUN", "split_label": "10km", "split_secs": 2940, "cumulative_secs": 2940 }
              ]
            }]
            """;

        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        var eventId = uploaded![0].Id;

        // Act
        var response = await _client.GetAsync($"/api/events/{eventId}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        await Assert.That(ev!.Id).IsEqualTo(eventId);
        await Assert.That(ev.EventName).IsEqualTo("Berlin Marathon");
        await Assert.That(ev.Splits.Count).IsEqualTo(2);
        await Assert.That(ev.Splits[0].SplitLabel).IsEqualTo("10km");
        await Assert.That(ev.Splits[1].SplitLabel).IsEqualTo("21km");
    }

    [Test]
    public async Task GetEventById_Returns404_WhenEventDoesNotExist()
    {
        // Arrange
        var token = await GetTokenAsync("getbyid-notfound@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/events/{nonExistentId}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetEventById_Returns404_WhenEventBelongsToAnotherUser()
    {
        // Arrange — user A uploads an event
        var tokenA = await GetTokenAsync("getbyid-owner@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        const string json = """
            [{ "event_type": "GENERIC", "event_name": "Test 10K", "event_date": "2024-06-01",
               "completion": "FINISHED", "elapsed_secs": 2900 }]
            """;
        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        var eventId = uploaded![0].Id;

        // Act — user B tries to fetch it
        var tokenB = await GetTokenAsync("getbyid-thief@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var response = await _client.GetAsync($"/api/events/{eventId}");

        // Assert — 404 not 403, to avoid leaking ownership (OWASP A01)
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetEventById_Returns401_WhenNotAuthenticated()
    {
        var response = await _client.GetAsync($"/api/events/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task detail_includes_average_splits_across_same_type_finished_events()
    {
        // Arrange — two HYROX events sharing the "SkiErg" label: (300 + 400) / 2 = 350
        var token = await GetTokenAsync("avg-splits@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string json = """
            [
              {
                "event_type": "HYROX", "event_name": "HYROX Berlin", "event_date": "2024-11-10",
                "completion": "FINISHED", "elapsed_secs": 5400,
                "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 300, "cumulative_secs": 300 }]
              },
              {
                "event_type": "HYROX", "event_name": "HYROX Hamburg", "event_date": "2025-02-15",
                "completion": "FINISHED", "elapsed_secs": 5600,
                "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 400, "cumulative_secs": 400 }]
              }
            ]
            """;
        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();

        // Act
        var detail = await _client.GetFromJsonAsync<EventDetailResponse>($"/api/events/{uploaded![0].Id}");

        // Assert
        await Assert.That(detail!.AverageSplits.Count).IsEqualTo(1);
        await Assert.That(detail.AverageSplits[0].Label).IsEqualTo("SkiErg");
        await Assert.That(detail.AverageSplits[0].AvgSecs).IsEqualTo(350);
    }

    [Test]
    public async Task average_splits_exclude_non_finished_events()
    {
        // Arrange — the DNF's 900s SkiErg must not drag the average
        var token = await GetTokenAsync("avg-dnf@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string json = """
            [
              {
                "event_type": "HYROX", "event_name": "Good Race", "event_date": "2024-11-10",
                "completion": "FINISHED", "elapsed_secs": 5400,
                "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 300, "cumulative_secs": 300 }]
              },
              {
                "event_type": "HYROX", "event_name": "Bad Day", "event_date": "2025-02-15",
                "completion": "DNF", "elapsed_secs": 900,
                "splits": [{ "split_type": "STATION", "split_label": "SkiErg", "split_secs": 900, "cumulative_secs": 900 }]
              }
            ]
            """;
        var uploadResponse = await _client.PostAsync("/api/events/upload", BuildJsonUpload(json));
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();
        var finishedId = uploaded!.Single(e => e.EventName == "Good Race").Id;

        // Act
        var detail = await _client.GetFromJsonAsync<EventDetailResponse>($"/api/events/{finishedId}");

        // Assert
        await Assert.That(detail!.AverageSplits.Single(a => a.Label == "SkiErg").AvgSecs).IsEqualTo(300);
    }

    [Test]
    public async Task average_splits_is_empty_when_no_same_type_event_has_splits()
    {
        // Arrange
        var token = await GetTokenAsync("avg-empty@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        const string csv = "MARATHON,No Splits Race,2024-09-29,FINISHED,14400";
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "events.csv");
        var uploadResponse = await _client.PostAsync("/api/events/upload", content);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<List<EventResponse>>();

        // Act
        var detail = await _client.GetFromJsonAsync<EventDetailResponse>($"/api/events/{uploaded![0].Id}");

        // Assert
        await Assert.That(detail!.AverageSplits.Count).IsEqualTo(0);
    }
}
