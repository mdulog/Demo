using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Pacevite.Api.Contracts.Requests;
using Pacevite.Api.Contracts.Responses;
using Pacevite.Api.Features.Events.CreateEvent;
using Pacevite.Api.Features.Events.DeleteEvent;
using Pacevite.Api.Features.Events.GetEventById;
using Pacevite.Api.Features.Events.GetEvents;
using Pacevite.Api.Features.Events.GetPersonalBests;
using Pacevite.Api.Features.Events.GetPrediction;
using Pacevite.Api.Features.Events.GetTimeline;
using Pacevite.Api.Features.Events.PredictionCoaching;
using Pacevite.Api.Features.Events.Upload;

namespace Pacevite.Api.Features.Events;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/upload", UploadAsync).WithName("UploadEvents").DisableAntiforgery();
        app.MapPost("/", CreateAsync).WithName("CreateEvent");
        app.MapGet("/", GetEventsAsync).WithName("GetEvents");
        app.MapGet("/personal-bests", GetPersonalBestsAsync).WithName("GetPersonalBests");
        app.MapGet("/timeline", GetTimelineAsync).WithName("GetEventsTimeline");
        app.MapGet("/prediction", GetPredictionAsync).WithName("GetPrediction");
        app.MapGet("/prediction/coaching", GetPredictionCoachingAsync).WithName("GetPredictionCoaching");
        app.MapGet("/{id:guid}", GetEventByIdAsync).WithName("GetEventById");
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
            new UploadEventCommand(userId, file.ContentType, file.FileName, stream), ct);

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<EventResponse>, Conflict<string>>> CreateAsync(
        CreateEventRequest request,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = GetUserId(user);

        var splits = (request.Splits ?? [])
            .Select(s => new CreateEventSplitInput(s.SplitType, s.SplitLabel, s.SplitSecs, s.CumulativeSecs))
            .ToList();

        var result = await mediator.Send(
            new CreateEventCommand(
                userId,
                request.EventType,
                request.EventName,
                request.EventDate,
                request.Completion,
                request.ElapsedSecs,
                request.OverallRank,
                request.AgeGroupRank,
                request.FieldSize,
                request.AgeGroupFieldSize,
                splits),
            ct);

        return result is null
            ? TypedResults.Conflict($"An event named '{request.EventName}' already exists for {request.EventDate}.")
            : TypedResults.Created($"/api/events/{result.Id}", result);
    }

    private static async Task<Ok<PagedEventsResponse>> GetEventsAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct,
        string? eventType = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? search = null,
        string? cursor = null,
        int limit = GetEventsQuery.DefaultLimit)
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(
            new GetEventsQuery(userId, eventType, from, to, search, cursor, limit), ct);
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

    private static async Task<Ok<IReadOnlyList<TimelineEntryResponse>>> GetTimelineAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct,
        string? eventType = null)
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(new GetTimelineQuery(userId, eventType), ct);
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

    private static async Task<Results<Ok<EventResponse>, NotFound>> GetEventByIdAsync(
        Guid id,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct)
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(new GetEventByIdQuery(id, userId), ct);

        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<PredictionResponse>, Conflict<string>>> GetPredictionAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken ct,
        string eventType = "")
    {
        var userId = GetUserId(user);
        var result = await mediator.Send(new GetPredictionQuery(userId, eventType), ct);
        return result is null
            ? TypedResults.Conflict($"Insufficient data — ensure you have at least 2 finished {eventType.ToUpperInvariant()} events on different dates.")
            : TypedResults.Ok(result);
    }

    private static async Task GetPredictionCoachingAsync(
        HttpContext context,
        ClaimsPrincipal user,
        PredictionCoachingHandler handler,
        CancellationToken ct,
        string eventType = "")
    {
        await handler.HandleAsync(context, GetUserId(user), eventType, ct);
    }

    private static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User ID claim missing from token.");
}
