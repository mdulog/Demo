using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Pacevite.Api.Tests.Integration;

// One container shared across all tests in this class — TUnit honours ClassConstructor
// and class teardown via IAsyncDisposable.
public sealed class AuthEndpointsTests : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres;
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public AuthEndpointsTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .WithDatabase("pacevite_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _postgres.StartAsync().GetAwaiter().GetResult();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    // Replace DbContext with test container connection
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor is not null)
                        services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));

                    // Apply migrations against the test DB
                    using var scope = services.BuildServiceProvider().CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.Migrate();
                });

                // Inject required JWT config
                host.UseSetting("Jwt:Secret", "super-secret-key-for-testing-only-32c");
                host.UseSetting("Jwt:Issuer", "pacevite-test");
                host.UseSetting("Jwt:Audience", "pacevite-test");
            });

        _client = _factory.CreateClient();
    }

    [Test]
    public async Task Register_WithValidCredentials_Returns201WithToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("test@example.com", "P@ssword1!"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(string.IsNullOrWhiteSpace(body!.Token)).IsFalse();
        await Assert.That(body.Email).IsEqualTo("test@example.com");
    }

    [Test]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        var request = new RegisterRequest("duplicate@example.com", "P@ssword1!");

        await _client.PostAsJsonAsync("/api/auth/register", request);
        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Register_WithInvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("not-an-email", "P@ssword1!"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Register_WithShortPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("short@example.com", "abc"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Login_WithValidCredentials_Returns200WithToken()
    {
        const string email = "login-valid@example.com";
        const string password = "P@ssword1!";

        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(string.IsNullOrWhiteSpace(body!.Token)).IsFalse();
    }

    [Test]
    public async Task Login_WithWrongPassword_Returns401()
    {
        const string email = "login-wrong@example.com";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "P@ssword1!"));

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "WrongPassword1!"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@example.com", "P@ssword1!"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
