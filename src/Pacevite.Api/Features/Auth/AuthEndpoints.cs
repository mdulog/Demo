using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Features.Auth.Login;
using Pacevite.Api.Features.Auth.Register;

namespace Pacevite.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth")
            .RequireRateLimiting("auth")
            .AllowAnonymous();

        group.MapPost("/register", RegisterAsync).WithName("Register");
        group.MapPost("/login", LoginAsync).WithName("Login");

        return app;
    }

    // TypedResults gives OpenAPI the return type metadata for free — it reads the
    // generic type parameters to infer schema without extra .Produces() attributes.
    private static async Task<Results<Created<AuthResponse>, ProblemHttpResult>> RegisterAsync(
        [FromBody] RegisterRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new RegisterCommand(request.Email, request.Password), ct);

        if (result.IsSuccess)
            return TypedResults.Created($"/api/users/{result.UserId}", new AuthResponse(result.UserId!, result.Email!, result.Token!));

        var statusCode = result.IsDuplicate
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;

        return TypedResults.Problem(result.Error, statusCode: statusCode);
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> LoginAsync(
        [FromBody] LoginRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new LoginCommand(request.Email, request.Password), ct);

        return result.IsSuccess
            ? TypedResults.Ok(new AuthResponse(result.UserId!, result.Email!, result.Token!))
            : TypedResults.Unauthorized();
    }
}
