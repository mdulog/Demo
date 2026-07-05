namespace Pacevite.Api.Contracts.Responses;

// Shape matches chartUtils.ts's AverageSplit interface: { label, avgSecs }.
public sealed record AverageSplitResponse(string Label, int AvgSecs);

public sealed record EventDetailResponse(
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
    bool NeedsEnrichment,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EventSplitResponse> Splits,
    IReadOnlyList<AverageSplitResponse> AverageSplits);
