using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pacevite.Api.Features.Auth;
using Pacevite.Api.Features.Events;
using Pacevite.Api.Infrastructure.Auth;
using Pacevite.Api.Infrastructure.Parsing;
using Pacevite.Api.Infrastructure.Persistence;
using Pacevite.Api.Pipeline;
using Scalar.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = builder.Configuration.GetValue<int>("RateLimit:Auth:PermitLimit", defaultValue: 10);
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
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapGroup("/api").MapAuthEndpoints();
app.MapGroup("/api/events").RequireAuthorization().MapEventEndpoints();

app.Run();

// Exposed for integration test WebApplicationFactory
public partial class Program;
