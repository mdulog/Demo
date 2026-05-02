using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Features.Auth.Login;
using Pacevite.Api.Features.Auth.Logout;
using Pacevite.Api.Features.Auth.Refresh;
using Pacevite.Api.Features.Auth.Register;

namespace Pacevite.Api.Features.Auth;

public static class AuthEndpoints
{
    private const string RefreshTokenCookie = "refreshToken";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth")
            .RequireRateLimiting("auth");

        group.MapPost("/register", RegisterAsync).WithName("Register").AllowAnonymous();
        group.MapPost("/login", LoginAsync).WithName("Login").AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).WithName("Refresh").AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).WithName("Logout").RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<AuthResponse>, ProblemHttpResult>> RegisterAsync(
        [FromBody] RegisterRequest request,
        IMediator mediator,
        HttpContext httpContext,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var result = await mediator.Send(new RegisterCommand(request.Email, request.Password), ct);

        if (result.IsSuccess)
        {
            SetRefreshCookie(httpContext, result.RefreshToken!, env.IsProduction());
            return TypedResults.Created($"/api/users/{result.UserId}", new AuthResponse(result.UserId!, result.Email!, result.Token!));
        }

        var statusCode = result.IsDuplicate
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;

        return TypedResults.Problem(result.Error, statusCode: statusCode);
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> LoginAsync(
        [FromBody] LoginRequest request,
        IMediator mediator,
        HttpContext httpContext,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var result = await mediator.Send(new LoginCommand(request.Email, request.Password), ct);

        if (result.IsSuccess)
        {
            SetRefreshCookie(httpContext, result.RefreshToken!, env.IsProduction());
            return TypedResults.Ok(new AuthResponse(result.UserId!, result.Email!, result.Token!));
        }

        return TypedResults.Unauthorized();
    }

    private static async Task<Results<Ok<RefreshResponse>, UnauthorizedHttpResult>> RefreshAsync(
        IMediator mediator,
        HttpContext httpContext,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var rawToken = httpContext.Request.Cookies[RefreshTokenCookie];
        var result = await mediator.Send(new RefreshCommand(rawToken), ct);

        if (result.IsSuccess)
        {
            SetRefreshCookie(httpContext, result.NewRefreshToken!, env.IsProduction());
            return TypedResults.Ok(new RefreshResponse(result.Token!));
        }

        ClearRefreshCookie(httpContext);
        return TypedResults.Unauthorized();
    }

    private static async Task<NoContent> LogoutAsync(
        IMediator mediator,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var rawToken = httpContext.Request.Cookies[RefreshTokenCookie];
        await mediator.Send(new LogoutCommand(rawToken), ct);
        ClearRefreshCookie(httpContext);
        return TypedResults.NoContent();
    }

    private static void SetRefreshCookie(HttpContext httpContext, string rawToken, bool isProduction)
    {
        httpContext.Response.Cookies.Append(RefreshTokenCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromDays(7)
        });
    }

    private static void ClearRefreshCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions { Path = "/api/auth" });
    }
}
