namespace Pacevite.Api.Contracts.Responses;

public sealed record EventResponse(
    Guid Id,
    string EventType,
    string EventName,
    DateOnly EventDate,
    string Completion,
    int ElapsedSecs,
    int? OverallRank,
    int? AgeGroupRank,
    int? FieldSize,
    int? AgeGroupFieldSize,
    string Source,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EventSplitResponse> Splits);

public sealed record EventSplitResponse(
    Guid Id,
    string SplitType,
    string SplitLabel,
    int SplitSecs,
    int CumulativeSecs);
