using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pacevite.Api.Features.Auth;
using Pacevite.Api.Features.Events;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Parsing;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Infrastructure.OpenApi;
using Pacevite.Api.Pipeline;
using Scalar.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

// ── Reverse Proxy ─────────────────────────────────────────────────────────────
// Restrict to RFC-1918 private ranges so spoofed X-Forwarded-* headers from
// public clients are rejected (OWASP A01). Adjust to match your proxy's network.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
    options.KnownProxies.Add(System.Net.IPAddress.Loopback);
    options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
});

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddEntityFrameworkStores<AppDbContext>();

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

// ── Rate Limiting (OWASP A07 — auth endpoint brute force protection) ──────────
// PermitLimit is configurable so Development/test overrides can set a high
// value without disabling the middleware entirely.
builder.Services.AddRateLimiter(options =>
{
    var permitLimit = builder.Configuration.GetValue<int>("RateLimit:Auth:PermitLimit", 10);

    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = permitLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Exception Handling ────────────────────────────────────────────────────────
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddProblemDetails();

// ── Mediator + Validation Pipeline ───────────────────────────────────────────
builder.Services.AddMediator(options =>
{
    options.Assemblies = [typeof(Program).Assembly];
    options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// ── Event Parsers (registered as IEventParser — endpoint dispatches by content type) ──
builder.Services.AddSingleton<IEventParser, CsvEventParser>();
builder.Services.AddSingleton<IEventParser, JsonEventParser>();

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<ForwardedPrefixTransformer>());

var app = builder.Build();

// ── Database Migration ────────────────────────────────────────────────────────
// Applies pending migrations automatically in Development so the API is
// self-bootstrapping for local runs and E2E tests without a manual migration step.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// ── Reverse Proxy middleware ───────────────────────────────────────────────────
// Must be first — rewrites Scheme, Host, and RemoteIpAddress before anything reads them.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealthJson });

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapGroup("/api").MapAuthEndpoints();
app.MapGroup("/api/events").RequireAuthorization().MapEventEndpoints();

app.Run();

static Task WriteHealthJson(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        duration_ms = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.ToDictionary(
            e => e.Key,
            e => new
            {
                status = e.Value.Status.ToString(),
                duration_ms = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                error = e.Value.Exception?.Message,
            })
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

// Exposed for integration test WebApplicationFactory
public partial class Program;
