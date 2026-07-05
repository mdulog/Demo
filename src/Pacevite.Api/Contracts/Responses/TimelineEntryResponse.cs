namespace Pacevite.Api.Contracts.Responses;

public sealed record TimelineEntryResponse(
    Guid Id,
    DateOnly EventDate,
    string EventType,
    int ElapsedSecs,
    string Completion);
