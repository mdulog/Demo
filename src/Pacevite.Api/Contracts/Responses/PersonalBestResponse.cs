namespace Pacevite.Api.Contracts.Responses;

public sealed record PersonalBestResponse(
    string EventType,
    Guid EventId,
    string EventName,
    DateOnly EventDate,
    int ElapsedSecs);
