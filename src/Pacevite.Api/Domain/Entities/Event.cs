using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Domain.Entities;

public class Event
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required EventType EventType { get; init; }
    public required string EventName { get; set; }
    public required DateOnly EventDate { get; set; }
    public required CompletionStatus Completion { get; set; }
    public required int ElapsedSecs { get; set; }
    public int? OverallRank { get; set; }
    public int? AgeGroupRank { get; set; }
    public int? FieldSize { get; set; }
    public int? AgeGroupFieldSize { get; set; }

    // JSONB columns — stored as structured metadata without schema migration per event type
    public Dictionary<string, object> Location { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = [];

    public string Source { get; init; } = "MANUAL";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<EventSplit> Splits { get; init; } = [];
}
