namespace Pacevite.Api.Domain.Entities;

public class EventSplit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid EventId { get; init; }
    public required string SplitType { get; set; }
    public required string SplitLabel { get; set; }
    public required int SplitSecs { get; set; }
    public required int CumulativeSecs { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = [];

    public Event Event { get; init; } = null!;
}
