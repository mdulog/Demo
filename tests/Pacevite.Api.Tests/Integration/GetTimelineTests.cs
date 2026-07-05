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
public sealed class GetTimelineTests
{
    private PostgreSqlContainer _postgres = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [Before(Test)]
    public async Task SetUpAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("pacevite_timeline_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

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

    [After(Test)]
    public async Task TearDownAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> GetTokenAsync(string email)
    {
        var regResponse = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "P@ssword1!"));
        var body = await regResponse.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }

    private async Task UploadCsvAsync(string csv)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "events.csv");
        var response = await _client.PostAsync("/api/events/upload", content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task timeline_returns_all_events_ascending_by_date_without_splits()
    {
        // Arrange
        var token = await GetTokenAsync("timeline@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("""
            MARATHON,Race Late,2024-09-10,FINISHED,14400
            MARATHON,Race Early,2024-01-10,FINISHED,15000
            HYROX,Hyrox One,2024-05-10,DNF,5400
            """);

        // Act
        var response = await _client.GetAsync("/api/events/timeline");
        var raw = await response.Content.ReadAsStringAsync();
        var entries = await _client.GetFromJsonAsync<List<TimelineEntryResponse>>("/api/events/timeline");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(entries!.Count).IsEqualTo(3);
        await Assert.That(entries[0].EventDate).IsEqualTo(new DateOnly(2024, 1, 10));
        await Assert.That(entries[2].EventDate).IsEqualTo(new DateOnly(2024, 9, 10));
        await Assert.That(entries[1].EventType).IsEqualTo("HYROX");
        await Assert.That(entries[1].Completion).IsEqualTo("DNF");
        await Assert.That(raw.Contains("\"splits\"")).IsFalse();
    }

    [Test]
    public async Task timeline_filters_by_event_type()
    {
        // Arrange
        var token = await GetTokenAsync("timeline-filter@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await UploadCsvAsync("""
            MARATHON,Race One,2024-01-10,FINISHED,15000
            HYROX,Hyrox One,2024-05-10,FINISHED,5400
            """);

        // Act
        var entries = await _client.GetFromJsonAsync<List<TimelineEntryResponse>>("/api/events/timeline?eventType=HYROX");

        // Assert
        await Assert.That(entries!.Count).IsEqualTo(1);
        await Assert.That(entries[0].EventType).IsEqualTo("HYROX");
    }

    [Test]
    public async Task timeline_is_scoped_to_the_authenticated_user()
    {
        // Arrange
        var tokenA = await GetTokenAsync("timeline-a@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        await UploadCsvAsync("MARATHON,Private Race,2024-01-10,FINISHED,15000");

        var tokenB = await GetTokenAsync("timeline-b@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        // Act
        var entries = await _client.GetFromJsonAsync<List<TimelineEntryResponse>>("/api/events/timeline");

        // Assert
        await Assert.That(entries!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task timeline_without_token_returns_401()
    {
        // Act
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/events/timeline");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task timeline_with_invalid_event_type_returns_400()
    {
        // Arrange
        var token = await GetTokenAsync("timeline-bad-type@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/events/timeline?eventType=ULTRA_SPRINT");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
