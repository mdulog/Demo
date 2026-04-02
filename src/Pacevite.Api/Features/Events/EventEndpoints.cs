using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Features.Events.DeleteEvent;
using Pacevite.Api.Features.Events.GetEvents;
using Pacevite.Api.Features.Events.GetPersonalBests;
using Pacevite.Api.Features.Events.Upload;

namespace Pacevite.Api.Features.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/upload", UploadAsync).WithName("UploadEvents").DisableAntiforgery();
        app.MapGet("/", GetEventsAsync).WithName("GetEvents");
        app.MapGet("/personal-bests", GetPersonalBestsAsync).WithName("GetPersonalBests");
        app.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteEvent");

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<EventResponse>>, BadRequest<string>>> UploadAsync(
        IFormFile file,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("User ID claim missing from token.");

        await using var stream = file.OpenReadStream();

        var result = await mediator.Send(
            new UploadEventCommand(userId, file.ContentType, stream), ct);

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<IReadOnlyList<EventResponse>>> GetEventsAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct,
        string? eventType = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(new GetEventsQuery(userId, eventType, from, to), ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<IReadOnlyList<PersonalBestResponse>>> GetPersonalBestsAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(new GetPersonalBestsQuery(userId), ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        await mediator.Send(new DeleteEventCommand(id, userId), ct);

        // Always return 204 — revealing "not found vs not yours" would leak ownership info (A01).
        return TypedResults.NoContent();
    }

    private static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User ID claim missing from token.");
}
